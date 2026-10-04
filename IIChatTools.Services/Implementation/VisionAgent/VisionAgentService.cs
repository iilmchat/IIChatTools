using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Оркестратор loop'а Vision Agent:
    /// <c>screenshot → describe → plan → validate → act → repeat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.2). См. DESIGN § 3.3.
    /// </para>
    /// <para>
    /// <b>Резолв координат:</b> если действие содержит <c>target</c>
    /// (семантический id) — loop ищет элемент в <c>screen.UiElements</c>
    /// и берёт <c>Center.X/Y</c>. Это единственный способ «перевести»
    /// семантику LLM в пиксели backend'а.
    /// </para>
    /// <para>
    /// <b>Что НЕ делает сейчас (отложено):</b>
    /// <list type="bullet">
    ///   <item>timeout <c>MaxTaskSeconds</c> — Ф6.3;</item>
    ///   <item>rate limiter — Ф6.4;</item>
    ///   <item>сохранение скриншотов в workspace — Ф6.5;</item>
    ///   <item>overlay — Ф6.7.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class VisionAgentService : IVisionAgentService
    {
        /// <summary>Короткая пауза после действия, если LLM не задала явный wait.</summary>
        private const int DefaultWaitMs = 1000;

        private readonly IVisionBackend _backend;
        private readonly IVisionLlmClient _visionLlm;
        private readonly IPlannerLlmClient _plannerLlm;
        private readonly IVisionActionValidator _validator;
        private readonly IVisionRateLimiter _rateLimiter;
        private readonly IVisionScreenshotStore _screenshotStore;
        private readonly IVisionOverlayLauncher _overlayLauncher;
        private readonly VisionAgentOptions _options;
        private readonly ILogger<VisionAgentService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="backend">Backend (Local / Sandbox / RemoteVnc).</param>
        /// <param name="visionLlm">Vision LLM (описание UI).</param>
        /// <param name="plannerLlm">Planner LLM (следующее действие).</param>
        /// <param name="validator">Валидатор действий.</param>
        /// <param name="rateLimiter">Rate limiter (5 задач / 5 мин per-user).</param>
        /// <param name="screenshotStore">Хранилище скриншотов (workspace).</param>
        /// <param name="overlayLauncher">Launcher on-screen indicator'а (Ф6.8: Noop).</param>
        /// <param name="options">Настройки Vision Agent.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public VisionAgentService(
            IVisionBackend backend,
            IVisionLlmClient visionLlm,
            IPlannerLlmClient plannerLlm,
            IVisionActionValidator validator,
            IVisionRateLimiter rateLimiter,
            IVisionScreenshotStore screenshotStore,
            IVisionOverlayLauncher overlayLauncher,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionAgentService> logger)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _visionLlm = visionLlm ?? throw new ArgumentNullException(nameof(visionLlm));
            _plannerLlm = plannerLlm ?? throw new ArgumentNullException(nameof(plannerLlm));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _screenshotStore = screenshotStore ?? throw new ArgumentNullException(nameof(screenshotStore));
            _overlayLauncher = overlayLauncher ?? throw new ArgumentNullException(nameof(overlayLauncher));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<VisionTaskResultDto> RunTaskAsync(
            VisionTaskRequest request,
            int userId,
            CancellationToken cancellationToken = default)
        {
            // 1. Валидация.
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.Task))
            {
                throw new ArgumentException("VisionTaskRequest.Task не задан.", nameof(request));
            }

            // 2. Инициализация.
            var taskId = string.IsNullOrWhiteSpace(request.TaskId)
                ? "vt_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                : request.TaskId;

            // 2.1. Rate limit (Ф6.4): 5 задач / 5 мин per-user.
            var rateCheck = _rateLimiter.TryAcquire(userId);
            if (!rateCheck.Allowed)
            {
                _logger.LogWarning(
                    "VisionAgent[{TaskId}]: rate limit для user={UserId}, retry через {Sec}с",
                    taskId, userId, rateCheck.RetryAfterSeconds);

                var retryMsg = rateCheck.RetryAfterSeconds > 0
                    ? $" Попробуйте через {rateCheck.RetryAfterSeconds} с."
                    : string.Empty;

                return new VisionTaskResultDto
                {
                    Task = request.Task,
                    Backend = _backend.Name,
                    Success = false,
                    Error = "Превышен лимит запусков Vision Agent (5 задач / 5 минут)." + retryMsg,
                    Steps = new List<VisionStepDto>()
                };
            }

            var maxSteps = request.MaxSteps.HasValue && request.MaxSteps.Value > 0
                ? Math.Min(request.MaxSteps.Value, _options.Limits.MaxSteps)
                : _options.Limits.MaxSteps;

            var history = new List<VisionStepDto>();
            var result = new VisionTaskResultDto
            {
                Task = request.Task,
                Backend = _backend.Name,
                Steps = history,
                Success = false
            };

            var requestSw = Stopwatch.StartNew();

            // Ф6.3 (KI-131): общий timeout на всю задачу.
            // RULES § 4.48 — CancellationTokenSource.CancelAfter, а не HttpClient.Timeout.
            // Различаем: timeoutCts (внутренний) vs cancellationToken (внешний Stop).
            var maxSeconds = Math.Max(1, _options.Limits.MaxTaskSeconds);
            using var timeoutCts = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxSeconds));

            _logger.LogInformation(
                "VisionAgent[{TaskId}]: старт задачи (user={UserId}, maxSteps={MaxSteps}, timeout={Timeout}s, url={Url}): {Task}",
                taskId, userId, maxSteps, maxSeconds, request.Url ?? "(нет)", request.Task);

            // Ф6.8 (KI-131): overlay для on-screen indicator.
            // Noop-заглушка сейчас, WPF — в Ф6.7. Отключается, если:
            //  - overlay недоступен (IsAvailable = false)
            //  - в локальных настройках ShowOverlay = false
            var showOverlay = _overlayLauncher.IsAvailable &&
                              (_options.Backend?.Local?.ShowOverlay ?? false);
            IVisionOverlayHandle overlayHandle = null;
            if (showOverlay)
            {
                try
                {
                    overlayHandle = await _overlayLauncher.StartAsync(taskId, maxSteps, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VisionAgent[{TaskId}]: не удалось запустить overlay", taskId);
                    overlayHandle = null;
                }
            }

            // Ф6.7 (KI-131): STOP от overlay (кнопка / ESC) — отдельный канал отмены.
            // Связываем его с timeoutCts: любая из причин отменяет loop.
            // Приоритет STOP над timeout: см. catch-блоки ниже — STOP проверяется первым.
            var overlayStopToken = overlayHandle?.StopToken ?? CancellationToken.None;
            using var effectiveCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, overlayStopToken);

            try
            {
                // 3. Открыть стартовый URL (если задан).
                if (!string.IsNullOrWhiteSpace(request.Url))
                {
                    try
                    {
                        await _backend.OpenAsync(request.Url, effectiveCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "VisionAgent[{TaskId}]: OpenAsync упал на '{Url}'", taskId, request.Url);
                        result.Error = $"Не удалось открыть URL: {ex.Message}";
                        requestSw.Stop();
                        result.TotalDurationMs = requestSw.ElapsedMilliseconds;
                        return result;
                    }
                }

                // 4. Main loop.
                var plan = new List<string>();   // пока пустой; LLM может сам планировать.
                for (int step = 1; step <= maxSteps; step++)
                {
                    effectiveCts.Token.ThrowIfCancellationRequested();

                    // Ф6.8: прогресс в overlay (если включён).
                    try
                    {
                        overlayHandle?.UpdateProgress(step, maxSteps, "screenshot");
                    }
                    catch { /* overlay не критичен */ }

                    // 4.1. Скриншот.
                    byte[] png;
                    try
                    {
                        png = await _backend.ScreenshotAsync(effectiveCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // KI-152: пробрасываем отмену наверх — внешний catch разберётся,
                        // это timeout / overlay-STOP / внешний cancel.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "VisionAgent[{TaskId}]: ScreenshotAsync упал", taskId);
                        result.Error = $"Ошибка скриншота: {ex.Message}";
                        break;
                    }

                    // 4.1.1. Сохранить скриншот в workspace (Ф6.5).
                    // Best-effort: ошибка сохранения не прерывает loop.
                    try
                    {
                        var savedPath = await _screenshotStore.SaveAsync(
                                userId, taskId, step, png, effectiveCts.Token)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(savedPath))
                        {
                            result.FinalScreenshotPath = savedPath;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "VisionAgent[{TaskId}]: сохранение скриншота шага {Step} упало",
                            taskId, step);
                    }

                    // 4.2. Описание экрана.
                    ScreenDescriptionDto screen;
                    try
                    {
                        screen = await _visionLlm.DescribeAsync(png, effectiveCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // KI-152: пробрасываем отмену наверх.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "VisionAgent[{TaskId}]: DescribeAsync упал", taskId);
                        result.Error = $"Ошибка описания экрана: {ex.Message}";
                        break;
                    }

                    // 4.3. Следующее действие.
                    VisionActionDto action;
                    try
                    {
                        action = await _plannerLlm.PlanNextAsync(
                            request.Task, history, screen, plan, userId, effectiveCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // KI-152: пробрасываем отмену наверх.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "VisionAgent[{TaskId}]: PlanNextAsync упал", taskId);
                        result.Error = $"Ошибка планирования: {ex.Message}";
                        break;
                    }

                    if (action == null || string.IsNullOrWhiteSpace(action.Action))
                    {
                        result.Error = "Planner LLM вернула пустое действие.";
                        break;
                    }

                    var actionType = action.Action.Trim().ToLowerInvariant();

                    // 4.4. Валидация.
                    var validation = _validator.Validate(action, screen);
                    if (!validation.Success)
                    {
                        _logger.LogWarning(
                            "VisionAgent[{TaskId}]: действие отклонено валидатором: {Error}",
                            taskId, validation.Error);

                        // Записываем шаг с ошибкой и продолжаем (LLM перепланирует).
                        history.Add(new VisionStepDto
                        {
                            StepIndex = step,
                            Action = actionType,
                            Target = action.Target,
                            Text = action.Text,
                            LlmReason = action.Reason,
                            Error = validation.Error,
                            DurationMs = 0
                        });
                        continue;
                    }

                    // 4.5. Sanitized action (если валидатор clamp'нул).
                    var effectiveAction = validation.SanitizedAction ?? action;

                    // 4.6. done / fail — завершаем loop.
                    if (actionType == "done")
                    {
                        result.Success = true;
                        result.Summary = effectiveAction.Reason;
                        break;
                    }
                    if (actionType == "fail")
                    {
                        result.Success = false;
                        result.Error = string.IsNullOrWhiteSpace(effectiveAction.Reason)
                            ? "Planner LLM вернула fail."
                            : effectiveAction.Reason;
                        break;
                    }

                    // 4.7. Выполняем действие.
                    var stepSw = Stopwatch.StartNew();
                    string stepError = null;
                    try
                    {
                        await ExecuteActionAsync(_backend, actionType, effectiveAction, screen, effectiveCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;   // отмена — не глотаем.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "VisionAgent[{TaskId}]: шаг {Step} ({Action}) упал",
                            taskId, step, actionType);
                        stepError = ex.Message;
                    }
                    stepSw.Stop();

                    // 4.8. Записываем шаг.
                    history.Add(new VisionStepDto
                    {
                        StepIndex = step,
                        Action = actionType,
                        Target = effectiveAction.Target,
                        Text = effectiveAction.Text,
                        LlmReason = effectiveAction.Reason,
                        Error = stepError,
                        DurationMs = stepSw.ElapsedMilliseconds
                    });

                    // 4.9. Пауза на стабилизацию.
                    var delayMs = Math.Max(0, _options.Limits.ActionDelayMs);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, effectiveCts.Token).ConfigureAwait(false);
                    }
                }

                // 5. Если дошли до конца без done/fail — исчерпан лимит.
                if (!result.Success && string.IsNullOrEmpty(result.Error))
                {
                    result.Error = $"Исчерпан лимит шагов ({maxSteps}).";
                    _logger.LogWarning(
                        "VisionAgent[{TaskId}]: maxSteps={MaxSteps} исчерпан", taskId, maxSteps);
                }
            }
            catch (OperationCanceledException) when (overlayStopToken.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                // Ф6.7 (KI-131): STOP от overlay (кнопка / ESC) — плановая остановка.
                // Возвращаем результат — НЕ throw (пользователь сам остановил задачу,
                // это не «отмена стрима», а «отмена tool-call'а»).
                // Проверяется ПЕРВЫМ: если STOP и timeout сработали одновременно,
                // приоритет — у активного действия пользователя.
                result.Error = "Отменено пользователем (STOP в overlay).";
                _logger.LogInformation(
                    "VisionAgent[{TaskId}]: STOP от overlay, steps={Steps}",
                    taskId, history.Count);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                // Ф6.3: сработал ВНУТРЕННИЙ timeout (MaxTaskSeconds).
                // Возвращаем результат — НЕ throw (это плановая остановка задачи).
                result.Error = $"Превышен лимит времени задачи ({maxSeconds} с).";
                _logger.LogWarning(
                    "VisionAgent[{TaskId}]: timeout {Timeout}s, steps={Steps}",
                    taskId, maxSeconds, history.Count);
            }
            catch (OperationCanceledException)
            {
                // Внешняя отмена (Stop в чате / HTTP abort) — пробрасываем.
                result.Error = "Задача отменена.";
                _logger.LogInformation("VisionAgent[{TaskId}]: отменена", taskId);
                throw;
            }
            catch (Exception ex)
            {
                result.Error = $"Внутренняя ошибка: {ex.Message}";
                _logger.LogError(ex, "VisionAgent[{TaskId}]: необработанная ошибка", taskId);
            }
            finally
            {
                requestSw.Stop();
                result.TotalDurationMs = requestSw.ElapsedMilliseconds;

                // Ф6.8: финальное состояние overlay + закрытие.
                if (overlayHandle != null)
                {
                    try
                    {
                        // Ф6.7: различаем «Готово» / «Отменено пользователем (STOP)» / «Ошибка».
                        // STOP показываем нейтрально (не красным как ошибку), хотя Success=false.
                        var summary = result.Success
                            ? "Готово"
                            : overlayStopToken.IsCancellationRequested
                                ? "Отменено"
                                : (string.IsNullOrEmpty(result.Error) ? "Отменено" : "Ошибка");

                        // Success для STOP — тоже false (задача не выполнена),
                        // но overlay сам отрисует нейтральный цвет по факту закрытия.
                        overlayHandle.SetFinalStatus(summary, result.Success);
                    }
                    catch { /* overlay не критичен */ }

                    try { overlayHandle.Dispose(); }
                    catch { /* overlay не критичен */ }
                }

                _logger.LogInformation(
                    "VisionAgent[{TaskId}]: завершено (success={Success}, steps={Steps}, ms={Ms}): {Error}",
                    taskId, result.Success, history.Count, result.TotalDurationMs,
                    result.Error ?? "(нет)");
            }

            return result;
        }

        // ============================================================
        // Private — dispatch
        // ============================================================

        /// <summary>
        /// Выполняет действие в backend. Координаты резолвятся из
        /// <c>target</c> (семантический id) → <c>Center.X/Y</c>, если target задан.
        /// Иначе — из <c>x</c> / <c>y</c>.
        /// </summary>
        private static async Task ExecuteActionAsync(
            IVisionBackend backend,
            string actionType,
            VisionActionDto action,
            ScreenDescriptionDto screen,
            CancellationToken ct)
        {
            switch (actionType)
            {
                case "click":
                case "double_click":
                case "right_click":
                case "move_mouse":
                {
                    var (x, y) = ResolveCoordinates(action, screen);
                    switch (actionType)
                    {
                        case "click": await backend.ClickAsync(x, y, ct); break;
                        case "double_click": await backend.DoubleClickAsync(x, y, ct); break;
                        case "right_click": await backend.RightClickAsync(x, y, ct); break;
                        case "move_mouse": await backend.MoveMouseAsync(x, y, ct); break;
                    }
                    break;
                }

                case "type":
                    await backend.TypeAsync(action.Text ?? string.Empty, ct);
                    break;

                case "press_key":
                    await backend.PressKeyAsync(action.Key ?? string.Empty, ct);
                    break;

                case "hotkey":
                    await backend.HotkeyAsync(action.Keys ?? new List<string>(), ct);
                    break;

                case "scroll":
                    await backend.ScrollAsync(action.DeltaY ?? 0, ct);
                    break;

                case "wait":
                    await backend.WaitAsync(action.DeltaY ?? DefaultWaitMs, ct);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"VisionAgentService: неизвестный action для backend'а: '{actionType}'.");
            }
        }

        /// <summary>
        /// Резолвит координаты: если задан <c>target</c> — ищем в
        /// <c>screen.UiElements</c> и берём центр. Иначе — <c>x</c> / <c>y</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Если target не найден или не заданы ни target, ни x/y.
        /// </exception>
        private static (int x, int y) ResolveCoordinates(
            VisionActionDto action, ScreenDescriptionDto screen)
        {
            // 1. Приоритет — target (семантический id).
            if (!string.IsNullOrWhiteSpace(action.Target))
            {
                var element = screen?.UiElements?.FirstOrDefault(el =>
                    string.Equals(el.Id, action.Target, StringComparison.Ordinal));

                if (element?.Center != null)
                {
                    return (element.Center.X, element.Center.Y);
                }

                // Если target есть в screen, но без center — попробуем bounds.
                if (element?.Bounds != null)
                {
                    return (element.Bounds.X + element.Bounds.W / 2,
                            element.Bounds.Y + element.Bounds.H / 2);
                }

                throw new InvalidOperationException(
                    $"VisionAgentService: target '{action.Target}' не найден в ui_elements.");
            }

            // 2. Fallback — прямые координаты.
            if (action.X.HasValue && action.Y.HasValue)
            {
                return (action.X.Value, action.Y.Value);
            }

            throw new InvalidOperationException(
                "VisionAgentService: не задан ни target, ни x/y для действия.");
        }
    }
}