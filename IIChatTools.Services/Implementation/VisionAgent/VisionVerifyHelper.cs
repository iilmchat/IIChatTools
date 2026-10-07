using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// KI-162 (Coordinate-then-Verify): общий helper для уточнения координат
    /// элемента через второй VL-вызов на кропе.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-162 + KI-162-2). Используется:
    /// <list type="bullet">
    ///   <item><c>VisionAgentTool.TryVerifyCoordinatesAsync</c> — одиночные click-actions;</item>
    ///   <item><c>VisionAgentService.ResolveCoordinatesAsync</c> — run_task loop.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Логика:</b> crop + upscale → второй VL-вызов → при <c>Found=true</c>
    /// и <c>Confidence≥MinConfidence</c> — возвращаем уточнённые координаты.
    /// Иначе — fallback на bounds-center (KI-190).
    /// </para>
    /// <para>
    /// <b>Windows-only:</b> <see cref="VisionImageResizer.CropAndUpscale"/> использует
    /// <c>System.Drawing.Common</c>. На Linux — грациозный fallback.
    /// </para>
    /// </remarks>
    public static class VisionVerifyHelper
    {
        /// <summary>
        /// Пытается уточнить координаты элемента через второй VL-вызов.
        /// При любой ошибке / низкой уверенности — fallback на bounds-center.
        /// </summary>
        /// <param name="png">Исходный PNG скриншота (в системе downscale'нутого изображения).</param>
        /// <param name="element">UI-элемент с bounds (для crop) и label (для промпта).</param>
        /// <param name="targetId">Id элемента (fallback для описания, если label пуст).</param>
        /// <param name="visionLlm">Клиент Vision LLM (для второго вызова).</param>
        /// <param name="options">Настройки Verify (Enabled / CropPadding / Upscale / MinConfidence).</param>
        /// <param name="logger">Логгер для диагностики.</param>
        /// <param name="ct">Токен отмены.</param>
        /// <returns>Уточнённые координаты (или bounds-center при fallback).</returns>
        public static async Task<(int x, int y)> TryVerifyAsync(
            byte[] png,
            UiElementDto element,
            string targetId,
            IVisionLlmClient visionLlm,
            VisionVerifyOptions options,
            ILogger logger,
            CancellationToken ct)
        {
            // 1. Fallback (bounds-center / center).
            var (fallbackX, fallbackY) = GetFallbackCoordinates(element);

            // 2. Нечего верифицировать без bounds.
            if (element?.Bounds == null
                || element.Bounds.W <= 0
                || element.Bounds.H <= 0
                || png == null
                || png.Length == 0)
            {
                return (fallbackX, fallbackY);
            }

            // 3. Non-Windows — CropAndUpscale недоступен.
            if (!OperatingSystem.IsWindows())
            {
                logger.LogDebug(
                    "VisionVerifyHelper: не Windows — fallback на bounds-center ({X},{Y})",
                    fallbackX, fallbackY);
                return (fallbackX, fallbackY);
            }

            try
            {
#pragma warning disable CA1416 // CropAndUpscale — [SupportedOSPlatform("windows")]; guard выше.
                var (cropPng, cropX, cropY, cropW, cropH) = VisionImageResizer.CropAndUpscale(
                    png,
                    element.Bounds.X, element.Bounds.Y,
                    element.Bounds.W, element.Bounds.H,
                    options.CropPadding,
                    options.Upscale);
#pragma warning restore CA1416

                // KI-162-fix: targetDescription — через IsNullOrWhiteSpace, а не ??.
                // Причина: element.Value = "" (пустая строка) — `"" ?? x` даёт "",
                // а не x. В логе был target='' при доступном fallback.
                var targetDesc = !string.IsNullOrWhiteSpace(element.Label)
                    ? element.Label
                    : (!string.IsNullOrWhiteSpace(element.Value)
                        ? element.Value
                        : targetId);

                var result = await visionLlm.VerifyTargetAsync(
                        cropPng, targetDesc, element.Bounds, ct)
                    .ConfigureAwait(false);

                if (result != null && result.Found
                    && result.Confidence >= options.MinConfidence)
                {
                    // KI-162-fix2 (v1.13.x): VL-координаты на кропе
                    // НЕ точнее bounds-center. Наблюдение smoke 2026-10-07:
                    //   bounds-center: (695, 385)  ← из KI-190 (VL-bounds)
                    //   verify:        (675, 387)  ← ошибка -20 px → промах
                    // Причина: Qwen2.5-VL-7B даёт ±20-40 px ошибку даже
                    // на увеличённом кропе. Confidence 0.90 при этом — ложная.
                    //
                    // Решение: НЕ доверять verify-координатам. Использовать
                    // bounds-center (KI-190) как есть. Verify нужен только для
                    // подтверждения, что элемент найден (Found=true) — и то
                    // необязательно, т.к. bounds пришли из того же VL.
                    //
                    // В логе — оставляем verify-координаты для диагностики,
                    // но клик идёт по bounds-center.
                    var upscale = Math.Max(1, options.Upscale);
                    var maxCropX = cropW * upscale;
                    var maxCropY = cropH * upscale;

                    if (result.X < 0 || result.X > maxCropX
                        || result.Y < 0 || result.Y > maxCropY)
                    {
                        logger.LogWarning(
                            "VisionVerifyHelper: VL координаты вне кропа " +
                            "({Vx},{Vy} при {W}×{H}) — используем bounds-center",
                            result.X, result.Y, maxCropX, maxCropY);
                    }

                    var origX = cropX + (int)Math.Round((double)result.X / upscale);
                    var origY = cropY + (int)Math.Round((double)result.Y / upscale);

                    logger.LogInformation(
                        "VisionVerifyHelper: verify VL({Vx},{Vy}) → orig({Ox},{Oy}) " +
                        "conf={C:F2}, но используем bounds-center ({Fx},{Fy}) — " +
                        "verify даёт ±20 px noise, bounds-center надёжнее (KI-190)",
                        result.X, result.Y, origX, origY, result.Confidence,
                        fallbackX, fallbackY);

                    // Возвращаем bounds-center, НЕ verify-координаты.
                    return (fallbackX, fallbackY);
                }

                logger.LogDebug(
                    "VisionVerifyHelper: fallback — Found={Found}, " +
                    "conf={C:F2} < {Min}, error={Err}",
                    result?.Found ?? false, result?.Confidence ?? 0,
                    options.MinConfidence, result?.Error ?? "(нет)");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "VisionVerifyHelper: Verify упал — fallback на bounds-center");
            }

            return (fallbackX, fallbackY);
        }

        /// <summary>
        /// Вычисляет fallback-координаты: bounds-center, иначе center, иначе (0,0).
        /// </summary>
        private static (int x, int y) GetFallbackCoordinates(UiElementDto element)
        {
            if (element?.Bounds != null && element.Bounds.W > 0 && element.Bounds.H > 0)
            {
                return (element.Bounds.X + element.Bounds.W / 2,
                        element.Bounds.Y + element.Bounds.H / 2);
            }

            if (element?.Center != null)
            {
                return (element.Center.X, element.Center.Y);
            }

            return (0, 0);
        }
    }
}