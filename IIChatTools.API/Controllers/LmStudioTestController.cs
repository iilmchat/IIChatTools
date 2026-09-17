using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using IIChatTools.API.Resources;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// Контроллер для проверки связи с LM Studio.
    /// Доступен только администраторам.
    /// </summary>
    [ApiController]
    [Route("api/lmstudio")]
    [Authorize(Policy = "AdminOnly")]
    public class LmStudioTestController : ControllerBase
    {
        private readonly ILmStudioClient _lmStudioClient;
        private readonly ILogger<LmStudioTestController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="lmStudioClient">Клиент LM Studio</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public LmStudioTestController(
            ILmStudioClient lmStudioClient,
            ILogger<LmStudioTestController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Тестовый вызов инструмента list_directory через LM Studio.
        /// Проверяет сквозной tool calling: API -> LmStudioClient -> LM Studio -> модель.
        /// </summary>
        /// <param name="request">Тело запроса с полем prompt (необязательно)</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("tool-test")]
        public async Task<IActionResult> ToolTestAsync([FromBody] LmStudioToolTestRequest request)
        {
            var prompt = string.IsNullOrWhiteSpace(request?.Prompt)
                ? "Посмотри список файлов в текущей папке. Вызови list_directory с path='.'."
                : request.Prompt;

            var sw = Stopwatch.StartNew();

            try
            {
                var messages = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Ты — ассистент IIChatTools. Для получения информации " +
                                      "о файлах обязательно используй инструмент list_directory. " +
                                      "Не выдумывай содержимое каталогов. Если пользователь " +
                                      "просит посмотреть файлы — вызови инструмент."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = prompt
                    }
                };

                var tools = new JArray
                {
                    new JObject
                    {
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = "list_directory",
                            ["description"] = "Возвращает список файлов и подкаталогов " +
                                              "в указанной директории",
                            ["parameters"] = new JObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JObject
                                {
                                    ["path"] = new JObject
                                    {
                                        ["type"] = "string",
                                        ["description"] = "Относительный путь к каталогу. " +
                                                          "По умолчанию — корень workspace."
                                    }
                                },
                                ["required"] = new JArray { "path" }
                            }
                        }
                    }
                };

                var response = await _lmStudioClient.CompleteAsync(
                    messages, tools, HttpContext.RequestAborted);

                sw.Stop();

                var hasToolCalls = response.ToolCalls != null && response.ToolCalls.Count > 0;

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        content = response.Content,
                        reasoningContent = response.ReasoningContent,
                        finishReason = response.FinishReason,
                        toolCalls = response.ToolCalls,
                        toolCallsCount = response.ToolCalls?.Count ?? 0,
                        hasToolCalls,
                        usage = response.Usage == null ? null : new
                        {
                            promptTokens = response.Usage.PromptTokens,
                            completionTokens = response.Usage.CompletionTokens,
                            totalTokens = response.Usage.TotalTokens,
                            reasoningTokens = response.Usage.ReasoningTokens
                        },
                        elapsedMs = sw.ElapsedMilliseconds
                    },
                    message = hasToolCalls
                        ? "Tool calling подтверждён."
                        : "Модель не вызвала инструмент (см. content)."
                });
            }
            catch (TimeoutException ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Таймаут при tool-test");
                return Ok(new { success = false, message = $"Таймаут: {ex.Message}" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Ошибка при tool-test");
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        /// <summary>
        /// Отправляет простой запрос в LM Studio и возвращает ответ модели.
        /// </summary>
        /// <returns>JSON { success, data, message }</returns>
        [HttpGet("ping")]
        public async Task<IActionResult> PingAsync()
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var messages = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = "Сколько будет 2+2? Ответь одним словом."
                    }
                };

                var response = await _lmStudioClient.CompleteAsync(
                    messages, null, HttpContext.RequestAborted);

                sw.Stop();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        content = response.Content,
                        reasoningContent = response.ReasoningContent,
                        finishReason = response.FinishReason,
                        toolCallsCount = response.ToolCalls?.Count ?? 0,
                        usage = response.Usage == null ? null : new
                        {
                            promptTokens = response.Usage.PromptTokens,
                            completionTokens = response.Usage.CompletionTokens,
                            totalTokens = response.Usage.TotalTokens,
                            reasoningTokens = response.Usage.ReasoningTokens
                        },
                        elapsedMs = sw.ElapsedMilliseconds
                    },
                    message = "Связь с LM Studio установлена."
                });
            }
            catch (TimeoutException ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Таймаут при обращении к LM Studio");
                return Ok(new { success = false, message = $"Таймаут: {ex.Message}" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Ошибка при проверке связи с LM Studio");
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        /// <summary>
        /// Возвращает список моделей, доступных в LM Studio.
        /// </summary>
        /// <returns>JSON { success, data, message }</returns>
        [HttpGet("models")]
        public async Task<IActionResult> GetModelsAsync()
        {
            try
            {
                var models = await _lmStudioClient.GetModelIdsAsync(HttpContext.RequestAborted);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        count = models.Count,
                        models
                    },
                    message = "Список моделей получен."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка моделей LM Studio");
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        /// <summary>
        /// Отправляет произвольный запрос в LM Studio (для ручной отладки).
        /// </summary>
        /// <param name="request">Тело запроса с полями prompt и optional maxTokens</param>
        /// <returns>JSON { success, data, message }</returns>
        [HttpPost("chat")]
        public async Task<IActionResult> ChatAsync([FromBody] LmStudioChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
                return Ok(new { success = false, message = "Параметр 'prompt' обязателен." });

            var sw = Stopwatch.StartNew();

            try
            {
                var messages = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = request.Prompt
                    }
                };

                var response = await _lmStudioClient.CompleteAsync(
                    messages, null, HttpContext.RequestAborted);

                sw.Stop();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        content = response.Content,
                        reasoningContent = response.ReasoningContent,
                        finishReason = response.FinishReason,
                        usage = response.Usage == null ? null : new
                        {
                            promptTokens = response.Usage.PromptTokens,
                            completionTokens = response.Usage.CompletionTokens,
                            totalTokens = response.Usage.TotalTokens,
                            reasoningTokens = response.Usage.ReasoningTokens
                        },
                        elapsedMs = sw.ElapsedMilliseconds
                    }
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Ошибка при вызове LM Studio");
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }
    }

    /// <summary>
    /// Тело запроса на тестовый вызов LM Studio.
    /// </summary>
    public class LmStudioChatRequest
    {
        /// <summary>
        /// Текст пользовательского сообщения.
        /// </summary>
        public string Prompt { get; set; }
    }
    
    /// <summary>
    /// Тело запроса на тестовый вызов инструмента.
    /// </summary>
    public class LmStudioToolTestRequest
    {
        /// <summary>
        /// Текст пользовательского сообщения (необязательно,
        /// если пусто — используется дефолтный prompt для list_directory).
        /// </summary>
        public string Prompt { get; set; }
    }    
}