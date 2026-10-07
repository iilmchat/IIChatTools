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

            // KI-173 (v1.13.x): fallback URL из текста task.
            // qwen3-4b часто кладёт URL в `task`, а не в `url` (KI-172).
            // Без url loop работает на ТЕКУЩЕМ экране — чат IIChatTools, не Chrome.
            // Пытаемся извлечь URL из task — regex `https?://...`.
            // Если нашлось — используем как effectiveUrl (не мутируем request).
            var effectiveUrl = request.Url;
            if (string.IsNullOrWhiteSpace(effectiveUrl))
            {
                var urlMatch = System.Text.RegularExpressions.Regex.Match(
                    request.Task,
                    @"https?://[^\s\)\]\},;]+",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (urlMatch.Success)
                {
                    effectiveUrl = urlMatch.Value.TrimEnd('.', ',', ';', ')', ']');
                    _logger.LogWarning(
                        "VisionAgent[{TaskId}]: url не задан, извлечён из task: {Url}. " +
                        "Chat LLM должна передавать url отдельным аргументом (KI-172).",
                        taskId, effectiveUrl);
                }
            }

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

            // v1.13.x (KI-190): последний успешный PNG — вернём в result.Base64,
            // чтобы Chat UI мог отрендерить картинку (KI-175).
            // Hoisted из try-блока: иначе не видно после finally (CS0103).
            byte[] lastScreenshotPng = null;

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
                taskId, userId, maxSteps, maxSeconds, effectiveUrl ?? "(нет)", request.Task);

            // KI-153 (v1.12.x): предупреждаем, если run_task запущен без URL.
            // Loop работает на ТЕКУЩЕМ экране — если фокус не на whitelisted
            // процессе, все mutation-действия упадут на EnsureForegroundProcessAllowed.
            // KI-173 (v1.13.x): если URL извлечён из task — предупреждения нет.
            if (string.IsNullOrWhiteSpace(effectiveUrl))
            {
                _logger.LogWarning(
                    "VisionAgent[{TaskId}]: run_task без url — loop будет работать " +
                    "на текущем экране. Убедитесь, что фокус на whitelisted-процессе " +
                    "(Chrome / Edge / Firefox).",
                    taskId);
            }

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
                if (!string.IsNullOrWhiteSpace(effectiveUrl))
                {
                    try
                    {
                        await _backend.OpenAsync(effectiveUrl, effectiveCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "VisionAgent[{TaskId}]: OpenAsync упал на '{Url}'", taskId, effectiveUrl);
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
                        // v1.13.x (KI-190): запоминаем последний успешный PNG.
                        lastScreenshotPng = png;
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

                    // KI-192 (v1.13.x): retry на пустом ui_elements на первом кадре.
                    // Медленно грузящиеся страницы (gismeteo, РЖД) на первом
                    // describe возвращают UiElements=[] (белый экран, спиннер,
                    // переход). Planner видит «пустой экран» → wait или fail.
                    // Retry до 3 раз с паузой PageStabilityCheckMs × 4 (~2 сек),
                    // не вызывая Planner. Только на первом кадре (history.Count == 0):
                    // на последующих шагах пустой экран обрабатывает сам Planner
                    // (wait) + DetectPlannerCycle (KI-187).
                    if (history.Count == 0
                        && (screen.UiElements == null || screen.UiElements.Count == 0))
                    {
                        const int MaxRetries = 3;
                        // KI-192-fix: задержка через отдельную опцию
                        // (в unit-тестах — 1 мс, в проде — 2000 мс).
                        var retryDelayMs = Math.Max(10,
                            _options.Limits.EmptyUiElementsRetryDelayMs);

                        for (int attempt = 1; attempt <= MaxRetries; attempt++)
                        {
                            _logger.LogInformation(
                                "VisionAgent[{TaskId}]: ui_elements=[] на шаге {Step} " +
                                "(history={Hist}). Retry {Attempt}/{Max} через {Delay}ms.",
                                taskId, step, history.Count, attempt, MaxRetries, retryDelayMs);

                            await Task.Delay(retryDelayMs, effectiveCts.Token)
                                .ConfigureAwait(false);

                            // Свежий скриншот.
                            try
                            {
                                png = await _backend.ScreenshotAsync(effectiveCts.Token)
                                    .ConfigureAwait(false);
                                lastScreenshotPng = png;
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex,
                                    "VisionAgent[{TaskId}]: retry ScreenshotAsync упал",
                                    taskId);
                                break;
                            }

                            // Повторный describe.
                            try
                            {
                                screen = await _visionLlm.DescribeAsync(png, effectiveCts.Token)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex,
                                    "VisionAgent[{TaskId}]: retry DescribeAsync упал",
                                    taskId);
                                break;
                            }

                            if (screen.UiElements != null && screen.UiElements.Count > 0)
                            {
                                _logger.LogInformation(
                                    "VisionAgent[{TaskId}]: retry {Attempt} успешен — " +
                                    "ui_elements={Count}",
                                    taskId, attempt, screen.UiElements.Count);
                                break;
                            }
                        }

                        // Если после retry всё ещё пусто — fallthrough к Planner,
                        // он сам решит wait/fail.
                        if (screen.UiElements == null || screen.UiElements.Count == 0)
                        {
                            _logger.LogWarning(
                                "VisionAgent[{TaskId}]: ui_elements=[] после {Max} retry — " +
                                "передаём Planner'у как есть.",
                                taskId, MaxRetries);
                        }
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

                    // 4.4.1. KI-187 (v1.13.x): детектор цикла.
                    // Проверяем накопленную историю ПЕРЕД выполнением действия:
                    // если Planner зациклился — прерываем loop с fail.
                    // Причина: на wikipedia.org Planner делал 4×wait подряд,
                    // потом повторял click search_btn → бесконечный loop.
                    if (DetectPlannerCycle(history, out var cycleReason))
                    {
                        _logger.LogWarning(
                            "VisionAgent[{TaskId}]: детектор цикла сработал — {Reason}",
                            taskId, cycleReason);
                        result.Success = false;
                        result.Error = cycleReason;
                        break;
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
                    // KI-162-2 (v1.13.x): передаём png — нужен для VerifyTargetAsync
                    // внутри ResolveCoordinatesAsync (кроп вокруг bounds).
                    var stepSw = Stopwatch.StartNew();
                    string stepError = null;
                    try
                    {
                        // KI-162-2 (v1.13.x): передаём png — нужен для VerifyTargetAsync
                        // внутри ResolveCoordinatesAsync (кроп вокруг bounds).
                        await ExecuteActionAsync(
                                _backend, actionType, effectiveAction, screen, png,
                                effectiveCts.Token)
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

            // v1.13.x (KI-190): base64 последнего скриншота — Chat UI отрендерит.
            // Ограничение 3 MB — защита от раздутия SSE (для downscale-скриншотов
            // 1024-1280px размер PNG ~150-300 KB → ~200-400 KB base64, с запасом).
            if (lastScreenshotPng != null && lastScreenshotPng.Length > 0
                && lastScreenshotPng.Length <= 3_000_000)
            {
                try
                {
                    result.Base64 = Convert.ToBase64String(lastScreenshotPng);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VisionAgent[{TaskId}]: base64-кодирование скриншота упало", taskId);
                }
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
        private async Task ExecuteActionAsync(
            IVisionBackend backend,
            string actionType,
            VisionActionDto action,
            ScreenDescriptionDto screen,
            byte[] currentScreenshotPng,
            CancellationToken ct)
        {
            switch (actionType)
            {
                case "click":
                case "double_click":
                case "right_click":
                case "move_mouse":
                {
                    var (x, y) = await ResolveCoordinatesAsync(
                            action, screen, currentScreenshotPng, ct)
                        .ConfigureAwait(false);
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
        /// <c>screen.UiElements</c>. <b>Сначала пробуем DOM через CDP</b>
        /// (KI-161, 0 px ошибки), затем — VL-fallback:
        /// Verify (KI-162-2) → bounds-center (KI-190) → center.
        /// Иначе — прямые x/y.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Порядок резолва (v1.13.x, KI-161):</b>
        /// <list type="number">
        ///   <item><description>Если <c>Mode ∈ {dom, auto}</c> и backend
        ///     поддерживает CDP (<c>GetCoordinateProvider() != null</c>) —
        ///     <c>DomCoordinateProvider</c>. При <c>Found=true</c>
        ///     возвращаем координаты из DOM.</description></item>
        ///   <item><description>VL-fallback: Verify (KI-162-2) →
        ///     bounds-center (KI-190) → center.</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Mode:</b> <c>"dom"</c> — только DOM; <c>"vision"</c> — только VL
        /// (DOM-блок пропускается); <c>"auto"</c> — DOM если доступен, иначе VL.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Если target не найден или не заданы ни target, ни x/y.
        /// </exception>
        private async Task<(int x, int y)> ResolveCoordinatesAsync(
            VisionActionDto action,
            ScreenDescriptionDto screen,
            byte[] currentScreenshotPng,
            CancellationToken ct)
        {
            // 1. Приоритет — target (семантический id).
            if (!string.IsNullOrWhiteSpace(action.Target))
            {
                var element = screen?.UiElements?.FirstOrDefault(el =>
                    string.Equals(el.Id, action.Target, StringComparison.Ordinal));

                if (element == null)
                {
                    throw new InvalidOperationException(
                        $"VisionAgentService: target '{action.Target}' не найден в ui_elements.");
                }

                // ========== KI-161: DOM-first (PuppeteerSharp CDP-attach) ==========
                // Если backend поддерживает DOM-координаты (LocalHarness + Chrome
                // с --remote-debugging-port=9222) — получаем точные координаты
                // из getBoundingClientRect (0 px ошибки vs ±20-30 px у VL).
                //
                // Mode:
                //   "dom"    — только DOM (при неудаче — VL-fallback, не падаем);
                //   "vision" — DOM-блок полностью пропускается (старое поведение, KI-190);
                //   "auto"   — DOM если CDP подключён, иначе VL.
                var coordinateMode = _options.CoordinateProvider?.Mode?.ToLowerInvariant() ?? "auto";
                var domEnabled = coordinateMode == "dom" || coordinateMode == "auto";

                if (domEnabled)
                {
                    var domProvider = _backend.GetCoordinateProvider();
                    if (domProvider != null)
                    {
                        var (scaleX, scaleY) = _backend.GetScreenshotScale();

                        var domRequest = new CoordinateRequest
                        {
                            TargetId = action.Target,
                            TargetLabel = element.Label,
                            TargetType = element.Type,
                            TargetBounds = element.Bounds,
                            ScreenshotScaleX = scaleX > 0 ? scaleX : 1.0,
                            ScreenshotScaleY = scaleY > 0 ? scaleY : 1.0
                        };

                        try
                        {
                            var domResult = await domProvider
                                .ResolveAsync(domRequest, ct)
                                .ConfigureAwait(false);

                            // Защита от (0,0): DomCoordinateProvider возвращает
                            // X=0/Y=0 только если coordinates не удалось получить
                            // (лежит в левом верхнем углу viewport под chrome UI —
                            // нереалистично для реальной кнопки; используем как маркер
                            // «не нашёл» → VL-fallback).
                            if (domResult != null
                                && domResult.Found
                                && domResult.X > 0 && domResult.Y > 0)
                            {
                                _logger.LogInformation(
                                    "VisionAgent: DOM-resolve target='{Target}' → ({X},{Y}) " +
                                    "selector='{Selector}' matchedBy={By}",
                                    action.Target, domResult.X, domResult.Y,
                                    domResult.Selector ?? "(нет)",
                                    domResult.Error ?? "(нет)");

                                return (domResult.X, domResult.Y);
                            }

                            _logger.LogDebug(
                                "VisionAgent: DOM-resolve target='{Target}' не нашёл — " +
                                "VL-fallback ({Err})",
                                action.Target,
                                domResult?.Error ?? "not found");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "VisionAgent: DOM-resolve упал — VL-fallback");
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "VisionAgent: DOM-провайдер недоступен (Mode={Mode}) — VL-fallback",
                            coordinateMode);
                    }
                }
                // ========== /KI-161 ==========

                // KI-162-2 (v1.13.x): Coordinate-then-Verify.
                // Тот же путь, что и в VisionAgentTool.HandleCoordinateActionAsync.
                if (_options.Verify?.Enabled == true
                    && element.Bounds != null
                    && element.Bounds.W > 0 && element.Bounds.H > 0
                    && currentScreenshotPng != null && currentScreenshotPng.Length > 0)
                {
                    return await VisionVerifyHelper.TryVerifyAsync(
                            currentScreenshotPng,
                            element,
                            action.Target,
                            _visionLlm,
                            _options.Verify,
                            _logger,
                            ct)
                        .ConfigureAwait(false);
                }

                // KI-190: bounds priority над center (fallback, если Verify выключен).
                if (element.Bounds != null && element.Bounds.W > 0 && element.Bounds.H > 0)
                {
                    return (element.Bounds.X + element.Bounds.W / 2,
                            element.Bounds.Y + element.Bounds.H / 2);
                }

                if (element.Center != null)
                {
                    return (element.Center.X, element.Center.Y);
                }

                throw new InvalidOperationException(
                    $"VisionAgentService: target '{action.Target}' не содержит ни bounds, ни center.");
            }

            // 2. Fallback — прямые координаты.
            if (action.X.HasValue && action.Y.HasValue)
            {
                return (action.X.Value, action.Y.Value);
            }

            throw new InvalidOperationException(
                "VisionAgentService: не задан ни target, ни x/y для действия.");
        }
        
        // ============================================================
        // KI-187 (v1.13.x) — детектор цикла Planner LLM.
        // ============================================================

        /// <summary>Сколько последних шагов анализировать на зацикливание.</summary>
        private const int CycleDetectionWindow = 3;

        /// <summary>
        /// KI-187 (v1.13.x): проверяет, не зациклился ли Planner LLM.
        ///
        /// <para>
        /// <b>Симптом:</b> на wikipedia.org после успешного <c>click search_btn</c>
        /// Planner делал 4×<c>wait</c> подряд, потом повторял <c>click search_btn</c>
        /// → бесконечный цикл. Vision LLM видит одну и ту же страницу (Wikipedia
        /// оставляет поле поиска сверху при переходе), Planner не понимает,
        /// что задача уже выполнена.
        /// </para>
        ///
        /// <para>
        /// <b>Правила:</b>
        /// <list type="bullet">
        ///   <item>3+ подряд <c>wait</c> — fail (нечего ждать, планировщик в тупике).</item>
        ///   <item>3+ подряд одинаковый <c>(action, target)</c> — fail (цикл).</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="history">Накопленная история шагов.</param>
        /// <param name="outReason">Причина зацикливания (если возвращает true).</param>
        /// <returns>true — если обнаружен цикл; false — всё чисто.</returns>
        private static bool DetectPlannerCycle(
            IReadOnlyList<VisionStepDto> history,
            out string outReason)
        {
            outReason = null;

            if (history == null || history.Count < CycleDetectionWindow)
            {
                return false;
            }

            // 1. 3+ подряд wait — Planner в тупике.
            var lastN = history
                .Skip(history.Count - CycleDetectionWindow)
                .ToList();

            if (lastN.All(s => string.Equals(
                    s.Action, "wait", StringComparison.OrdinalIgnoreCase)))
            {
                outReason =
                    $"Planner LLM сделал {CycleDetectionWindow} шага 'wait' подряд. " +
                    "Вероятно, страница не меняется или задача требует другого действия.";
                return true;
            }

            // 2. 3+ подряд одинаковый (action, target).
            // KI-193: сравниваем НОРМАЛИЗОВАННЫЕ target'ы — иначе
            // search_btn и search_button считаются разными.
            var first = lastN[0];
            var firstTargetNorm = VisionIdNormalizer.Normalize(first.Target);
            var allSame = lastN.All(s =>
                string.Equals(s.Action, first.Action, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(VisionIdNormalizer.Normalize(s.Target) ?? string.Empty,
                    firstTargetNorm ?? string.Empty,
                    StringComparison.Ordinal));
            if (allSame)
            {
                outReason =
                    $"Planner LLM повторил действие '{first.Action}' с target " +
                    $"'{first.Target}' {CycleDetectionWindow} раз подряд. " +
                    "Возможно, задача уже выполнена или Planner не видит изменений.";
                return true;
            }

            return false;
        }
    }
}