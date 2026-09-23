using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Implementation.ChatTools;
using IIChatTools.Services.Implementation.Tools;      // ← ДОБАВИТЬ
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса стриминга чата.
    /// Ограничение C# CS1631: <c>yield return</c> запрещён в блоке <c>catch</c> —
    /// ошибки сохраняются в локальные переменные и «отдаются» после блока.
    /// </summary>
    public class ChatStreamService : IChatStreamService
    {
        private const int MaxHistoryMessages = 50;
        private const int MaxToolIterations = 5;
        private const string RoleUser = "user";
        private const string RoleAssistant = "assistant";
        private const string RoleSystem = "system";
        private const string RoleTool = "tool";
        private const string ParentToolName = "consult_secondary_agent";

        private readonly IChatService _chatService;
        private readonly ILmStudioClient _lmStudioClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly IChatApprovalCoordinator _approvalCoordinator;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatStreamService> _logger;

        /// <summary>
        /// Создаёт сервис стриминга.
        /// </summary>
        /// <param name="chatService">Сервис CRUD чатов</param>
        /// <param name="lmStudioClient">Клиент LM Studio (SSE)</param>
        /// <param name="toolRegistry">Реестр инструментов (для tool calling в чате)</param>
        /// <param name="workspaceResolver">Резолвер рабочего пространства пользователя</param>
        /// <param name="approvalCoordinator">Координатор подтверждений tool call (Singleton)</param>
        /// <param name="configuration">Конфигурация приложения (для SubAgent:DefaultAllowedTools)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatStreamService(
            IChatService chatService,
            ILmStudioClient lmStudioClient,
            IToolRegistry toolRegistry,
            IWorkspaceResolver workspaceResolver,
            IChatApprovalCoordinator approvalCoordinator,
            IConfiguration configuration,
            ILogger<ChatStreamService> logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatStreamRequest request,
            int userId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 1. Валидация запроса
            if (request == null)
            {
                yield return ChatStreamEvent.Error("Пустой запрос");
                yield break;
            }

            // Message обязателен только в обычном flow (не Regenerate).
            if (!request.Regenerate && string.IsNullOrWhiteSpace(request.Message))
            {
                yield return ChatStreamEvent.Error("Пустое сообщение");
                yield break;
            }

            // 2. Проверка владения чатом
            var chat = await _chatService.GetChatAsync(request.ChatId, userId, cancellationToken);
            if (chat == null)
            {
                yield return ChatStreamEvent.Error($"Чат {request.ChatId} не найден");
                yield break;
            }

            // 3. Подготовка:
            //    - Regenerate (Фаза 2.1.2): удаляем последний assistant-exchange,
            //      новое user-сообщение НЕ сохраняем.
            //    - Обычный flow: сохраняем новое user-сообщение.
            int startUserMessageId;

            if (request.Regenerate)
            {
                int deleted = 0;
                string regenError = null;
                try
                {
                    deleted = await _chatService.DeleteLastAssistantExchangeAsync(
                        request.ChatId, userId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка Regenerate для чата {ChatId}", request.ChatId);
                    regenError = "Не удалось удалить предыдущий ответ";
                }

                if (regenError != null)
                {
                    yield return ChatStreamEvent.Error(regenError);
                    yield break;
                }

                if (deleted == 0)
                {
                    yield return ChatStreamEvent.Error("Нечего регенерировать");
                    yield break;
                }

                // Находим последний user-message (для Start).
                // После DeleteLastAssistantExchangeAsync последним в чате является user.
                var history = await _chatService.GetMessagesAsync(
                    request.ChatId, userId, MaxHistoryMessages, cancellationToken);

                var lastUser = history.LastOrDefault(m => m.Role == RoleUser);
                if (lastUser == null)
                {
                    yield return ChatStreamEvent.Error("Не найдено user-сообщение");
                    yield break;
                }

                startUserMessageId = lastUser.Id;

                _logger.LogInformation(
                    "Regenerate: чат {ChatId}, удалено {Deleted}, startUserId={UserId}",
                    request.ChatId, deleted, startUserMessageId);
            }
            else
            {
                ChatMessage userMsg = null;
                string userMsgError = null;
                try
                {
                    userMsg = await _chatService.AddMessageAsync(
                        request.ChatId,
                        userId,
                        new ChatMessage { Role = RoleUser, Content = request.Message },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось сохранить user message (chatId={ChatId})", request.ChatId);
                    userMsgError = "Не удалось сохранить сообщение";
                }

                if (userMsgError != null)
                {
                    yield return ChatStreamEvent.Error(userMsgError);
                    yield break;
                }

                startUserMessageId = userMsg.Id;
            }

            // 4. Сигнализируем начало стрима
            yield return ChatStreamEvent.Start(startUserMessageId, request.ChatId);

            // 5. Формируем историю для LM Studio
            JArray messages = null;
            string historyError = null;
            try
            {
                messages = await BuildMessagesAsync(chat, userId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка формирования истории чата {ChatId}", request.ChatId);
                historyError = "Не удалось загрузить историю чата";
            }

            if (historyError != null)
            {
                yield return ChatStreamEvent.Error(historyError);
                yield break;
            }

            // 6. Формируем tools (10 инструментов из конфига SubAgent:DefaultAllowedTools)
            JArray tools = null;
            if (request.UseTools)
            {
                var allowed = _configuration
                    .GetSection("SubAgent:DefaultAllowedTools")
                    .Get<IReadOnlyList<string>>();

                tools = ToolDefinitionsBuilder.Build(
                    _toolRegistry.GetAllDescriptors(),
                    allowedNames: allowed,
                    excludeNames: new[] { ParentToolName });
            }

            // 7. Multi-turn loop (до MaxToolIterations итераций)
            var allAssistantMessageIds = new List<int>();
            var finalAssistantContent = new StringBuilder();
            int? tokensIn = null;
            int? tokensOut = null;
            Exception streamError = null;

            for (var iteration = 1; iteration <= MaxToolIterations && !cancellationToken.IsCancellationRequested; iteration++)
            {
                var accumulator = new ToolCallsAccumulator();
                var textBuffer = new StringBuilder();
                var iterator = _lmStudioClient.ChatStreamAsync(messages, tools, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                try
                {
                    while (true)
                    {
                        ChatCompletionChunk chunk;
                        try
                        {
                            if (!await iterator.MoveNextAsync()) break;
                            chunk = iterator.Current;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            streamError = ex;
                            break;
                        }

                        if (chunk == null) continue;

                        if (!string.IsNullOrEmpty(chunk.DeltaContent))
                        {
                            textBuffer.Append(chunk.DeltaContent);
                            finalAssistantContent.Append(chunk.DeltaContent);
                            yield return ChatStreamEvent.Delta(chunk.DeltaContent);
                        }

                        if (chunk.DeltaToolCall != null)
                        {
                            accumulator.Add(chunk.DeltaToolCall);
                        }

                        if (chunk.Usage != null)
                        {
                            tokensIn = chunk.Usage.PromptTokens;
                            tokensOut = chunk.Usage.CompletionTokens;
                        }

                        if (chunk.IsDone) break;
                    }
                }
                finally
                {
                    await iterator.DisposeAsync();
                }

                if (streamError != null) break;

                var partialContent = textBuffer.ToString();

                // LLM не вызвала tools — финальный ответ
                if (!accumulator.HasToolCalls)
                {
                    ChatMessage finalMsg = null;
                    string saveErr = null;
                    try
                    {
                        finalMsg = await _chatService.AddMessageAsync(
                            request.ChatId, userId,
                            new ChatMessage
                            {
                                Role = RoleAssistant,
                                Content = partialContent,
                                TokensIn = tokensIn,
                                TokensOut = tokensOut
                            },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка сохранения assistant message");
                        saveErr = "Ответ получен, но не сохранён";
                    }

                    if (saveErr != null)
                    {
                        yield return ChatStreamEvent.Error(saveErr);
                        yield break;
                    }

                    yield return ChatStreamEvent.Done(finalMsg.Id, tokensIn, tokensOut);
                    yield break;
                }

                // LLM вызвала tools — сохраняем assistant с tool_calls
                var completedCalls = accumulator.BuildCompletedCalls();
                var toolCallsJson = new JArray(completedCalls).ToString(Formatting.None);

                ChatMessage assistantWithTools = null;
                try
                {
                    assistantWithTools = await _chatService.AddMessageAsync(
                        request.ChatId, userId,
                        new ChatMessage
                        {
                            Role = RoleAssistant,
                            Content = partialContent,
                            ToolCallsJson = toolCallsJson
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка сохранения assistant message с tool_calls");
                }

                // Добавляем в messages для следующей итерации
                messages.Add(new JObject
                {
                    ["role"] = RoleAssistant,
                    ["content"] = string.IsNullOrEmpty(partialContent)
                        ? (JToken)JValue.CreateNull()
                        : new JValue(partialContent),
                    ["tool_calls"] = new JArray(completedCalls)
                });

                // Выполняем каждый tool_call
                foreach (var call in completedCalls)
                {
                    var callId = call["id"]?.ToString();
                    var functionName = call["function"]?["name"]?.ToString();
                    var argumentsRaw = call["function"]?["arguments"]?.ToString() ?? "{}";

                    if (string.IsNullOrWhiteSpace(functionName)) continue;

                    // Парсим аргументы
                    JObject args;
                    try
                    {
                        args = JObject.Parse(string.IsNullOrWhiteSpace(argumentsRaw) ? "{}" : argumentsRaw);
                    }
                    catch
                    {
                        args = new JObject();
                    }

                    // Определяем requiresApproval
                    var descriptor = _toolRegistry.GetDescriptor(functionName);
                    var requiresApproval = descriptor?.RequiresApprovalByDefault ?? true;

                    // SSE-событие: tool_call (requiresApproval → UI покажет модалку)
                    yield return ChatStreamEvent.ToolCall(new ChatToolCallDto
                    {
                        Id = callId,
                        Name = functionName,
                        Arguments = args,
                        RequiresApproval = requiresApproval
                    });

                    ToolResult toolResult;

                    if (requiresApproval)
                    {
                        // Фаза 1.7 — ожидание решения пользователя (5 минут)
                        var expiresAt = DateTime.UtcNow.AddMinutes(5);

                        yield return ChatStreamEvent.ToolApprovalRequired(new ChatApprovalRequiredDto
                        {
                            Id = callId,
                            Name = functionName,
                            Arguments = args,
                            ExpiresAt = expiresAt
                        });

                        _logger.LogInformation(
                            "Ожидание approval: callId={CallId}, tool={Tool}, chatId={ChatId}",
                            callId, functionName, request.ChatId);

                        var decision = await _approvalCoordinator.WaitForDecisionAsync(
                            callId,
                            TimeSpan.FromMinutes(5),
                            cancellationToken);

                        yield return ChatStreamEvent.ToolApprovalResolved(new ChatApprovalResolvedDto
                        {
                            Id = callId,
                            Decision = decision.ToString().ToLowerInvariant()
                        });

                        if (decision == ChatApprovalDecision.Approved)
                        {
                            // Пользователь подтвердил — выполняем
                            var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
                            var execContext = new ToolExecutionContext
                            {
                                UserId = userId,
                                WorkspaceRoot = workspaceRoot,
                                ClientIp = null,
                                CancellationToken = cancellationToken
                            };

                            try
                            {
                                toolResult = await _toolRegistry.ExecuteAsync(functionName, execContext, args);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Ошибка выполнения инструмента {Tool} после approval", functionName);
                                toolResult = ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
                            }
                        }
                        else if (decision == ChatApprovalDecision.Expired)
                        {
                            toolResult = ToolResult.Fail(
                                "Время подтверждения истекло (5 минут). Действие не выполнено.");
                        }
                        else
                        {
                            toolResult = ToolResult.Fail("Пользователь отклонил вызов инструмента.");
                        }
                    }
                    else
                    {
                        // Без approval — выполняем сразу
                        var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(userId);
                        var execContext = new ToolExecutionContext
                        {
                            UserId = userId,
                            WorkspaceRoot = workspaceRoot,
                            ClientIp = null,
                            CancellationToken = cancellationToken
                        };

                        try
                        {
                            toolResult = await _toolRegistry.ExecuteAsync(functionName, execContext, args);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка выполнения инструмента {Tool} в чате", functionName);
                            toolResult = ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
                        }
                    }

                    // SSE-событие: tool_result
                    yield return ChatStreamEvent.ToolResult(new ChatToolResultDto
                    {
                        Id = callId,
                        Name = functionName,
                        Success = toolResult.Success,
                        Content = toolResult.Data,
                        Message = toolResult.Message
                    });

                    // Сохраняем tool message в БД
                    try
                    {
                        await _chatService.AddMessageAsync(
                            request.ChatId, userId,
                            new ChatMessage
                            {
                                Role = RoleTool,
                                Content = JsonConvert.SerializeObject(new
                                {
                                    success = toolResult.Success,
                                    data = toolResult.Data,
                                    message = toolResult.Message
                                }),
                                ToolCallId = callId,
                                ToolName = functionName
                            },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка сохранения tool message");
                    }

                    // Добавляем в messages для следующей итерации
                    messages.Add(new JObject
                    {
                        ["role"] = RoleTool,
                        ["tool_call_id"] = callId ?? string.Empty,
                        ["content"] = JsonConvert.SerializeObject(new
                        {
                            success = toolResult.Success,
                            data = toolResult.Data,
                            message = toolResult.Message
                        })
                    });
                }

                // Цикл повторяется: следующий stream с обновлёнными messages
            }

            // 8. Обработка ошибок стрима
            if (streamError != null)
            {
                _logger.LogError(streamError, "Ошибка стрима LM Studio (chatId={ChatId})", request.ChatId);
                yield return ChatStreamEvent.Error($"Ошибка стрима: {streamError.Message}");
                yield break;
            }

            // 9. Лимит итераций исчерпан (вариант B — «извинение»)
            var limitMessage = "Извините, я достиг лимита вызовов инструментов. "
                             + "Пожалуйста, переформулируйте задачу или задайте более конкретный вопрос.";

            ChatMessage limitMsg = null;
            try
            {
                limitMsg = await _chatService.AddMessageAsync(
                    request.ChatId, userId,
                    new ChatMessage { Role = RoleAssistant, Content = limitMessage },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения limit message");
            }

            yield return ChatStreamEvent.Delta(limitMessage);

            if (limitMsg != null)
            {
                yield return ChatStreamEvent.Done(limitMsg.Id, tokensIn, tokensOut);
            }
        }

        /// <summary>
        /// Формирует JArray сообщений для LM Studio (OpenAI-формат).
        /// </summary>
        /// <param name="chat">Чат (для SystemPrompt)</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JArray сообщений</returns>
        private async Task<JArray> BuildMessagesAsync(
            Chat chat,
            int userId,
            CancellationToken cancellationToken)
        {
            var messages = new JArray();

            if (!string.IsNullOrWhiteSpace(chat.SystemPrompt))
            {
                messages.Add(new JObject
                {
                    ["role"] = RoleSystem,
                    ["content"] = chat.SystemPrompt
                });
            }

            var history = await _chatService.GetMessagesAsync(
                chat.Id, userId, MaxHistoryMessages, cancellationToken);

            foreach (var msg in history)
            {
                var obj = new JObject { ["role"] = msg.Role };

                // Assistant с tool_calls — content может быть null
                if (msg.Role == RoleAssistant && !string.IsNullOrEmpty(msg.ToolCallsJson))
                {
                    obj["content"] = string.IsNullOrEmpty(msg.Content)
                        ? (JToken)JValue.CreateNull()
                        : new JValue(msg.Content);
                    try
                    {
                        obj["tool_calls"] = JArray.Parse(msg.ToolCallsJson);
                    }
                    catch
                    {
                        // Битый JSON — пропускаем tool_calls
                        obj["content"] = msg.Content ?? string.Empty;
                    }
                }
                else if (msg.Role == RoleTool)
                {
                    obj["content"] = msg.Content ?? string.Empty;
                    obj["tool_call_id"] = msg.ToolCallId ?? string.Empty;
                }
                else
                {
                    obj["content"] = msg.Content ?? string.Empty;
                }

                messages.Add(obj);
            }

            return messages;
        }
    }
}