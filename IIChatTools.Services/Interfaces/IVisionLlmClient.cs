using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Клиент Vision LLM — модели, которая описывает UI со скриншота
    /// и возвращает структурированный <see cref="ScreenDescriptionDto"/>.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Фаза 1). См. DESIGN § 3.2, § 4.4.
    /// </remarks>
    public interface IVisionLlmClient
    {
        /// <summary>
        /// Отправляет PNG скриншота в Vision LLM, возвращает описание UI
        /// (с текстовым summary + списком <c>ui_elements[]</c>).
        /// </summary>
        /// <param name="screenshotPng">PNG-байты скриншота.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Описание экрана. Fallback: <c>Description</c> = текстовая часть ответа, <c>UiElements</c> = пустой список.</returns>
        Task<ScreenDescriptionDto> DescribeAsync(
            byte[] screenshotPng,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Coordinate-then-Verify (KI-162): уточняет координаты центра
        /// UI-элемента на кропе (с upscale). Второй VL-вызов.
        /// </summary>
        /// <param name="croppedPng">
        /// PNG-кроп вокруг элемента (обычно upscale'нутый ×2).
        /// Готовится вызывающим кодом через <c>VisionImageResizer.CropAndUpscale</c>.
        /// </param>
        /// <param name="targetDescription">
        /// Описание элемента для промпта VL: label («Кнопка Поиск») или
        /// id (<c>search_btn</c>), если label отсутствует.
        /// </param>
        /// <param name="originalBounds">
        /// Оригинальные bounds элемента в системе исходного PNG
        /// (для контекста / логирования). Может быть <c>null</c>.
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// <see cref="VerifyTargetResultDto"/> с координатами <b>в системе
        /// исходного PNG</b>. При любой ошибке — <c>Found = false</c> с
        /// заполненным <c>Error</c>; вызывающий код делает fallback.
        /// </returns>
        Task<VerifyTargetResultDto> VerifyTargetAsync(
            byte[] croppedPng,
            string targetDescription,
            UiElementBoundsDto originalBounds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Готов ли клиент к работе (например, LM Studio доступен и модель загружена).
        /// </summary>
        bool IsReady { get; }
    }
}