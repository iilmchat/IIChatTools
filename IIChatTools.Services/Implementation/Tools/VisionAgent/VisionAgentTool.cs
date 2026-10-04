using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.VisionAgent
{
    /// <summary>
    /// Top-level инструмент Vision Agent (v1.12.0, KI-131, DESIGN § 4.1, § 7.7).
    /// Управление компьютером через визуальные подсказки (Computer Use pattern):
    /// скриншот → анализ UI Vision-моделью → действие мышью/клавиатурой.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Не наследует <c>AgentToolBase</c></b> — в отличие от 7 специализированных
    /// агентов. Сделано намеренно (DESIGN § 4.1): Vision Agent работает со
    /// скриншотами и двумя моделями (Vision LLM + Planner LLM), чего
    /// <c>SubAgentService</c> не умеет. Паттерн совпадает с
    /// <c>DatabaseAgentTool</c> (v1.7.0, KI-097) и
    /// <c>CodeAgentWithReviewTool</c> (v1.11.0, KI-126).
    /// </para>
    ///
    /// <para>
    /// <b>12 действий:</b> <c>run_task</c>, <c>describe</c>, <c>screenshot</c>,
    /// <c>click</c>, <c>double_click</c>, <c>right_click</c>, <c>move_mouse</c>,
    /// <c>type</c>, <c>press_key</c>, <c>hotkey</c>, <c>scroll</c>, <c>wait</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Per-action approval</b> (v1.7.0, KI-101): read-only действия
    /// (<c>describe</c>, <c>screenshot</c>, <c>move_mouse</c>, <c>scroll</c>,
    /// <c>wait</c>) — без approval; mutation + <c>run_task</c> — с approval.
    /// </para>
    ///
    /// <para>
    /// <b>RULES § 4.44:</b> зарегистрирован в DI как top-level <see cref="ITool"/>
    /// → обязательно добавлен в <c>allowedNames</c> в <c>ChatStreamService</c>.
    /// </para>
    /// </remarks>
    public class VisionAgentTool : ITool
    {
        // ============ Имена действий ============

        private const string ActionRunTask = "run_task";
        private const string ActionDescribe = "describe";
        private const string ActionScreenshot = "screenshot";
        private const string ActionClick = "click";
        private const string ActionDoubleClick = "double_click";
        private const string ActionRightClick = "right_click";
        private const string ActionMoveMouse = "move_mouse";
        private const string ActionType = "type";
        private const string ActionPressKey = "press_key";
        private const string ActionHotkey = "hotkey";
        private const string ActionScroll = "scroll";
        private const string ActionWait = "wait";

        /// <summary>
        /// Действия, не требующие approval (read-only или без побочных эффектов
        /// на пользовательские данные).
        /// </summary>
        private static readonly HashSet<string> ReadOnlyActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ActionDescribe,
                ActionScreenshot,
                ActionMoveMouse,
                ActionScroll,
                ActionWait
            };

        /// <summary>Пауза после <c>OpenAsync</c> для стабилизации страницы (мс).</summary>
        private const int OpenUrlSettleDelayMs = 500;

        /// <summary>Пауза по умолчанию для <c>action=wait</c> (мс).</summary>
        private const int DefaultWaitMs = 1000;

        // ============ Зависимости ============

        private readonly IVisionAgentService _visionAgentService;
        private readonly IVisionBackend _backend;
        private readonly IVisionLlmClient _visionLlm;
        private readonly IVisionActionValidator _validator;
        private readonly IVisionScreenshotStore _screenshotStore;
        private readonly VisionAgentOptions _options;
        private readonly ILogger<VisionAgentTool> _logger;

        /// <summary>
        /// Создаёт инструмент.
        /// </summary>
        /// <param name="visionAgentService">Оркестратор loop'а Vision Agent (для <c>run_task</c>).</param>
        /// <param name="backend">Backend (Local / Sandbox / RemoteVnc).</param>
        /// <param name="visionLlm">Vision LLM (описание UI со скриншота).</param>
        /// <param name="validator">Валидатор действий (blocked keys, координаты).</param>
        /// <param name="screenshotStore">Хранилище скриншотов в workspace.</param>
        /// <param name="options">Настройки Vision Agent.</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public VisionAgentTool(
            IVisionAgentService visionAgentService,
            IVisionBackend backend,
            IVisionLlmClient visionLlm,
            IVisionActionValidator validator,
            IVisionScreenshotStore screenshotStore,
            IOptions<VisionAgentOptions> options,
            ILogger<VisionAgentTool> logger)
        {
            _visionAgentService = visionAgentService
                ?? throw new ArgumentNullException(nameof(visionAgentService));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _visionLlm = visionLlm ?? throw new ArgumentNullException(nameof(visionLlm));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _screenshotStore = screenshotStore
                ?? throw new ArgumentNullException(nameof(screenshotStore));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name => "vision_agent";

        /// <inheritdoc />
        public string Description =>
            "Vision Agent — управление компьютером через визуальные подсказки " +
            "(Computer Use): скриншот → анализ UI Vision-моделью → действие " +
            "мышью/клавиатурой. Работает с любым приложением (canvas, shadow-DOM, " +
            "десктопные программы), в отличие от browser_* — только DOM.\n\n" +
            "12 действий:\n" +
            "- run_task(task, url?, maxSteps?) — автономное выполнение многошаговой " +
            "задачи. Требует approval. Пример: «Найди статью про Москву в Wikipedia».\n" +
            "- describe(url?) — описать текущий экран (текст + список ui_elements с id и координатами).\n" +
            "- screenshot(url?) — снять PNG, сохранить в workspace, вернуть " +
            "{ path, base64, sizeBytes }.\n" +
            "- click / double_click / right_click(target?|x,y) — мышь. Требует approval.\n" +
            "- move_mouse(x, y) — наведение (hover).\n" +
            "- type(text) — ввод текста в текущий фокус. Требует approval.\n" +
            "- press_key(key) — одиночная клавиша (Enter / Tab / Escape / ...). Требует approval.\n" +
            "- hotkey(keys=[\"Ctrl\",\"C\"]) — комбинация. Требует approval.\n" +
            "- scroll(deltaY) — прокрутка (+вниз, -вверх).\n" +
            "- wait(deltaY?) — пауза (мс, default 1000).\n\n" +
            "Для одиночных действий с target сначала вызови describe — получишь id " +
            "элементов. Либо укажи x/y напрямую (пиксели).\n\n" +
            "Approval: run_task + все mutation-actions (click, double_click, " +
            "right_click, type, press_key, hotkey). Read-only (describe, screenshot, " +
            "move_mouse, scroll, wait) — без approval.\n\n" +
            "Безопасность: whitelist доменов, blocked keys (F12, Ctrl+Alt+Del), " +
            "MaxSteps, MaxTaskSeconds, on-screen overlay, audit. Скриншоты не " +
            "сохраняются в ChatMessage.";

        /// <inheritdoc />
        /// <remarks>
        /// Возвращает <c>true</c> как консервативный default для вызовов
        /// <c>/api/tools/execute</c> (вне Chat). В Chat поведение уточняется
        /// через <see cref="RequiresApprovalForCall"/> (per-action approval, KI-101).
        /// </remarks>
        public bool RequiresApprovalByDefault => true;

        /// <summary>
        /// Per-action approval (v1.7.0, KI-101): read-only действия — без approval,
        /// mutation + <c>run_task</c> — с approval.
        ///
        /// <para>
        /// При <c>null</c> / пустом <c>action</c> возвращает <c>true</c>
        /// (консервативно — не доверяем «непонятному» вызову).
        /// </para>
        /// </summary>
        /// <param name="arguments">Аргументы вызова.</param>
        /// <returns>true — если действие требует approval.</returns>
        public bool RequiresApprovalForCall(JObject arguments)
        {
            var action = (arguments?.GetString("action") ?? string.Empty)
                .Trim();

            if (string.IsNullOrEmpty(action))
            {
                return true;   // консервативно: непонятный вызов → approval
            }

            return !ReadOnlyActions.Contains(action);
        }

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "action",
                Type = "string",
                Description =
                    "Действие. Одно из: run_task, describe, screenshot, click, " +
                    "double_click, right_click, move_mouse, type, press_key, " +
                    "hotkey, scroll, wait.",
                Required = true
            },
            new ToolParameterDescriptor
            {
                Name = "task",
                Type = "string",
                Description =
                    "Задача естественным языком (для run_task). " +
                    "Пример: «Купи билет РЖД Москва→Камчатка, купе, нижняя полка».",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "url",
                Type = "string",
                Description =
                    "Стартовый URL (для run_task / describe / screenshot). " +
                    "Домен должен быть в whitelist (VisionAgent:Whitelist:Domains).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "target",
                Type = "string",
                Description =
                    "ID элемента из ui_elements (приоритет над x/y). " +
                    "Для координатных действий. Получить id можно через describe.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "x",
                Type = "integer",
                Description =
                    "X-координата в пикселях (для click / double_click / right_click / " +
                    "move_mouse). Используется, если не задан target.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "y",
                Type = "integer",
                Description =
                    "Y-координата в пикселях (см. x).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "text",
                Type = "string",
                Description = "Текст для ввода (для type). До 2000 символов.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "key",
                Type = "string",
                Description =
                    "Имя клавиши (для press_key): Enter / Tab / Escape / Delete / " +
                    "Backspace / стрелки / F1-F12 / A-Z / 0-9. " +
                    "Запрещены: F12, Ctrl+Shift+I, Alt+F4 и др. (см. ActionValidation).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "keys",
                Type = "array",
                Description =
                    "Комбинация клавиш (для hotkey): [\"Ctrl\",\"C\"]. " +
                    "Запрещены: Ctrl+Alt+Delete, Alt+Tab, Win+L и др.",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "deltaY",
                Type = "integer",
                Description =
                    "Прокрутка (для scroll): + вниз, − вверх (в WHEEL_DELTA, 120 = 1 щелчок). " +
                    "Для wait — длительность паузы в мс (default 1000).",
                Required = false
            },
            new ToolParameterDescriptor
            {
                Name = "maxSteps",
                Type = "integer",
                Description =
                    "Override MaxSteps для этого run_task (1..Limits.MaxSteps). " +
                    "Если не задано — используется Limits.MaxSteps из настроек.",
                Required = false
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            JObject arguments)
        {
            if (context == null)
                return ToolResult.Fail("Контекст выполнения не задан.");

            var action = (arguments?.GetString("action") ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return ToolResult.Fail(
                    "Не указан параметр 'action'. Допустимые значения: " +
                    "run_task, describe, screenshot, click, double_click, right_click, " +
                    "move_mouse, type, press_key, hotkey, scroll, wait.");

            try
            {
                switch (action)
                {
                    case ActionRunTask:
                        return await HandleRunTaskAsync(context, arguments);
                    case ActionDescribe:
                        return await HandleDescribeAsync(context, arguments);
                    case ActionScreenshot:
                        return await HandleScreenshotAsync(context, arguments);
                    case ActionClick:
                    case ActionDoubleClick:
                    case ActionRightClick:
                    case ActionMoveMouse:
                        return await HandleCoordinateActionAsync(context, arguments, action);
                    case ActionType:
                    case ActionPressKey:
                    case ActionHotkey:
                    case ActionScroll:
                    case ActionWait:
                        return await HandleInputActionAsync(context, arguments, action);
                    default:
                        return ToolResult.Fail(
                            $"Неизвестное действие '{action}'. Допустимые: " +
                            "run_task, describe, screenshot, click, double_click, " +
                            "right_click, move_mouse, type, press_key, hotkey, scroll, wait.");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "VisionAgentTool: операция отменена (action={Action})", action);
                return ToolResult.Fail("Операция отменена.");
            }
            catch (ArgumentException ex)
            {
                _logger.LogInformation(
                    "VisionAgentTool: некорректный вызов (action={Action}): {Message}",
                    action, ex.Message);
                return ToolResult.Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex,
                    "VisionAgentTool: операция невозможна (action={Action})", action);
                return ToolResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "VisionAgentTool: неожиданная ошибка (action={Action})", action);
                return ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
            }
        }

        // ============================================================
        // Обработчики действий
        // ============================================================

        /// <summary>
        /// <c>run_task</c> — полный loop Vision Agent через <see cref="IVisionAgentService"/>.
        /// </summary>
        private async Task<ToolResult> HandleRunTaskAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var task = arguments?.GetString("task");
            if (string.IsNullOrWhiteSpace(task))
            {
                return ToolResult.Fail(
                    "Для action='run_task' обязателен параметр 'task' " +
                    "(задача естественным языком).");
            }

            var url = arguments?.GetString("url");
            var maxSteps = arguments?.GetInt("maxSteps", 0) ?? 0;

            var request = new VisionTaskRequest
            {
                Task = task.Trim(),
                Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
                MaxSteps = maxSteps > 0 ? maxSteps : (int?)null
            };

            var result = await _visionAgentService.RunTaskAsync(
                request, context.UserId, context.CancellationToken);

            var message = result.Success
                ? $"Задача выполнена за {result.Steps?.Count ?? 0} шагов ({result.TotalDurationMs} мс)."
                : (string.IsNullOrWhiteSpace(result.Error)
                    ? "Задача не выполнена."
                    : result.Error);

            // Semantически: Success=false — это не «tool упал», а «агент не справился».
            // Возвращаем Fail с Data=result, чтобы Chat LLM увидел детали,
            // но не выдал пользователю «агент справился» (урок KI-113/KI-114).
            return result.Success
                ? ToolResult.Ok(result, message)
                : ToolResult.Fail(message, result);
        }

        /// <summary>
        /// <c>describe</c> — снять скриншот, отдать в Vision LLM, вернуть описание UI.
        /// </summary>
        private async Task<ToolResult> HandleDescribeAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var url = arguments?.GetString("url");

            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    await _backend.OpenAsync(url.Trim(), context.CancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(OpenUrlSettleDelayMs, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VisionAgentTool.describe: OpenAsync('{Url}') упал", url);
                    return ToolResult.Fail($"Не удалось открыть URL '{url}': {ex.Message}");
                }
            }

            byte[] png;
            try
            {
                png = await _backend.ScreenshotAsync(context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgentTool.describe: ScreenshotAsync упал");
                return ToolResult.Fail($"Ошибка скриншота: {ex.Message}");
            }

            ScreenDescriptionDto screen;
            try
            {
                screen = await _visionLlm.DescribeAsync(png, context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgentTool.describe: DescribeAsync упал");
                return ToolResult.Fail($"Ошибка описания экрана: {ex.Message}");
            }

            var elementCount = screen?.UiElements?.Count ?? 0;
            return ToolResult.Ok(
                screen,
                $"Описание экрана получено ({elementCount} элементов UI, backend={_backend.Name}).");
        }

        /// <summary>
        /// <c>screenshot</c> — снять PNG, сохранить в workspace, вернуть path + base64.
        /// </summary>
        private async Task<ToolResult> HandleScreenshotAsync(
            ToolExecutionContext context, JObject arguments)
        {
            var url = arguments?.GetString("url");

            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    await _backend.OpenAsync(url.Trim(), context.CancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(OpenUrlSettleDelayMs, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "VisionAgentTool.screenshot: OpenAsync('{Url}') упал", url);
                    return ToolResult.Fail($"Не удалось открыть URL '{url}': {ex.Message}");
                }
            }

            byte[] png;
            try
            {
                png = await _backend.ScreenshotAsync(context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VisionAgentTool.screenshot: ScreenshotAsync упал");
                return ToolResult.Fail($"Ошибка скриншота: {ex.Message}");
            }

            // Сохранение в workspace — best-effort (ошибка не критична, base64 всё равно отдадим).
            var taskId = "vt_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string savedPath = null;
            try
            {
                savedPath = await _screenshotStore.SaveAsync(
                    context.UserId, taskId, 1, png, context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VisionAgentTool.screenshot: сохранение в workspace упало");
            }

            var data = new
            {
                path = savedPath,
                base64 = Convert.ToBase64String(png),
                sizeBytes = png.Length,
                backend = _backend.Name
            };

            var message = savedPath != null
                ? $"Скриншот получен и сохранён ({png.Length} байт)."
                : $"Скриншот получен ({png.Length} байт; сохранение в workspace отключено).";

            return ToolResult.Ok(data, message);
        }

        /// <summary>
        /// Координатные действия: <c>click</c> / <c>double_click</c> / <c>right_click</c> / <c>move_mouse</c>.
        /// Если задан <c>target</c> — резолвим его через screenshot + describe.
        /// Иначе — используем <c>x</c> / <c>y</c>.
        /// </summary>
        private async Task<ToolResult> HandleCoordinateActionAsync(
            ToolExecutionContext context, JObject arguments, string actionType)
        {
            var target = arguments?.GetString("target");
            var x = arguments?.GetInt("x", 0) ?? 0;
            var y = arguments?.GetInt("y", 0) ?? 0;
            var reason = arguments?.GetString("reason");

            var hasTarget = !string.IsNullOrWhiteSpace(target);
            var hasCoords = x != 0 || y != 0;

            if (!hasTarget && !hasCoords)
            {
                return ToolResult.Fail(
                    $"Для action='{actionType}' нужен либо 'target' " +
                    "(id из ui_elements), либо пара 'x'/'y' (пиксели).");
            }

            // Если задан target — резолвим координаты через screenshot + describe.
            ScreenDescriptionDto screen = null;
            if (hasTarget)
            {
                byte[] png;
                try
                {
                    png = await _backend.ScreenshotAsync(context.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    return ToolResult.Fail(
                        $"Не удалось снять скриншот для резолва target: {ex.Message}");
                }

                try
                {
                    screen = await _visionLlm.DescribeAsync(png, context.CancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    return ToolResult.Fail(
                        $"Не удалось описать экран для резолва target: {ex.Message}");
                }

                var element = screen?.UiElements?.FirstOrDefault(el =>
                    string.Equals(el.Id, target, StringComparison.Ordinal));

                if (element == null)
                {
                    return ToolResult.Fail(
                        $"target '{target}' не найден в ui_elements. " +
                        "Вызови describe, чтобы получить актуальный список.");
                }

                if (element.Center != null)
                {
                    x = element.Center.X;
                    y = element.Center.Y;
                }
                else if (element.Bounds != null)
                {
                    x = element.Bounds.X + element.Bounds.W / 2;
                    y = element.Bounds.Y + element.Bounds.H / 2;
                }
                else
                {
                    return ToolResult.Fail(
                        $"target '{target}' не содержит ни center, ни bounds.");
                }
            }

            // Валидация.
            var actionDto = new VisionActionDto
            {
                Action = actionType,
                Target = target,
                X = x,
                Y = y,
                Reason = reason
            };

            var validationScreen = screen
                ?? new ScreenDescriptionDto { UiElements = new List<UiElementDto>() };

            var validation = _validator.Validate(actionDto, validationScreen);
            if (!validation.Success)
            {
                return ToolResult.Fail(validation.Error ?? "Действие отклонено валидатором.");
            }

            // Выполнение.
            try
            {
                switch (actionType)
                {
                    case ActionClick:
                        await _backend.ClickAsync(x, y, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionDoubleClick:
                        await _backend.DoubleClickAsync(x, y, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionRightClick:
                        await _backend.RightClickAsync(x, y, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionMoveMouse:
                        await _backend.MoveMouseAsync(x, y, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VisionAgentTool: {Action} упал ({X},{Y})", actionType, x, y);
                return ToolResult.Fail($"Ошибка выполнения '{actionType}': {ex.Message}");
            }

            return ToolResult.Ok(
                new { action = actionType, x, y, target = hasTarget ? target : null },
                $"Действие '{actionType}' выполнено в ({x}, {y}).");
        }

        /// <summary>
        /// Клавиатурные / временные действия: <c>type</c> / <c>press_key</c> /
        /// <c>hotkey</c> / <c>scroll</c> / <c>wait</c>.
        /// </summary>
        private async Task<ToolResult> HandleInputActionAsync(
            ToolExecutionContext context, JObject arguments, string actionType)
        {
            var text = arguments?.GetString("text");
            var key = arguments?.GetString("key");
            var keys = arguments?["keys"]?.ToObject<List<string>>();
            var deltaY = arguments?.GetInt("deltaY", 0) ?? 0;
            var reason = arguments?.GetString("reason");

            // Валидация обязательных полей по action.
            switch (actionType)
            {
                case ActionType when string.IsNullOrEmpty(text):
                    return ToolResult.Fail("Для action='type' нужен параметр 'text'.");
                case ActionPressKey when string.IsNullOrEmpty(key):
                    return ToolResult.Fail("Для action='press_key' нужен параметр 'key'.");
                case ActionHotkey when keys == null || keys.Count == 0:
                    return ToolResult.Fail(
                        "Для action='hotkey' нужен непустой массив 'keys'. " +
                        "Пример: [\"Ctrl\",\"C\"].");
            }

            // Валидация (blocked keys / hotkeys / clamp text / clamp deltaY).
            var actionDto = new VisionActionDto
            {
                Action = actionType,
                Text = text,
                Key = key,
                Keys = keys,
                DeltaY = deltaY,
                Reason = reason
            };

            var emptyScreen = new ScreenDescriptionDto { UiElements = new List<UiElementDto>() };
            var validation = _validator.Validate(actionDto, emptyScreen);
            if (!validation.Success)
            {
                return ToolResult.Fail(validation.Error ?? "Действие отклонено валидатором.");
            }

            // Используем sanitized (если валидатор clamp'нул text / deltaY).
            var effective = validation.SanitizedAction ?? actionDto;

            try
            {
                switch (actionType)
                {
                    case ActionType:
                        await _backend.TypeAsync(effective.Text, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionPressKey:
                        await _backend.PressKeyAsync(effective.Key, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionHotkey:
                        await _backend.HotkeyAsync(
                                effective.Keys ?? new List<string>(),
                                context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionScroll:
                        await _backend.ScrollAsync(
                                effective.DeltaY ?? 0,
                                context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case ActionWait:
                        var ms = effective.DeltaY.GetValueOrDefault();
                        if (ms <= 0) ms = DefaultWaitMs;
                        await _backend.WaitAsync(ms, context.CancellationToken)
                            .ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VisionAgentTool: {Action} упал", actionType);
                return ToolResult.Fail($"Ошибка выполнения '{actionType}': {ex.Message}");
            }

            return ToolResult.Ok(
                new { action = actionType },
                $"Действие '{actionType}' выполнено.");
        }
    }
}