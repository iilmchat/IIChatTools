using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
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
        private const string RoleUser = "user";
        private const string RoleAssistant = "assistant";
        private const string RoleSystem = "system";

        private readonly IChatService _chatService;
        private readonly ILmStudioClient _lmStudioClient;
        private readonly ILogger<ChatStreamService> _logger;

        /// <summary>
        /// Создаёт сервис стриминга.
        /// </summary>
        /// <param name="chatService">Сервис CRUD чатов</param>
        /// <param name="lmStudioClient">Клиент LM Studio (SSE)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatStreamService(
            IChatService chatService,
            ILmStudioClient lmStudioClient,
            ILogger<ChatStreamService> logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatStreamRequest request,
            int userId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // 1. Валидация запроса
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
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

            // 3. Сохраняем user message (ошибка → в переменную, yield после catch)
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

            // 4. Сигнализируем начало стрима
            yield return ChatStreamEvent.Start(userMsg.Id, request.ChatId);

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

            // 6. Открываем SSE-стрим от LM Studio
            var assistantContent = new StringBuilder();
            int? tokensIn = null;
            int? tokensOut = null;
            Exception streamError = null;

            var stream = _lmStudioClient.ChatStreamAsync(messages, null, cancellationToken);
            var enumerator = stream.GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    ChatCompletionChunk chunk;
                    try
                    {
                        if (!await enumerator.MoveNextAsync()) break;
                        chunk = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Стрим отменён (chatId={ChatId})", request.ChatId);
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
                        assistantContent.Append(chunk.DeltaContent);
                        // yield вне catch — допустимо
                        yield return ChatStreamEvent.Delta(chunk.DeltaContent);
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
                await enumerator.DisposeAsync();
            }

            // 7. Обработка ошибок стрима
            if (streamError != null)
            {
                _logger.LogError(streamError, "Ошибка стрима LM Studio (chatId={ChatId})", request.ChatId);
                yield return ChatStreamEvent.Error($"Ошибка стрима: {streamError.Message}");
                yield break;
            }

            // 8. Сохраняем assistant message (ошибка → в переменную, yield после catch)
            var fullContent = assistantContent.ToString();
            ChatMessage assistantMsg = null;
            string saveError = null;
            try
            {
                assistantMsg = await _chatService.AddMessageAsync(
                    request.ChatId,
                    userId,
                    new ChatMessage
                    {
                        Role = RoleAssistant,
                        Content = fullContent,
                        TokensIn = tokensIn,
                        TokensOut = tokensOut
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения assistant message (chatId={ChatId})", request.ChatId);
                saveError = "Ответ получен, но не сохранён";
            }

            if (saveError != null)
            {
                yield return ChatStreamEvent.Error(saveError);
                yield break;
            }

            _logger.LogInformation(
                "Стрим завершён: chatId={ChatId}, userMsgId={UserMsgId}, assistantMsgId={AssistantMsgId}, tokensIn={TokensIn}, tokensOut={TokensOut}, len={Len}",
                request.ChatId, userMsg.Id, assistantMsg.Id, tokensIn, tokensOut, fullContent.Length);

            // 9. Сигнализируем завершение
            yield return ChatStreamEvent.Done(assistantMsg.Id, tokensIn, tokensOut);
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
                // tool-сообщения пока не поддерживаем — Фаза 1.6
                if (msg.Role == RoleUser || msg.Role == RoleAssistant || msg.Role == RoleSystem)
                {
                    messages.Add(new JObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? string.Empty
                    });
                }
            }

            return messages;
        }
    }
}