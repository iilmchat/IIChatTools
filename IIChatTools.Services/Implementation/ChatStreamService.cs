using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Agents;
using IIChatTools.Services.Implementation.ChatTools;
using IIChatTools.Services.Implementation.Rag;        // v1.6.0 (KI-086): RagSourceBuilder
using IIChatTools.Services.Implementation.Tools;
using IIChatTools.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
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

        /// <summary>Индекс RAG-чанков, приложенных к чату (v1.5.0, KI-083, Шаг 6C).</summary>
        private const string MyRagDocsIndex = "my_rag_docs";

        /// <summary>
        /// v1.6.0 (KI-086, Шаг 3.5c): имена RAG-инструментов, явно добавляемых
        /// в top-level список tools для Chat.
        ///
        /// <para>
        /// В v1.5.0 (KI-083, Фаза 5) RAG-tools были зарегистрированы в DI как
        /// <see cref="ITool"/>, но НЕ попали в <c>allowedNames</c> при формировании
        /// <c>tools[]</c> для LLM в <c>ChatStreamService.StreamAsync</c>.
        /// Список строился только из <c>SubAgentRegistry.GetEnabled()</c>
        /// (6 агентов) + <c>consult_secondary_agent</c> — итого 7 инструментов
        /// вместо ожидаемых 10.
        /// </para>
        ///
        /// <para>
        /// <b>Симптом:</b> LLM в чате не могла вызвать <c>search_knowledge_base</c>
        /// (его не было в <c>tools[]</c>) и выбирала <c>file_system_agent</c>
        /// как fallback — тот искал <c>RULES.md</c> в workspace пользователя
        /// и, разумеется, не находил.
        /// </para>
        /// </summary>
        private static readonly string[] RagToolNames =
        {
            "search_knowledge_base",
            "search_chat_history",
            "search_workspace"
        };

        /// <summary>
        /// v1.7.0 (KI-097, Фаза 5): имя Database Agent tool.
        /// Явно добавляется в <c>allowedNames</c> при построении <c>tools[]</c> для Chat.
        /// Если <c>SqlAgent:Enabled = false</c>, tool не зарегистрирован в DI —
        /// фильтр по имени в <see cref="ToolDefinitionsBuilder.Build"/> просто его
        /// пропустит (не найдёт descriptor).
        ///
        /// <para>
        /// RULES § 4.44: новый top-level <see cref="ITool"/> → обязательно добавить
        /// в <c>allowedNames</c>, иначе LLM физически не сможет его вызвать.
        /// </para>
        /// </summary>
        private const string DatabaseAgentToolName = "database_agent";

        /// <summary>
        /// v1.11.0 (KI-126, Шаг 1D): имя Actor-Critic оркестратора.
        /// Явно добавляется в <c>allowedNames</c> при построении <c>tools[]</c>
        /// для Chat (RULES § 4.44).
        /// </summary>
        private const string CodeAgentWithReviewToolName = "code_agent_with_review";

        /// <summary>Сколько top-K чанков вставлять в system prompt (по умолчанию).</summary>
        private const int DefaultAutoInjectTopK = 5;

        /// <summary>Минимальный score для вставки в system prompt (по умолчанию).</summary>
        private const float DefaultAutoInjectMinScore = 0.35f;

        /// <summary>
        /// Базовый system prompt, добавляемый к КАЖДОМУ чату.
        /// Даёт LLM явные правила выбора инструментов (в дополнение к
        /// Description'ам, которые 4B-модели часто игнорируют).
        ///
        /// <para>
        /// v1.11.0 (KI-126, Шаг 1E-fix4): после неудачной попытки усилить
        /// только <c>Description</c> у <c>code_agent_with_review</c> (Шаг 1E-fix3) —
        /// qwen3-4b всё равно выбирала <c>code_agent</c> (position-bias:
        /// алфавитный порядок tools + игнор описаний). System-prompt имеет
        /// более высокий приоритет в малых моделях.
        /// </para>
        /// </summary>
        private const string DefaultSystemPrompt =
            "Ты — ассистент IIChatTools с доступом к инструментам.\n" +
            "\n" +
            "ПРАВИЛА ВЫБОРА ИНСТРУМЕНТА (соблюдай строго):\n" +
            "\n" +
            "1. Задачи кодинга со словами/смыслом: алгоритм, парсер, валидация, " +
            "безопасность, security, производительность, edge cases, обработка " +
            "ошибок/исключений, unicode, сортировка, структуры данных, regex, " +
            "кодирование/декодирование, палиндром, работа с текстом — ВСЕГДА " +
            "используй инструмент `code_agent_with_review` (он делает ревью " +
            "через критика).\n" +
            "\n" +
            "2. Простые операции кодинга: rename, add import, исправление опечатки, " +
            "тривиальный однострочник, быстрая проверка — используй `code_agent`.\n" +
            "\n" +
            "3. Документация проекта (README.md, RULES.md, CHANGELOG.md, " +
            "KNOWN_ISSUES.md, ARCHITECTURE.md) — используй `search_knowledge_base`.\n" +
            "\n" +
            "4. Файлы пользователя в workspace — `file_system_agent`.\n" +
            "\n" +
            "5. Поиск в интернете / Wikipedia — `web_agent`.\n" +
            "\n" +
            "6. Вопросы про БД приложения (чаты, сообщения, аудит) — `database_agent`.\n" +
            "\n" +
            "7. Задача «исправить / доработать / отрефакторить код в файле X» " +
            "(даже если X — приложенный через 📎 файл) — используй " +
            "`code_agent_with_review` или `code_agent`, НЕ `file_system_agent`. " +
            "Приложенные файлы лежат в RAG-индексе чата, а не в workspace — " +
            "`file_system_agent` их не видит и вернёт «файл не найден». " +
            "`code_agent` умеет искать содержимое вложений через " +
            "`search_knowledge_base`.\n" +
            "\n" +
            "ПРИМЕР 1: «Напиши функцию для проверки палиндрома с обработкой edge cases» " +
            "→ вызови `code_agent_with_review`.\n" +
            "\n" +
            "ПРИМЕР 2: «Исправь код в файле code-12345.py (приложен)» " +
            "→ вызови `code_agent` (он найдёт содержимое через `search_knowledge_base`). " +
            "НЕ вызывай `file_system_agent`.";

        private readonly IChatService _chatService;
        private readonly ILmStudioClient _lmStudioClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly ISubAgentRegistry _subAgentRegistry;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly IChatApprovalCoordinator _approvalCoordinator;
        private readonly ITokenCounter _tokenCounter;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ChatStreamService> _logger;

        // v1.5.0 (KI-083, Шаг 6C): auto-inject top-K из my_rag_docs в system prompt.
        private readonly AppDbContext _db;
        private readonly IRetrievalService _retrievalService;

        /// <summary>
        /// Создаёт сервис стриминга.
        /// </summary>
        /// <param name="chatService">Сервис CRUD чатов</param>
        /// <param name="lmStudioClient">Клиент LM Studio (SSE)</param>
        /// <param name="toolRegistry">Реестр инструментов (для tool calling в чате)</param>
        /// <param name="subAgentRegistry">Реестр специализированных суб-агентов (v1.4.0 Фаза 5, KI-052)</param>
        /// <param name="workspaceResolver">Резолвер рабочего пространства пользователя</param>
        /// <param name="approvalCoordinator">Координатор подтверждений tool call (Singleton)</param>
        /// <param name="tokenCounter">Счётчик токенов (v1.4.x, KI-049)</param>
        /// <param name="configuration">Конфигурация приложения</param>
        /// <param name="logger">Логгер</param>
        /// <param name="db">Контекст БД (для проверки наличия чанков my_rag_docs, Шаг 6C)</param>
        /// <param name="retrievalService">Сервис поиска по векторным индексам (Шаг 6C)</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatStreamService(
            IChatService chatService,
            ILmStudioClient lmStudioClient,
            IToolRegistry toolRegistry,
            ISubAgentRegistry subAgentRegistry,
            IWorkspaceResolver workspaceResolver,
            IChatApprovalCoordinator approvalCoordinator,
            ITokenCounter tokenCounter,
            IConfiguration configuration,
            ILogger<ChatStreamService> logger,
            AppDbContext db,
            IRetrievalService retrievalService)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _subAgentRegistry = subAgentRegistry ?? throw new ArgumentNullException(nameof(subAgentRegistry));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
            _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
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

            // v1.6.0 (KI-086): аккумулятор источников за весь stream.
            // Собираются из: (а) auto-inject RAG-чанков (Шаг 6C), (б) tool_result'ов.
            // Ключ дедупликации — (Type|DocumentPath|ChunkIndex).
            var accumulatedSources = new List<ChatSourceDto>();
            var seenSourceKeys = new HashSet<string>(StringComparer.Ordinal);

            // KI-084b: токены user-сообщения — для SSE-события `start`.
            // В Regenerate-flow читаем из БД (могут быть null для старых сообщений).
            int? startUserTokens = null;

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

                // KI-065: deleted == 0 — это норма, если предыдущий ответ был отменён
                // через Stop или не сгенерирован (последнее сообщение — user).
                // Best-effort удаление: продолжаем от последнего user-сообщения.

                var history = await _chatService.GetMessagesAsync(
                    request.ChatId, userId, MaxHistoryMessages, cancellationToken);

                var lastUser = history.LastOrDefault(m => m.Role == RoleUser);
                if (lastUser == null)
                {
                    yield return ChatStreamEvent.Error("Не найдено user-сообщение");
                    yield break;
                }

                startUserMessageId = lastUser.Id;
                startUserTokens = lastUser.TokensIn;   // KI-084b

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
                    // KI-049: считаем токены user-сообщения (tiktoken-приближение).
                    var userTokens = _tokenCounter.CountTokens(request.Message);

                    userMsg = await _chatService.AddMessageAsync(
                        request.ChatId,
                        userId,
                        new ChatMessage
                        {
                            Role = RoleUser,
                            Content = request.Message,
                            TokensIn = userTokens
                        },
                        cancellationToken);

                    startUserTokens = userTokens;   // KI-084b
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
            //    KI-084b: передаём токены user-сообщения, чтобы UI показал сразу.
            yield return ChatStreamEvent.Start(startUserMessageId, request.ChatId, startUserTokens);

            // 5. Формируем историю для LM Studio
            //    v1.5.0 (KI-083, Шаг 6C): передаём startUserMessageId —
            //    для auto-inject top-K из my_rag_docs в system prompt.
            //    v1.6.0 (KI-086): возвращает также sources auto-inject — их
            //    кладём в accumulatedSources (не приходят через tool_result).
            JArray messages = null;
            IReadOnlyList<ChatSourceDto> ragSources = null;
            string historyError = null;
            try
            {
                (messages, ragSources) = await BuildMessagesAsync(
                    chat, userId, startUserMessageId, cancellationToken);
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

            // v1.6.0 (KI-086): sources от auto-inject идут в accumulator
            // до первого tool-цикла, чтобы попасть в финальный assistant.
            AddSourcesToAccumulator(accumulatedSources, seenSourceKeys, ragSources);

            // 6. Формируем tools (v1.4.0 Фаза 5, KI-052):
            //    Chat видит 6 специализированных агентов + consult_secondary_agent (fallback).
            //    Список резолвится из SubAgentRegistry (singleton), а не из SubAgent:DefaultAllowedTools.
            JArray tools = null;
            if (request.UseTools)
            {
                var enabledAgents = _subAgentRegistry.GetEnabled();
                var allowedNames = enabledAgents
                    .Select(a => a.Name)
                    .ToList();

                // consult_secondary_agent — универсальный fallback (browser + всё остальное).
                // Всегда доступен, независимо от реестра.
                if (!allowedNames.Contains(ParentToolName, StringComparer.OrdinalIgnoreCase))
                {
                    allowedNames.Add(ParentToolName);
                }

                // v1.6.0 (KI-086, Шаг 3.5c): явно добавляем RAG-tools.
                // Без этого Chat видел только 7 инструментов (6 агентов + consult),
                // и LLM физически не могла вызвать search_knowledge_base — его не было
                // в tools[] (см. RagToolNames — подробное объяснение).
                foreach (var ragName in RagToolNames)
                {
                    if (!allowedNames.Contains(ragName, StringComparer.OrdinalIgnoreCase))
                    {
                        allowedNames.Add(ragName);
                    }
                }

                // v1.7.0 (KI-097, Фаза 5): явно добавляем Database Agent tool.
                // RULES § 4.44: новый top-level ITool → обязательно в allowedNames.
                // Если SqlAgent:Enabled = false, tool не зарегистрирован в DI —
                // фильтр по имени в ToolDefinitionsBuilder.Build его пропустит.
                if (!allowedNames.Contains(DatabaseAgentToolName, StringComparer.OrdinalIgnoreCase))
                {
                    allowedNames.Add(DatabaseAgentToolName);
                }

                // v1.11.0 (KI-126, Шаг 1D): Actor-Critic оркестратор.
                // RULES § 4.44 — новый top-level ITool → обязательно в allowedNames.
                if (!allowedNames.Contains(CodeAgentWithReviewToolName, StringComparer.OrdinalIgnoreCase))
                {
                    allowedNames.Add(CodeAgentWithReviewToolName);
                }

                tools = ToolDefinitionsBuilder.Build(
                    _toolRegistry.GetAllDescriptors(),
                    allowedNames: allowedNames);

                _logger.LogInformation(
                    "Chat tools: {Count} инструментов ({Names})",
                    tools.Count,
                    string.Join(", ", allowedNames));
            }

            // 7. Multi-turn loop (до MaxToolIterations итераций)
            var allAssistantMessageIds = new List<int>();
            var finalAssistantContent = new StringBuilder();
            int? tokensIn = null;
            int? tokensOut = null;
            Exception streamError = null;

            // KI-084a: статистика генерации (Stopwatch — от старта до done).
            var requestStopwatch = Stopwatch.StartNew();
            long? firstTokenMs = null;
            string lastFinishReason = null;

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
                            // KI-084a: фиксируем время до первого delta (один раз).
                            firstTokenMs ??= requestStopwatch.ElapsedMilliseconds;

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

                        // KI-084a: запоминаем последний непустой finish_reason.
                        if (!string.IsNullOrEmpty(chunk.FinishReason))
                        {
                            lastFinishReason = chunk.FinishReason;
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

                    // KI-049: если LM Studio отдала usage (не stream) — используем
                    // точные значения; иначе считаем через tiktoken.
                    // KI-084b: переносим объявление ДО try — значения нужны
                    // для `yield return Done` после try-блока (scope).
                    var contextTokens = tokensIn
                        ?? _tokenCounter.CountConversation(
                            messages.Select(m => (
                                Role: m["role"]?.ToString() ?? "user",
                                Content: m["content"]?.ToString() ?? string.Empty)));
                    var completionTokens = tokensOut
                        ?? _tokenCounter.CountTokens(partialContent);

                    // KI-084a: останавливаем Stopwatch — финальный ответ готов.
                    requestStopwatch.Stop();
                    var totalDurationMs = requestStopwatch.ElapsedMilliseconds;

                    // Перезаписываем tokensIn/tokensOut — пригодятся в Done и в
                    // дальнейшей логике (limit-message ниже).
                    tokensIn = contextTokens;
                    tokensOut = completionTokens;

                    try
                    {
                        finalMsg = await _chatService.AddMessageAsync(
                            request.ChatId, userId,
                            new ChatMessage
                            {
                                Role = RoleAssistant,
                                Content = partialContent,
                                TokensIn = contextTokens,
                                TokensOut = completionTokens,
                                // KI-084a: статистика генерации.
                                DurationMs = totalDurationMs,
                                FirstTokenMs = firstTokenMs,
                                FinishReason = lastFinishReason,
                                // v1.6.0 (KI-086): источники для UI-блока
                                // «Источники» под ответом ассистента.
                                MetadataJson = SerializeSources(accumulatedSources)
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

                    // KI-084b + KI-084a: передаём статистику — UI покажет всё
                    // сразу, без F5 + GET /api/chats/{id}.
                    // v1.6.0 (KI-086): sources — для UI-блока «Источники».
                    yield return ChatStreamEvent.Done(
                        finalMsg.Id,
                        tokensIn,
                        tokensOut,
                        totalDurationMs,
                        firstTokenMs,
                        lastFinishReason,
                        accumulatedSources.Count > 0 ? accumulatedSources : null);
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

                    // Определяем requiresApproval.
                    // v1.7.0 (KI-101): per-call approval через ITool.RequiresApprovalForCall.
                    // Default-реализация возвращает RequiresApprovalByDefault — для 46
                    // существующих инструментов поведение не меняется. DatabaseAgentTool
                    // переопределяет: execute_query → approval, метаданные → без approval.
                    var toolInstance = _toolRegistry.GetTool(functionName);
                    var requiresApproval = toolInstance?.RequiresApprovalForCall(args) ?? true;

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
                                CancellationToken = cancellationToken,
                                ChatId = request.ChatId   // v1.11.0 (KI-126, 1E-part2)
                            };

                            var holder = new ToolStreamingResult();
                            await foreach (var evt in ExecuteToolWithStreamingAsync(
                                functionName, execContext, args, holder, cancellationToken))
                            {
                                yield return evt;
                            }
                            toolResult = holder.Result;
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
                            CancellationToken = cancellationToken,
                            ChatId = request.ChatId   // v1.11.0 (KI-126, 1E-part2)
                        };

                        var holder = new ToolStreamingResult();
                        await foreach (var evt in ExecuteToolWithStreamingAsync(
                            functionName, execContext, args, holder, cancellationToken))
                        {
                            yield return evt;
                        }
                        toolResult = holder.Result;
                    }

                    // SSE-событие: tool_result
                    // v1.6.0 (KI-086): проброс sources — UI покажет «Источники»
                    // под ответом ассистента (Шаг 4) без F5.
                    yield return ChatStreamEvent.ToolResult(new ChatToolResultDto
                    {
                        Id = callId,
                        Name = functionName,
                        Success = toolResult.Success,
                        Content = toolResult.Data,
                        Message = toolResult.Message,
                        Sources = toolResult.Sources
                    });

                    // v1.6.0 (KI-086): собрать sources от инструмента (RAG-tools).
                    // Дедупликация — в AddSourcesToAccumulator.
                    AddSourcesToAccumulator(accumulatedSources, seenSourceKeys, toolResult.Sources);

                    // v1.11.0 (KI-129): для code_agent_with_review сохраняем
                    // sessionId в MetadataJson tool-сообщения. Это позволит
                    // ChatController при F5 связать toolCallId → debate-сессия
                    // и восстановить блок дебатов в UI.
                    //
                    // Данные приходят в ToolResult.Data.sessionId (возвращается
                    // CodeAgentWithReviewTool при успехе).
                    string toolMetadataJson = null;
                    if (string.Equals(
                            functionName,
                            CodeAgentWithReviewToolName,
                            StringComparison.Ordinal)
                        && toolResult.Success
                        && toolResult.Data != null)
                    {
                        try
                        {
                            var dataJson = toolResult.Data is JObject jObj
                                ? jObj
                                : JObject.FromObject(toolResult.Data);

                            var sid = dataJson["sessionId"]?.Value<int?>();
                            if (sid.HasValue && sid.Value > 0)
                            {
                                toolMetadataJson = JsonConvert.SerializeObject(
                                    new { debateSessionId = sid.Value });
                            }
                        }
                        catch (Exception metaEx)
                        {
                            // Не критично — при F5 блок дебатов не восстановится,
                            // но сам tool-result сохранится.
                            _logger.LogWarning(metaEx,
                                "Не удалось извлечь sessionId из tool_result " +
                                "code_agent_with_review (callId={CallId})",
                                callId);
                        }
                    }

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
                                ToolName = functionName,
                                // v1.11.0 (KI-129): debateSessionId для F5.
                                MetadataJson = toolMetadataJson
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
            int? limitTokensIn = null;
            int? limitTokensOut = null;
            long? limitDurationMs = null;
            try
            {
                // KI-084b: считаем токены для limit-сообщения.
                limitTokensIn = tokensIn
                    ?? _tokenCounter.CountConversation(
                        messages.Select(m => (
                            Role: m["role"]?.ToString() ?? "user",
                            Content: m["content"]?.ToString() ?? string.Empty)));
                limitTokensOut = _tokenCounter.CountTokens(limitMessage);

                // KI-084a: статистика для limit-сообщения.
                requestStopwatch.Stop();
                limitDurationMs = requestStopwatch.ElapsedMilliseconds;

                limitMsg = await _chatService.AddMessageAsync(
                    request.ChatId, userId,
                    new ChatMessage
                    {
                        Role = RoleAssistant,
                        Content = limitMessage,
                        TokensIn = limitTokensIn,
                        TokensOut = limitTokensOut,
                        DurationMs = limitDurationMs,
                        FirstTokenMs = firstTokenMs,
                        FinishReason = lastFinishReason,
                        // v1.6.0 (KI-086): источники, собранные до лимита.
                        MetadataJson = SerializeSources(accumulatedSources)
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения limit message");
            }

            yield return ChatStreamEvent.Delta(limitMessage);

            if (limitMsg != null)
            {
                yield return ChatStreamEvent.Done(
                    limitMsg.Id,
                    limitTokensIn,
                    limitTokensOut,
                    limitDurationMs,
                    firstTokenMs,
                    lastFinishReason,
                    accumulatedSources.Count > 0 ? accumulatedSources : null);
            }
        }

        /// <summary>
        /// Выполняет tool с параллельным стримингом SSE-событий из
        /// <see cref="ToolExecutionContext.EventWriter"/> (v1.11.0, KI-126, Шаг 1E-part2).
        ///
        /// <para>
        /// Используется для tool'ов, которые эмитят события во время своего
        /// выполнения (<c>code_agent_with_review</c>). Иначе события копились бы
        /// до завершения tool'а — UI не увидел бы прогресс.
        /// </para>
        /// </summary>
        /// <param name="functionName">Имя tool'а.</param>
        /// <param name="ctx">Контекст выполнения (без <c>EventWriter</c>).</param>
        /// <param name="args">Аргументы вызова.</param>
        /// <param name="resultHolder">
        /// Контейнер для финального <see cref="ToolResult"/> — обходной путь,
        /// т.к. async-enumerable не может вернуть значение через <c>ref</c>/<c>out</c>.
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Поток SSE-событий из tool'а, финальный <see cref="ToolResult"/> —
        /// через <paramref name="resultHolder"/>.
        /// </returns>
        private async IAsyncEnumerable<ChatStreamEvent> ExecuteToolWithStreamingAsync(
            string functionName,
            ToolExecutionContext ctx,
            JObject args,
            ToolStreamingResult resultHolder,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (resultHolder == null) throw new ArgumentNullException(nameof(resultHolder));

            var channel = Channel.CreateUnbounded<ChatStreamEvent>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

            ctx.EventWriter = channel.Writer;

            // Запускаем tool без ожидания.
            var toolTask = _toolRegistry.ExecuteAsync(functionName, ctx, args);

            // Стримим события, пока tool не завершён.
            // ВАЖНО: yield не разрешён в try-catch (CS1631) — поэтому try-catch
            // вынесен за пределы цикла.
            while (!cancellationToken.IsCancellationRequested)
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }

                if (toolTask.IsCompleted) break;

                var readTask = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                await Task.WhenAny(readTask, toolTask);
            }

            // Финализация — читаем всё, что осталось в буфере.
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out var evt))
            {
                yield return evt;
            }

            // Обработка результата tool'а (try-catch без yield — OK).
            ToolResult result;
            try
            {
                result = await toolTask;
            }
            catch (OperationCanceledException)
            {
                result = ToolResult.Fail("Операция отменена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения инструмента {Tool}", functionName);
                result = ToolResult.Fail($"Ошибка выполнения: {ex.Message}");
            }

            resultHolder.Result = result ?? ToolResult.Fail("Пустой результат");
        }

        /// <summary>
        /// Контейнер для финального <see cref="ToolResult"/>, возвращаемого
        /// из <see cref="ExecuteToolWithStreamingAsync"/> (обходной путь:
        /// async-enumerable не может вернуть значение через ref или out).
        /// </summary>
        private sealed class ToolStreamingResult
        {
            public ToolResult Result { get; set; }
        }

        /// <summary>
        /// Формирует JArray сообщений для LM Studio (OpenAI-формат).
        ///
        /// <para>
        /// v1.5.0 (KI-083, Шаг 6C): если у чата есть attached-чанки в
        /// <c>my_rag_docs</c> — извлекает top-K релевантных фрагментов
        /// по тексту последнего user-сообщения и вставляет их в system prompt
        /// (перед оригинальным <see cref="Chat.SystemPrompt"/>).
        /// </para>
        /// </summary>
        /// <param name="chat">Чат (для SystemPrompt)</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="startUserMessageId">ID последнего user-сообщения (для RAG-запроса)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JArray сообщений</returns>
        private async Task<(JArray Messages, IReadOnlyList<ChatSourceDto> RagSources)> BuildMessagesAsync(
            Chat chat,
            int userId,
            int startUserMessageId,
            CancellationToken cancellationToken)
        {
            var messages = new JArray();

            // v1.11.0 (KI-126, Шаг 1E-fix4): базовый system prompt с правилами
            // выбора инструментов. Идёт ПЕРВЫМ — стабильный префикс для KV-cache.
            // 4B-модель (qwen3-4b) часто игнорирует Description'ы tools, но
            // system-prompt имеет высокий приоритет.
            messages.Add(new JObject
            {
                ["role"] = RoleSystem,
                ["content"] = DefaultSystemPrompt
            });

            // Auto-inject RAG-контекста (KI-083, Шаг 6C).
            // v1.6.0 (KI-086): метод также возвращает sources — для UI-блока.
            var (ragContext, ragSources) = await BuildRagContextAsync(
                chat.Id, userId, startUserMessageId, cancellationToken);

            // v1.8.2 (prefix stability): RAG-контекст — ОТДЕЛЬНЫМ system-сообщением
            // ПОСЛЕ основного chat.SystemPrompt (раньше склеивались в одно).
            //
            // Цель: стабильный префикс (chat.SystemPrompt) не меняется между
            // запросами → KV-cache LM Studio не инвалидируется при добавлении /
            // удалении attachments. TTFT сокращается на 30-60% при наличии RAG.
            // Без attachments поведение не меняется (ragContext == null → RAG
            // не добавляется, остаётся только основной system prompt).
            //
            // Порядок: сначала chat.SystemPrompt (стабильно), потом ragContext
            // (динамика). LLM видит RAG-блок ближе к user-сообщению — внимание
            // к релевантному контексту выше.
            if (!string.IsNullOrWhiteSpace(chat.SystemPrompt))
            {
                messages.Add(new JObject
                {
                    ["role"] = RoleSystem,
                    ["content"] = chat.SystemPrompt
                });
            }

            if (!string.IsNullOrEmpty(ragContext))
            {
                messages.Add(new JObject
                {
                    ["role"] = RoleSystem,
                    ["content"] = ragContext
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

            return (messages, ragSources);
        }

        /// <summary>
        /// Формирует RAG-блок для system prompt на основе top-K релевантных
        /// чанков из <c>my_rag_docs</c> для указанного чата
        /// (v1.5.0, KI-083, Шаг 6C).
        ///
        /// <para>
        /// v1.6.0 (KI-086): возвращает также список sources — для UI-блока
        /// «Источники» под ответом ассистента. Формат — <see cref="ChatSourceDto"/>.
        /// </para>
        ///
        /// <para>
        /// Возвращает <c>(null, null)</c>, если:
        /// <list type="bullet">
        ///   <item>у чата нет чанков в <c>my_rag_docs</c> (не приложены файлы);</item>
        ///   <item>текст user-сообщения пустой;</item>
        ///   <item>все чанки ниже <c>Rag:Attachments:AutoInjectMinScore</c>;</item>
        ///   <item>при ошибке retrieval (логируем и продолжаем без RAG).</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="chatId">ID чата</param>
        /// <param name="userId">ID пользователя-владельца</param>
        /// <param name="startUserMessageId">ID user-сообщения (для поиска)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>
        /// Кортеж: (текст RAG-блока, список sources). Оба поля — <c>null</c>,
        /// если RAG-контекст не сформирован.
        /// </returns>
        private async Task<(string RagContext, IReadOnlyList<ChatSourceDto> Sources)> BuildRagContextAsync(
            int chatId,
            int userId,
            int startUserMessageId,
            CancellationToken cancellationToken)
        {
            try
            {
                // 1. Быстрая проверка: есть ли чанки в my_rag_docs для этого чата?
                //    Если нет — не тратим время на embedding query.
                var hasChunks = await _db.DocumentChunks
                    .AnyAsync(c => c.IndexName == MyRagDocsIndex && c.ChatId == chatId,
                        cancellationToken);

                if (!hasChunks)
                {
                    return (null, null);
                }

                // 2. Текст последнего user-сообщения (из БД — для regenerate-совместимости).
                var lastUserText = await _db.ChatMessages
                    .Where(m => m.Id == startUserMessageId)
                    .Select(m => m.Content)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(lastUserText))
                {
                    return (null, null);
                }

                // 3. Retrieval.
                var topK = GetIntConfig("Rag:Attachments:AutoInjectTopK", DefaultAutoInjectTopK);
                var minScore = GetFloatConfig(
                    "Rag:Attachments:AutoInjectMinScore", DefaultAutoInjectMinScore);

                var chunks = await _retrievalService.SearchAsync(
                    query: lastUserText,
                    indexName: MyRagDocsIndex,
                    topK: topK,
                    chatId: chatId,
                    userId: userId,
                    cancellationToken: cancellationToken);

                var filtered = chunks.Where(c => c.Score >= minScore).ToList();
                if (filtered.Count == 0)
                {
                    return (null, null);
                }

                // 4. Формируем блок (формат из DESIGN § 4.7.5).
                var sb = new StringBuilder();
                sb.AppendLine("Ниже — релевантные фрагменты из прикреплённых пользователем документов.");
                sb.AppendLine("Используй их для ответа. Ссылайся на них как [1], [2] — в конце ответа");
                sb.AppendLine("дай список источников.");
                sb.AppendLine();

                for (int i = 0; i < filtered.Count; i++)
                {
                    var chunk = filtered[i];
                    var source = chunk.DocumentPath ?? "(unknown)";
                    sb.AppendLine($"[{i + 1}] {source} (фрагмент {chunk.ChunkIndex}):");
                    sb.AppendLine(chunk.Text ?? string.Empty);
                    sb.AppendLine();
                }

                _logger.LogDebug(
                    "Auto-inject RAG: chatId={ChatId}, вставлено чанков={Count}, " +
                    "минимальный score={MinScore}",
                    chatId, filtered.Count, minScore);

                // v1.6.0 (KI-086): sources для UI-блока «Источники».
                var sources = RagSourceBuilder.Build(filtered);

                return (sb.ToString().TrimEnd(), sources);
            }
            catch (Exception ex)
            {
                // Не падаем — продолжаем без RAG-контекста.
                _logger.LogWarning(ex,
                    "Auto-inject RAG-контекста упал для chatId={ChatId}. Продолжаем без RAG.",
                    chatId);
                return (null, null);
            }
        }

        /// <summary>
        /// v1.6.0 (KI-086): добавляет sources в аккумулятор с дедупликацией.
        ///
        /// <para>
        /// Ключ дедупликации — <c>(Type|DocumentPath|Url|ChunkIndex)</c>.
        /// Включает <c>Url</c> — иначе web/wiki-источники (у которых
        /// <c>DocumentPath = null</c> и <c>ChunkIndex = null</c>) дают
        /// одинаковый ключ и схлопываются в один.
        /// </para>
        ///
        /// <para>
        /// <b>Bugfix v1.6.1:</b> до этого фикса ключ был без <c>Url</c> —
        /// 7 web/wiki-источников от <c>web_agent</c> превращались в 1.
        /// Симметрично <c>SubAgentService.AddSourcesToAccumulator</c> (Шаг B).
        /// </para>
        /// </summary>
        /// <param name="accumulator">Список-приёмник (мутируется)</param>
        /// <param name="seenKeys">Множество ключей (мутируется)</param>
        /// <param name="sources">Источники для добавления (может быть null)</param>
        private static void AddSourcesToAccumulator(
            List<ChatSourceDto> accumulator,
            HashSet<string> seenKeys,
            IReadOnlyList<ChatSourceDto> sources)
        {
            if (sources == null || sources.Count == 0)
                return;

            foreach (var s in sources)
            {
                if (s == null)
                    continue;

                // v1.6.1: 4-полевой ключ, включая Url. `?? ""` — защита
                // от null-полей (RAG-чанки не имеют Url; web/wiki — не имеют
                // DocumentPath/ChunkIndex).
                var key = $"{s.Type}|{s.DocumentPath ?? string.Empty}|" +
                          $"{s.Url ?? string.Empty}|{s.ChunkIndex?.ToString() ?? string.Empty}";

                if (seenKeys.Add(key))
                {
                    accumulator.Add(s);
                }
            }
        }

        /// <summary>
        /// v1.6.0 (KI-086): настройки сериализации <c>MetadataJson</c> —
        /// camelCase (как в SSE), без null-полей (компактнее).
        /// </summary>
        private static readonly JsonSerializerSettings MetadataJsonSettings =
            new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore
            };

        /// <summary>
        /// v1.6.0 (KI-086): сериализует список sources в JSON для
        /// <c>ChatMessage.MetadataJson</c>. Пустой список → <c>null</c>
        /// (поле не пишем, БД не растёт).
        ///
        /// <para>
        /// Формат: <c>{ "sources": [ { "type": "rag", ... }, ... ] }</c> —
        /// camelCase, как в SSE-событии <c>done</c>. Единый формат для
        /// live-режима и F5-загрузки.
        /// </para>
        /// </summary>
        /// <param name="sources">Источники (может быть null / пустой)</param>
        /// <returns>
        /// JSON-строка <c>{ "sources": [...] }</c> или <c>null</c>.
        /// </returns>
        private static string SerializeSources(IReadOnlyList<ChatSourceDto> sources)
        {
            if (sources == null || sources.Count == 0)
                return null;

            return JsonConvert.SerializeObject(new { sources }, MetadataJsonSettings);
        }

        /// <summary>
        /// Читает int из конфига с fallback.
        /// </summary>
        private int GetIntConfig(string key, int defaultValue)
        {
            var raw = _configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>
        /// Читает float из конфига с fallback (InvariantCulture).
        /// </summary>
        private float GetFloatConfig(string key, float defaultValue)
        {
            var raw = _configuration[key];
            return float.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : defaultValue;
        }
    }
}
