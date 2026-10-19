using System.Linq;
using IIChatTools.Services.DTO.Browser;
using IIChatTools.Services.Implementation.Browser;
using IIChatTools.Services.Implementation.Tools.Browser;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Browser
{
    /// <summary>
    /// Unit-тесты парсинга/валидации декларативного smoke-сценария
    /// (<c>browser_session_control:run_smoke</c>, v2.0.3, KI-256).
    ///
    /// <para>
    /// Сам запуск PuppeteerSharp не тестируется: для него нужен Chromium.
    /// Покрыты чистые функции <c>BrowserSmokeRunner.TryParse</c> и
    /// метаданные инструмента.
    /// </para>
    /// </summary>
    public class BrowserSmokeTests
    {
        private const string FullScenario = """
        [
          { "action": "goto", "url": "https://localhost:5001" },
          { "action": "click", "selector": "#btn-login" },
          { "action": "wait_for_selector", "selector": "#login-form" },
          { "action": "type", "selector": "#username", "text": "admin" },
          { "action": "assert_text", "selector": "h1", "contains": "Добро пожаловать" },
          { "action": "screenshot", "path": "smoke-login.png" },
          { "action": "wait", "ms": 500 }
        ]
        """;

        // ============================================================
        // Парсинг
        // ============================================================

        /// <summary>
        /// Полный сценарий из постановки разбирается без ошибок.
        /// </summary>
        [Fact]
        public void TryParse_FullScenario_Succeeds()
        {
            var plan = Parse(FullScenario);

            Assert.Equal(7, plan.Steps.Count);
            Assert.Equal("goto", plan.Steps[0].Action);
            Assert.Equal("https://localhost:5001/", plan.Steps[0].Url);
            Assert.Equal("click", plan.Steps[1].Action);
            Assert.Equal("#btn-login", plan.Steps[1].Selector);
            Assert.Equal("wait_for_selector", plan.Steps[2].Action);
            Assert.Equal(5000, plan.Steps[2].TimeoutMs);
            Assert.Equal("type", plan.Steps[3].Action);
            Assert.Equal("admin", plan.Steps[3].Text);
            Assert.Equal("assert_text", plan.Steps[4].Action);
            Assert.Equal("Добро пожаловать", plan.Steps[4].Contains);
            Assert.Equal("screenshot", plan.Steps[5].Action);
            Assert.Equal("smoke-login.png", plan.Steps[5].Path);
            Assert.Equal("wait", plan.Steps[6].Action);
            Assert.Equal(500, plan.Steps[6].Ms);
        }

        /// <summary>
        /// Все 10 поддерживаемых actions разбираются.
        /// </summary>
        [Fact]
        public void TryParse_AllSupportedActions_Succeeds()
        {
            var plan = Parse("""
                [
                  { "action": "goto", "url": "https://example.com" },
                  { "action": "click", "selector": "#a" },
                  { "action": "click_by_text", "text": "Войти" },
                  { "action": "type", "selector": "#u", "text": "admin" },
                  { "action": "press_key", "key": "Enter" },
                  { "action": "wait_for_selector", "selector": "#f" },
                  { "action": "assert_text", "selector": "h1", "contains": "OK" },
                  { "action": "assert_selector", "selector": "#f" },
                  { "action": "screenshot", "path": "s.png" },
                  { "action": "wait", "ms": 100 }
                ]
                """);

            Assert.Equal(10, plan.Steps.Count);
            Assert.Equal("click_by_text", plan.Steps[2].Action);
            Assert.Equal("press_key", plan.Steps[4].Action);
            Assert.Equal("assert_selector", plan.Steps[7].Action);
        }

        /// <summary>
        /// Action приводится к нижнему регистру.
        /// </summary>
        [Fact]
        public void TryParse_ActionIsCaseInsensitive()
        {
            var plan = Parse("""[ { "action": "GOTO", "url": "https://example.com" } ]""");

            Assert.Equal("goto", plan.Steps[0].Action);
        }

        /// <summary>
        /// Пустой steps — ошибка.
        /// </summary>
        [Fact]
        public void TryParse_EmptySteps_Fails()
        {
            var ok = BrowserSmokeRunner.TryParse(new JArray(), out var plan, out var error);

            Assert.False(ok);
            Assert.Null(plan);
            Assert.Contains("пуст", error);
        }

        /// <summary>
        /// steps отсутствует — ошибка.
        /// </summary>
        [Fact]
        public void TryParse_MissingSteps_Fails()
        {
            var ok = BrowserSmokeRunner.TryParse(null, out _, out var error);

            Assert.False(ok);
            Assert.Contains("steps", error);
        }

        /// <summary>
        /// steps не массив — ошибка.
        /// </summary>
        [Fact]
        public void TryParse_NonArray_Fails()
        {
            var ok = BrowserSmokeRunner.TryParse(new JObject(), out _, out var error);

            Assert.False(ok);
            Assert.Contains("массив", error);
        }

        /// <summary>
        /// Шаг не объект — ошибка.
        /// </summary>
        [Fact]
        public void TryParse_StepIsNotObject_Fails()
        {
            var ok = BrowserSmokeRunner.TryParse(JArray.Parse("""[ "goto" ]"""), out _, out var error);

            Assert.False(ok);
            Assert.Contains("Шаг 1", error);
            Assert.Contains("объект", error);
        }

        /// <summary>
        /// Неизвестный action — ошибка с номером шага и списком допустимых.
        /// </summary>
        [Fact]
        public void TryParse_UnknownAction_FailsWithStepNumber()
        {
            var ok = BrowserSmokeRunner.TryParse(
                JArray.Parse("""[ { "action": "hover", "selector": "#x" } ]"""),
                out _, out var error);

            Assert.False(ok);
            Assert.Contains("Шаг 1", error);
            Assert.Contains("неизвестный action", error);
            Assert.Contains("assert_selector", error);
        }

        /// <summary>
        /// Ошибка во втором шаге содержит номер 2.
        /// </summary>
        [Fact]
        public void TryParse_UnknownActionOnSecondStep_ReportsStep2()
        {
            var ok = BrowserSmokeRunner.TryParse(JArray.Parse("""
                [
                  { "action": "goto", "url": "https://example.com" },
                  { "action": "hover" }
                ]
                """), out _, out var error, out var failedStep);

            Assert.False(ok);
            Assert.Contains("Шаг 2", error);
            Assert.Equal(2, failedStep);
        }

        /// <summary>
        /// Успешный разбор не оставляет failedStep.
        /// </summary>
        [Fact]
        public void TryParse_Success_FailedStepIsNull()
        {
            var ok = BrowserSmokeRunner.TryParse(
                JArray.Parse("""[ { "action": "wait", "ms": 1 } ]"""),
                out _, out _, out var failedStep);

            Assert.True(ok);
            Assert.Null(failedStep);
        }

        /// <summary>
        /// Обязательные параметры каждого действия проверяются.
        /// </summary>
        [Theory]
        [InlineData("""{ "action": "goto" }""")]
        [InlineData("""{ "action": "click" }""")]
        [InlineData("""{ "action": "click_by_text" }""")]
        [InlineData("""{ "action": "type", "selector": "#x" }""")]
        [InlineData("""{ "action": "press_key" }""")]
        [InlineData("""{ "action": "wait_for_selector" }""")]
        [InlineData("""{ "action": "assert_text", "selector": "h1" }""")]
        [InlineData("""{ "action": "assert_selector" }""")]
        [InlineData("""{ "action": "screenshot" }""")]
        [InlineData("""{ "action": "wait", "ms": 0 }""")]
        public void TryParse_MissingRequiredParameter_Fails(string stepJson)
        {
            var ok = BrowserSmokeRunner.TryParse(
                new JArray(JObject.Parse(stepJson)), out _, out var error);

            Assert.False(ok);
            Assert.Contains("Шаг 1", error);
        }

        /// <summary>
        /// goto допускает только абсолютные http(s)-URL.
        /// </summary>
        [Theory]
        [InlineData("""{ "action": "goto", "url": "/relative" }""")]
        [InlineData("""{ "action": "goto", "url": "ftp://example.com" }""")]
        [InlineData("""{ "action": "goto", "url": "javascript:alert(1)" }""")]
        public void TryParse_InvalidGotoUrl_Fails(string stepJson)
        {
            var ok = BrowserSmokeRunner.TryParse(
                new JArray(JObject.Parse(stepJson)), out _, out var error);

            Assert.False(ok);
            Assert.Contains("URL", error);
        }

        /// <summary>
        /// XPath/псевдо-селекторы отклоняются до запуска браузера.
        /// </summary>
        [Theory]
        [InlineData("//div[@id='x']")]
        [InlineData("text=Войти")]
        [InlineData("div:contains('Войти')")]
        public void TryParse_XPathSelector_Fails(string selector)
        {
            var step = new JObject
            {
                ["action"] = "click",
                ["selector"] = selector
            };

            var ok = BrowserSmokeRunner.TryParse(new JArray(step), out _, out var error);

            Assert.False(ok);
            Assert.Contains("CSS", error);
        }

        /// <summary>
        /// wait_for_selector: timeoutMs по умолчанию 5000.
        /// </summary>
        [Fact]
        public void TryParse_WaitForSelector_DefaultTimeoutIs5000()
        {
            var plan = Parse("""[ { "action": "wait_for_selector", "selector": "#x" } ]""");

            Assert.Equal(5000, plan.Steps[0].TimeoutMs);
        }

        /// <summary>
        /// Слишком длинный сценарий отклоняется.
        /// </summary>
        [Fact]
        public void TryParse_TooManySteps_Fails()
        {
            var steps = new JArray();
            for (var i = 0; i < BrowserSmokeRunner.MaxSteps + 1; i++)
            {
                steps.Add(new JObject { ["action"] = "wait", ["ms"] = 1 });
            }

            var ok = BrowserSmokeRunner.TryParse(steps, out _, out var error);

            Assert.False(ok);
            Assert.Contains("Слишком много шагов", error);
        }

        // ============================================================
        // Метаданные инструмента
        // ============================================================

        /// <summary>
        /// Инструмент объявляет run_smoke и параметр steps.
        /// </summary>
        [Fact]
        public void Tool_ExposesRunSmokeAndStepsParameter()
        {
            var manager = new Mock<IBrowserSessionManager>();
            var configuration = new ConfigurationBuilder().Build();
            var tool = new BrowserSessionControlTool(
                manager.Object,
                configuration,
                NullLogger<BrowserSessionControlTool>.Instance);

            Assert.Contains("run_smoke", tool.Description);
            Assert.Contains(tool.Parameters, p => p.Name == "steps" && p.Type == "array");
            Assert.Contains(tool.Parameters,
                p => p.Name == "command" && p.Description.Contains("run_smoke"));
        }

        private static BrowserSmokePlan Parse(string json)
        {
            var ok = BrowserSmokeRunner.TryParse(JArray.Parse(json), out var plan, out var error);
            Assert.True(ok, error);
            return plan;
        }
    }
}
