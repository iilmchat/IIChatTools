using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Browser;
using IIChatTools.Services.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace IIChatTools.Services.Implementation.Browser
{
    /// <summary>
    /// Выполняет декларативный smoke-сценарий в существующей браузерной сессии
    /// (команда <c>browser_session_control:run_smoke</c>, v2.0.3, KI-256).
    ///
    /// <para>
    /// Класс отделён от <see cref="BrowserSessionManager"/>, чтобы парсинг/валидацию
    /// шагов можно было покрыть unit-тестами без запуска Chromium.
    /// </para>
    /// </summary>
    public sealed class BrowserSmokeRunner
    {
        /// <summary>Максимальное количество шагов в одном сценарии.</summary>
        public const int MaxSteps = 50;

        /// <summary>Общий таймаут smoke-сценария, секунд.</summary>
        public const int OverallTimeoutSeconds = 60;

        /// <summary>Таймаут wait_for_selector по умолчанию, мс.</summary>
        public const int DefaultWaitForSelectorMs = 5000;

        private readonly ILogger _logger;

        /// <summary>
        /// Создаёт runner.
        /// </summary>
        /// <param name="logger">Логгер (может быть <c>ILogger&lt;BrowserSessionManager&gt;</c>).</param>
        public BrowserSmokeRunner(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Разбирает и валидирует массив шагов из аргумента <c>steps</c>.
        /// </summary>
        /// <param name="stepsToken">JSON-массив шагов.</param>
        /// <param name="plan">Разобранный план при успехе.</param>
        /// <param name="error">Текст ошибки при неудаче.</param>
        /// <returns><c>true</c>, если сценарий корректен.</returns>
        public static bool TryParse(JToken stepsToken, out BrowserSmokePlan plan, out string error)
            => TryParse(stepsToken, out plan, out error, out _);

        /// <summary>
        /// Разбирает и валидирует массив шагов, возвращая номер проблемного шага.
        /// </summary>
        /// <param name="stepsToken">JSON-массив шагов.</param>
        /// <param name="plan">Разобранный план при успехе.</param>
        /// <param name="error">Текст ошибки при неудаче.</param>
        /// <param name="failedStep">Номер шага (1-based) при ошибке в шаге; иначе <c>null</c>.</param>
        /// <returns><c>true</c>, если сценарий корректен.</returns>
        public static bool TryParse(
            JToken stepsToken, out BrowserSmokePlan plan, out string error, out int? failedStep)
        {
            plan = null;
            error = null;
            failedStep = null;

            if (stepsToken == null || stepsToken.Type == JTokenType.Null)
            {
                error = "Не указан steps — массив шагов smoke-сценария обязателен.";
                return false;
            }

            if (stepsToken is not JArray array)
            {
                error = "steps должен быть JSON-массивом объектов.";
                return false;
            }

            if (array.Count == 0)
            {
                error = "steps пуст — нечего выполнять.";
                return false;
            }

            if (array.Count > MaxSteps)
            {
                error = $"Слишком много шагов: {array.Count}. Максимум — {MaxSteps}.";
                return false;
            }

            var steps = new List<BrowserSmokeStep>(array.Count);

            for (var i = 0; i < array.Count; i++)
            {
                var number = i + 1;
                failedStep = number;

                if (array[i] is not JObject obj)
                {
                    error = $"Шаг {number}: ожидается объект.";
                    return false;
                }

                var action = obj.GetString("action")?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(action))
                {
                    error = $"Шаг {number}: не указан action.";
                    return false;
                }

                var step = new BrowserSmokeStep
                {
                    Number = number,
                    Action = action
                };

                switch (action)
                {
                    case "goto":
                    {
                        var url = obj.GetString("url")?.Trim();
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            error = $"Шаг {number} (goto): не указан url.";
                            return false;
                        }

                        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        {
                            error = $"Шаг {number} (goto): URL должен быть абсолютным http(s): '{url}'.";
                            return false;
                        }

                        step.Url = uri.ToString();
                        break;
                    }

                    case "click":
                    {
                        if (!TryGetSelector(obj, number, action, out var selector, out error)) return false;
                        step.Selector = selector;
                        break;
                    }

                    case "click_by_text":
                    {
                        var text = obj.GetString("text");
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            error = $"Шаг {number} (click_by_text): не указан text.";
                            return false;
                        }

                        step.Text = text;
                        break;
                    }

                    case "type":
                    {
                        if (!TryGetSelector(obj, number, action, out var selector, out error)) return false;
                        if (obj["text"] == null || obj["text"].Type == JTokenType.Null)
                        {
                            error = $"Шаг {number} (type): не указан text.";
                            return false;
                        }

                        step.Selector = selector;
                        step.Text = obj.GetString("text", string.Empty);
                        break;
                    }

                    case "press_key":
                    {
                        var key = obj.GetString("key")?.Trim();
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            error = $"Шаг {number} (press_key): не указан key.";
                            return false;
                        }

                        step.Key = key;
                        break;
                    }

                    case "wait_for_selector":
                    {
                        if (!TryGetSelector(obj, number, action, out var selector, out error)) return false;
                        step.Selector = selector;
                        step.TimeoutMs = obj.GetInt("timeoutMs", DefaultWaitForSelectorMs);
                        if (step.TimeoutMs <= 0 || step.TimeoutMs > 60000)
                        {
                            error = $"Шаг {number} (wait_for_selector): timeoutMs должен быть 1..60000.";
                            return false;
                        }

                        break;
                    }

                    case "assert_text":
                    {
                        if (!TryGetSelector(obj, number, action, out var selector, out error)) return false;
                        var contains = obj.GetString("contains");
                        if (string.IsNullOrEmpty(contains))
                        {
                            error = $"Шаг {number} (assert_text): не указан contains.";
                            return false;
                        }

                        step.Selector = selector;
                        step.Contains = contains;
                        break;
                    }

                    case "assert_selector":
                    {
                        if (!TryGetSelector(obj, number, action, out var selector, out error)) return false;
                        step.Selector = selector;
                        break;
                    }

                    case "screenshot":
                    {
                        var path = obj.GetString("path")?.Trim();
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            error = $"Шаг {number} (screenshot): не указан path.";
                            return false;
                        }

                        step.Path = path;
                        break;
                    }

                    case "wait":
                    {
                        step.Ms = obj.GetInt("ms", 500);
                        if (step.Ms <= 0 || step.Ms > 60000)
                        {
                            error = $"Шаг {number} (wait): ms должен быть 1..60000.";
                            return false;
                        }

                        break;
                    }

                    default:
                        error = $"Шаг {number}: неизвестный action '{action}'. " +
                                "Допустимые: goto, click, click_by_text, type, press_key, " +
                                "wait_for_selector, assert_text, assert_selector, screenshot, wait.";
                        return false;
                }

                steps.Add(step);
            }

            plan = new BrowserSmokePlan(steps);
            failedStep = null;
            return true;
        }

        /// <summary>
        /// Выполняет smoke-сценарий на открытой странице.
        /// Исключения наружу не пробрасываются — результат возвращается в <see cref="BrowserSmokeResult"/>.
        /// </summary>
        /// <param name="page">Открытая страница PuppeteerSharp.</param>
        /// <param name="plan">Провалидированный план.</param>
        /// <param name="context">Контекст (нужен WorkspaceRoot и CancellationToken).</param>
        /// <param name="defaultTimeoutMs">Таймаут шага по умолчанию (из аргумента timeoutMs инструмента).</param>
        /// <param name="cancellationToken">Токен с общим таймаутом смоука (60 с).</param>
        /// <returns>Результат выполнения.</returns>
        public async Task<BrowserSmokeResult> RunAsync(
            Page page,
            BrowserSmokePlan plan,
            ToolExecutionContext context,
            int defaultTimeoutMs,
            CancellationToken cancellationToken)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (defaultTimeoutMs <= 0) defaultTimeoutMs = 15000;

            var started = Stopwatch.StartNew();
            var screenshots = new List<string>();
            var originalTimeout = page.DefaultTimeout;
            var currentStep = 0;

            try
            {
                for (var i = 0; i < plan.Steps.Count; i++)
                {
                    currentStep = i + 1;

                    // KI-258: кооперативная отмена — не начинаем следующий шаг,
                    // если пользователь нажал «Отмена» (cancel_job / Stop).
                    // Log — в catch ниже (там же обрабатывается отмена,
                    // пришедшая посреди шага через WaitAsync).
                    cancellationToken.ThrowIfCancellationRequested();

                    var step = plan.Steps[i];
                    var remainingMs = (OverallTimeoutSeconds * 1000) - (int)started.ElapsedMilliseconds;
                    if (remainingMs <= 0)
                        throw new TimeoutException(
                            $"Превышен общий таймаут smoke-сценария ({OverallTimeoutSeconds} с).");

                    var requested = step.TimeoutMs > 0 ? step.TimeoutMs : defaultTimeoutMs;
                    var effectiveTimeout = Math.Max(1, Math.Min(requested, remainingMs));
                    page.DefaultTimeout = effectiveTimeout;

                    _logger.LogInformation(
                        "Browser smoke: шаг {Step}/{Total} — {Action}{Details}",
                        currentStep, plan.Steps.Count, step.Action, Describe(step));

                    await ExecuteStepAsync(page, step, context, effectiveTimeout, screenshots, cancellationToken);
                }

                return new BrowserSmokeResult
                {
                    Passed = true,
                    FailedStep = null,
                    DurationMs = started.ElapsedMilliseconds,
                    Screenshots = screenshots,
                    Error = null,
                    TotalSteps = plan.Steps.Count
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // KI-258: различаем внешнюю отмену (пользователь / cancel_job)
                // и внутренний общий таймаут сценария. Оба приходят через один
                // linked-токен, но внешний виден по context.CancellationToken.
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Задача отменена пользователем. Шаг {Step}.",
                        currentStep);

                    return Failure(
                        currentStep, started.ElapsedMilliseconds, screenshots, plan.Steps.Count,
                        $"Операция отменена. Шаг {currentStep}.");
                }

                return Failure(
                    currentStep, started.ElapsedMilliseconds, screenshots, plan.Steps.Count,
                    $"Превышен общий таймаут smoke-сценария ({OverallTimeoutSeconds} с) на шаге {currentStep}.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Browser smoke: шаг {Step} завершился ошибкой", currentStep);
                var action = currentStep >= 1 && currentStep <= plan.Steps.Count
                    ? plan.Steps[currentStep - 1].Action
                    : "setup";
                return Failure(
                    currentStep, started.ElapsedMilliseconds, screenshots, plan.Steps.Count,
                    $"Шаг {currentStep} ({action}): {ex.Message}");
            }
            finally
            {
                try { page.DefaultTimeout = originalTimeout; } catch { /* страница могла закрыться */ }
            }
        }

        private static BrowserSmokeResult Failure(
            int failedStep, long durationMs, IReadOnlyList<string> screenshots, int totalSteps, string error)
        {
            return new BrowserSmokeResult
            {
                Passed = false,
                FailedStep = failedStep,
                DurationMs = durationMs,
                Screenshots = screenshots,
                Error = error,
                TotalSteps = totalSteps
            };
        }

        private static string Describe(BrowserSmokeStep step)
        {
            return step.Action switch
            {
                "goto" => $" url={step.Url}",
                "click" or "wait_for_selector" or "assert_selector" => $" selector={step.Selector}",
                "click_by_text" => $" text={step.Text}",
                "type" => $" selector={step.Selector}",
                "press_key" => $" key={step.Key}",
                "assert_text" => $" selector={step.Selector} contains={step.Contains}",
                "screenshot" => $" path={step.Path}",
                "wait" => $" ms={step.Ms}",
                _ => string.Empty
            };
        }

        private async Task ExecuteStepAsync(
            Page page,
            BrowserSmokeStep step,
            ToolExecutionContext context,
            int effectiveTimeoutMs,
            List<string> screenshots,
            CancellationToken cancellationToken)
        {
            // KI-258: не начинаем шаг, если отмена пришла между шагами.
            cancellationToken.ThrowIfCancellationRequested();

            switch (step.Action)
            {
                case "goto":
                    // KI-258: у PuppeteerSharp 7.1.0 нет overload'ов с CancellationToken,
                    // поэтому оборачиваем ожидание в Task.WaitAsync(ct). Сам запрос
                    // прерывается page.DefaultTimeout'ом (effectiveTimeoutMs), а мы
                    // перестаём ждать и не запускаем следующий шаг.
                    await page.GoToAsync(step.Url).WaitAsync(cancellationToken);
                    break;

                case "click":
                    await page.ClickAsync(step.Selector).WaitAsync(cancellationToken);
                    break;

                case "click_by_text":
                {
                    // KI-220: точное совпадение → частичное → fallback по листовым узлам.
                    const string clickScript = @"(target) => {
                        const targetLower = (target || '').trim().toLowerCase();
                        const candidates = document.querySelectorAll('a, button, [role=button], [role=menuitem], [role=link], [role=tab], input[type=submit]');
                        for (const el of candidates) {
                            const t = (el.textContent || '').trim().toLowerCase();
                            if (t === targetLower) { el.click(); return { ok: true, matched: 'exact', tag: el.tagName }; }
                        }
                        for (const el of candidates) {
                            const t = (el.textContent || '').trim().toLowerCase();
                            if (t.includes(targetLower) && t.length < targetLower.length * 4) {
                                el.click(); return { ok: true, matched: 'contains', tag: el.tagName };
                            }
                        }
                        const all = document.querySelectorAll('*');
                        for (const el of all) {
                            if (el.children.length > 0) continue;
                            const t = (el.textContent || '').trim().toLowerCase();
                            if (t === targetLower) { el.click(); return { ok: true, matched: 'fallback', tag: el.tagName }; }
                        }
                        return { ok: false, error: 'Элемент с текстом «' + target + '» не найден' };
                    }";

                    var clickResult = await page
                        .EvaluateFunctionAsync<JObject>(clickScript, step.Text)
                        .WaitAsync(cancellationToken);
                    var ok = clickResult?["ok"]?.Value<bool>() ?? false;
                    if (!ok)
                    {
                        var clickError = clickResult?["error"]?.ToString() ?? "элемент не найден";
                        throw new InvalidOperationException(clickError);
                    }

                    break;
                }

                case "type":
                    await page.TypeAsync(step.Selector, step.Text).WaitAsync(cancellationToken);
                    break;

                case "press_key":
                    await page.Keyboard.PressAsync(step.Key).WaitAsync(cancellationToken);
                    break;

                case "wait_for_selector":
                    await page.WaitForSelectorAsync(step.Selector, new WaitForSelectorOptions
                    {
                        Timeout = effectiveTimeoutMs
                    }).WaitAsync(cancellationToken);
                    break;

                case "assert_text":
                {
                    const string textScript = @"(sel) => {
                        const el = document.querySelector(sel);
                        if (!el) return null;
                        return (el.innerText || el.textContent || '');
                    }";

                    var actual = await page
                        .EvaluateFunctionAsync<string>(textScript, step.Selector)
                        .WaitAsync(cancellationToken);
                    if (actual == null)
                        throw new InvalidOperationException($"Элемент '{step.Selector}' не найден.");

                    if (!actual.Contains(step.Contains, StringComparison.Ordinal))
                    {
                        var preview = actual.Length > 200 ? actual.Substring(0, 200) + "…" : actual;
                        throw new InvalidOperationException(
                            $"Текст элемента '{step.Selector}' не содержит '{step.Contains}'. Фактически: \"{preview}\".");
                    }

                    break;
                }

                case "assert_selector":
                {
                    const string visibleScript = @"(sel) => {
                        const el = document.querySelector(sel);
                        if (!el) return false;
                        const r = el.getBoundingClientRect();
                        const s = window.getComputedStyle(el);
                        return r.width > 0 && r.height > 0
                            && s.display !== 'none'
                            && s.visibility !== 'hidden'
                            && s.opacity !== '0';
                    }";

                    var visible = await page
                        .EvaluateFunctionAsync<bool>(visibleScript, step.Selector)
                        .WaitAsync(cancellationToken);
                    if (!visible)
                        throw new InvalidOperationException(
                            $"Элемент '{step.Selector}' не найден или не виден.");

                    break;
                }

                case "screenshot":
                {
                    if (string.IsNullOrWhiteSpace(context.WorkspaceRoot))
                        throw new InvalidOperationException(
                            "context.WorkspaceRoot пуст — некуда сохранять скриншот.");

                    if (!PathHelper.TryGetSafeFullPath(step.Path, context.WorkspaceRoot, out var safePath))
                        throw new InvalidOperationException(
                            $"Путь '{step.Path}' выходит за пределы workspace.");

                    var directory = Path.GetDirectoryName(safePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var bytes = await page.ScreenshotDataAsync(new ScreenshotOptions
                    {
                        Type = ScreenshotType.Png,
                        FullPage = false
                    }).WaitAsync(cancellationToken);

                    await File.WriteAllBytesAsync(safePath, bytes, cancellationToken);
                    screenshots.Add(step.Path);
                    _logger.LogInformation(
                        "Browser smoke: скриншот {Path} ({Bytes} байт)", step.Path, bytes.Length);
                    break;
                }

                case "wait":
                    await Task.Delay(step.Ms, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Неизвестный action: {step.Action}");
            }
        }

        private static bool TryGetSelector(
            JObject obj, int number, string action, out string selector, out string error)
        {
            selector = obj.GetString("selector")?.Trim();
            error = null;

            if (string.IsNullOrWhiteSpace(selector))
            {
                error = $"Шаг {number} ({action}): не указан selector.";
                return false;
            }

            if (selector.StartsWith("/", StringComparison.Ordinal) ||
                selector.Contains("text()", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains("text=", StringComparison.OrdinalIgnoreCase) ||
                selector.Contains(":contains(", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Шаг {number} ({action}): '{selector}' не является CSS-селектором " +
                        "(XPath/псевдо-функции не поддерживаются).";
                return false;
            }

            return true;
        }
    }
}
