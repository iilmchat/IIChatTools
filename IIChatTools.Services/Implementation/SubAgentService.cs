using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.SubAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса суб-агента.
    /// Управляет циклом: запрос → tool_calls → выполнение → продолжение.
    /// </summary>
    public class SubAgentService : ISubAgentService
    {
        /// <summary>
        /// Имя инструмента-«родителя», который запрещён к вызову внутри суб-агента.
        /// </summary>
        public const string ParentToolName = "consult_secondary_agent";

        private readonly ILmStudioClient _lmStudioClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly AppDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubAgentService> _logger;

        /// <summary>
        /// Создаёт сервис суб-агента.
        /// </summary>
        /// <param name="lmStudioClient">Клиент LM Studio</param>
        /// <param name="toolRegistry">Реестр инструментов</param>
        /// <param name="dbContext">Контекст БД</param>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public SubAgentService(
            ILmStudioClient lmStudioClient,
            IToolRegistry toolRegistry,
            AppDbContext dbContext,
            IConfiguration configuration,
            ILogger<SubAgentService> logger)
        {
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<SubAgentTaskResult> ExecuteTaskAsync(
            ToolExecutionContext context,
            SubAgentTaskRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Task))
                throw new ArgumentException("Описание задачи обязательно", nameof(request));

            var sw = Stopwatch.StartNew();

            var defaultMax = GetIntConfig("SubAgent:DefaultMaxSteps", 10);
            var hardLimit = GetIntConfig("SubAgent:MaxStepsLimit", 30);
            var maxSteps = request.MaxSteps <= 0 ? defaultMax : Math.Min(request.MaxSteps, hardLimit);

            var sessionId = Guid.NewGuid().ToString("N");
            var usedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stepsLog = new List<SubAgentStep>();

            // 1. Создаём сессию в БД
            var state = new AgentState
            {
                UserId = context.UserId,
                SessionId = sessionId,
                TaskDescription = request.Task,
                CurrentStep = 0,
                IsCompleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.AgentStates.Add(state);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Старт суб-агента {SessionId} (пользователь {UserId}, задача: {Task}, лимит шагов: {Max})",
                sessionId, context.UserId, Truncate(request.Task, 200), maxSteps);

            // 2. Формируем системный промпт и историю
            var messages = new JArray
            {
                BuildSystemMessage(maxSteps),
                BuildUserMessage(request.Task, request.Context)
            };

            // 3. Формируем список tools (исключая родительский инструмент)
            //var tools = BuildToolsForSubAgent(request.AllowedTools);
            var effectiveAllowed = request.AllowedTools;
            if (effectiveAllowed == null || effectiveAllowed.Count == 0)
            {
                effectiveAllowed = _configuration
                    .GetSection("SubAgent:DefaultAllowedTools")
                    .Get<IReadOnlyList<string>>();
            }
            var tools = BuildToolsForSubAgent(effectiveAllowed);

            // 4. Основной цикл
            var finalAnswer = string.Empty;
            var completed = false;

            for (var step = 1; step <= maxSteps; step++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                state.CurrentStep = step;
                state.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                ChatCompletionResponse response;
                try
                {
                    response = await _lmStudioClient.CompleteAsync(messages, tools, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка LM Studio на шаге {Step} сессии {SessionId}", step, sessionId);
                    stepsLog.Add(new SubAgentStep
                    {
                        Step = step,
                        Type = "system",
                        Content = $"Ошибка LM Studio: {ex.Message}"
                    });
                    finalAnswer = $"Ошибка взаимодействия с LM Studio: {ex.Message}";
                    break;
                }

                // Финальный ответ — модель не вызвала инструменты
                if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    finalAnswer = response.Content ?? string.Empty;
                    stepsLog.Add(new SubAgentStep
                    {
                        Step = step,
                        Type = "assistant_text",
                        Content = finalAnswer
                    });
                    completed = true;
                    break;
                }

                // Добавляем assistant-сообщение с tool_calls в историю
                messages.Add(BuildAssistantMessage(response.Content, response.ToolCalls));

                stepsLog.Add(new SubAgentStep
                {
                    Step = step,
                    Type = "tool_call",
                    Content = response.ToolCalls.ToString(Formatting.None)
                });

                // Выполняем каждый tool_call
                foreach (var call in response.ToolCalls)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var callId = call["id"]?.ToString();
                    var functionName = call["function"]?["name"]?.ToString();
                    var argumentsRaw = call["function"]?["arguments"]?.ToString() ?? "{}";

                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;

                    if (string.Equals(functionName, ParentToolName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Защита от рекурсии
                        messages.Add(BuildToolResultMessage(callId,
                            JsonConvert.SerializeObject(new { success = false, message = "Вложенный вызов суб-агента запрещён." })));
                        continue;
                    }

                    JObject args;
                    try { args = JObject.Parse(string.IsNullOrWhiteSpace(argumentsRaw) ? "{}" : argumentsRaw); }
                    catch
                    {
                        messages.Add(BuildToolResultMessage(callId,
                            JsonConvert.SerializeObject(new { success = false, message = "Некорректный JSON аргументов." })));
                        continue;
                    }

                    usedTools.Add(functionName);

                    ToolResult toolResult;
                    try
                    {
                        toolResult = await _toolRegistry.ExecuteAsync(functionName, context, args);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка выполнения инструмента {Tool} в суб-агенте", functionName);
                        toolResult = ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
                    }

                    var toolResultJson = JsonConvert.SerializeObject(new
                    {
                        success = toolResult.Success,
                        data = toolResult.Data,
                        message = toolResult.Message
                    });

                    messages.Add(BuildToolResultMessage(callId, toolResultJson));

                    stepsLog.Add(new SubAgentStep
                    {
                        Step = step,
                        Type = "tool_result",
                        ToolName = functionName,
                        Content = Truncate(toolResultJson, 2000)
                    });
                }
            }

            if (!completed)
            {
                // Лимит шагов исчерпан — запрашиваем финальное резюме
                try
                {
                    messages.Add(new JObject
                    {
                        ["role"] = "user",
                        ["content"] = "Достигнут лимит шагов. Кратко подведи итог выполненной работы."
                    });
                    var wrap = await _lmStudioClient.CompleteAsync(messages, null, context.CancellationToken);
                    finalAnswer = wrap.Content ?? "Задача не завершена в отведённое число шагов.";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось получить финальное резюме суб-агента");
                    finalAnswer = "Задача не завершена в отведённое число шагов.";
                }
            }

            // 5. Авто-отладка (опционально)
            string debugReview = null;
            if (request.AutoDebug && completed)
            {
                try
                {
                    debugReview = await RunReviewerAsync(messages, finalAnswer, context, stepsLog);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка авто-отладки суб-агента");
                    debugReview = "Авто-отладка завершилась с ошибкой: " + ex.Message;
                }
            }

            // 6. Сохраняем результат
            state.CodeSnapshotJson = JsonConvert.SerializeObject(new
            {
                finalAnswer,
                completed,
                usedTools = usedTools.ToArray(),
                steps = stepsLog
            });
            state.IsCompleted = true;
            state.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            sw.Stop();

            _logger.LogInformation(
                "Суб-агент {SessionId} завершён (шагов: {Steps}, инструментов: {Tools}, время: {Ms} мс)",
                sessionId, state.CurrentStep, usedTools.Count, sw.ElapsedMilliseconds);

            return new SubAgentTaskResult
            {
                SessionId = sessionId,
                FinalAnswer = finalAnswer,
                Completed = completed,
                Steps = state.CurrentStep,
                DurationMs = sw.ElapsedMilliseconds,
                UsedTools = usedTools.ToList(),
                DebugReview = debugReview
            };
        }

        // ----- Внутренние методы формирования сообщений -----

        /// <summary>
        /// Формирует системное сообщение для суб-агента.
        /// </summary>
        /// <param name="maxSteps">Лимит шагов</param>
        /// <returns>JObject сообщения</returns>
        private JObject BuildSystemMessage(int maxSteps)
        {
            var prompt = _configuration["SubAgent:SystemPrompt"]
                ?? "Ты — автономный вторичный агент IIChatTools. Твоя задача — выполнить порученную работу, " +
                   "используя доступные инструменты. Действуй пошагово, проверяй результаты и заверши работу финальным ответом. " +
                   "Отвечай на русском языке. У тебя максимум {{maxSteps}} шагов.";

            prompt = prompt.Replace("{{maxSteps}}", maxSteps.ToString());

            return new JObject
            {
                ["role"] = "system",
                ["content"] = prompt
            };
        }

        /// <summary>
        /// Формирует сообщение пользователя с задачей и контекстом.
        /// </summary>
        /// <param name="task">Задача</param>
        /// <param name="extraContext">Доп. контекст</param>
        /// <returns>JObject сообщения</returns>
        private static JObject BuildUserMessage(string task, string extraContext)
        {
            var content = $"Задача: {task}";
            if (!string.IsNullOrWhiteSpace(extraContext))
                content += "\n\nКонтекст:\n" + extraContext;

            return new JObject
            {
                ["role"] = "user",
                ["content"] = content
            };
        }

        /// <summary>
        /// Формирует assistant-сообщение с tool_calls.
        /// </summary>
        /// <param name="content">Текстовое содержимое</param>
        /// <param name="toolCalls">Вызовы инструментов</param>
        /// <returns>JObject сообщения</returns>
        private static JObject BuildAssistantMessage(string content, JArray toolCalls)
        {
            return new JObject
            {
                ["role"] = "assistant",
                ["content"] = string.IsNullOrEmpty(content)
                    ? (JToken)JValue.CreateNull()
                    : new JValue(content),
                ["tool_calls"] = toolCalls
            };
        }

        /// <summary>
        /// Формирует сообщение с результатом вызова инструмента.
        /// </summary>
        /// <param name="toolCallId">Идентификатор вызова</param>
        /// <param name="content">Содержимое результата</param>
        /// <returns>JObject сообщения</returns>
        private static JObject BuildToolResultMessage(string toolCallId, string content)
        {
            return new JObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = toolCallId ?? string.Empty,
                ["content"] = content ?? string.Empty
            };
        }

        /// <summary>
        /// Формирует массив tools в формате OpenAI Function Calling,
        /// исключая родительский инструмент и не разрешённые инструменты.
        /// </summary>
        /// <param name="allowed">Опциональный белый список имён</param>
        /// <returns>JArray инструментов</returns>
        private JArray BuildToolsForSubAgent(IReadOnlyList<string> allowed)
        {
            var descriptors = _toolRegistry.GetAllDescriptors();
            var allowedSet = allowed != null && allowed.Count > 0
                ? new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase)
                : null;

            var array = new JArray();
            foreach (var d in descriptors)
            {
                if (string.Equals(d.Name, ParentToolName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (allowedSet != null && !allowedSet.Contains(d.Name))
                    continue;

                array.Add(BuildToolSchema(d));
            }
            return array;
        }

        /// <summary>
        /// Формирует JSON-схему одного инструмента.
        /// </summary>
        /// <param name="descriptor">Описание инструмента</param>
        /// <returns>JObject схемы</returns>
        private static JObject BuildToolSchema(ToolDescriptor descriptor)
        {
            var properties = new JObject();
            var required = new JArray();

            foreach (var p in descriptor.Parameters)
            {
                var propSchema = new JObject { ["type"] = NormalizeSchemaType(p.Type) };
                if (!string.IsNullOrWhiteSpace(p.Description))
                    propSchema["description"] = p.Description;
                if (p.Default != null)
                    propSchema["default"] = JToken.FromObject(p.Default);

                properties[p.Name] = propSchema;

                if (p.Required)
                    required.Add(p.Name);
            }

            return new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = descriptor.Name,
                    ["description"] = descriptor.Description,
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = required
                    }
                }
            };
        }

        /// <summary>
        /// Приводит тип параметра к каноническому виду JSON Schema.
        /// LM Studio / llama.cpp отказывается парсить схемы с нестандартными
        /// типами ("bool", "int", "double") — принимает только
        /// string / integer / number / boolean / array / object.
        /// </summary>
        private static string NormalizeSchemaType(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
                return "string";

            switch (rawType.Trim().ToLowerInvariant())
            {
                case "string":
                case "str":
                case "text":
                    return "string";

                case "int":
                case "int32":
                case "int64":
                case "long":
                case "integer":
                    return "integer";

                case "float":
                case "double":
                case "decimal":
                case "number":
                    return "number";

                case "bool":
                case "boolean":
                    return "boolean";

                case "array":
                case "list":
                    return "array";

                case "object":
                case "dict":
                    return "object";

                default:
                    // Неизвестное значение — безопасный fallback
                    return "string";
            }
        }

        /// <summary>
        /// Запускает агента-рецензента для проверки финального результата.
        /// </summary>
        /// <param name="originalMessages">История суб-агента</param>
        /// <param name="finalAnswer">Финальный ответ</param>
        /// <param name="context">Контекст выполнения</param>
        /// <param name="steps">Шаги (для дополнения лога)</param>
        /// <returns>Текст рецензии</returns>
        private async Task<string> RunReviewerAsync(
            JArray originalMessages,
            string finalAnswer,
            ToolExecutionContext context,
            List<SubAgentStep> steps)
        {
            var reviewerSystem = _configuration["SubAgent:ReviewerSystemPrompt"]
                ?? "Ты — критичный рецензент. Проверь финальный ответ другого агента на полноту и корректность. " +
                   "Если всё в порядке — напиши 'OK' и краткое подтверждение. Если есть замечания — перечисли их по пунктам. " +
                   "Отвечай на русском языке.";

            var reviewerMessages = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = reviewerSystem },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = $"Задача: {originalMessages[1]?["content"]}\n\nФинальный ответ агента:\n{finalAnswer}"
                }
            };

            var review = await _lmStudioClient.CompleteAsync(reviewerMessages, null, context.CancellationToken);
            var reviewText = review.Content ?? string.Empty;

            steps.Add(new SubAgentStep
            {
                Step = -1,
                Type = "review",
                Content = Truncate(reviewText, 2000)
            });

            return reviewText;
        }

        /// <summary>
        /// Читает int-значение из конфигурации.
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Значение</returns>
        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) && v > 0 ? v : defaultValue;
        }

        /// <summary>
        /// Обрезает строку.
        /// </summary>
        /// <param name="value">Строка</param>
        /// <param name="max">Максимум символов</param>
        /// <returns>Обрезанная строка</returns>
        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }
    }
}