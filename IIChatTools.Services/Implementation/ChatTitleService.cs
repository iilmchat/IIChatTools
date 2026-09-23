using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация сервиса генерации названия чата.
    /// Использует <see cref="ILmStudioClient.CompleteAsync"/> (non-streaming)
    /// с коротким промптом. Fallback: ошибка → <c>null</c>, title не меняется.
    /// </summary>
    public class ChatTitleService : IChatTitleService
    {
        /// <summary>Максимальная длина названия (символов).</summary>
        private const int MaxTitleLength = 60;

        /// <summary>Сколько сообщений грузить для контекста (хватит первого user'а).</summary>
        private const int HistoryLimit = 5;

        private readonly IChatService _chatService;
        private readonly ILmStudioClient _lmStudioClient;
        private readonly ILogger<ChatTitleService> _logger;

        /// <summary>
        /// Создаёт сервис.
        /// </summary>
        /// <param name="chatService">Сервис чатов</param>
        /// <param name="lmStudioClient">Клиент LM Studio</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatTitleService(
            IChatService chatService,
            ILmStudioClient lmStudioClient,
            ILogger<ChatTitleService> logger)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _lmStudioClient = lmStudioClient ?? throw new ArgumentNullException(nameof(lmStudioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> GenerateAndSetTitleAsync(
            int chatId,
            int userId,
            CancellationToken cancellationToken = default)
        {
            var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
            if (chat == null)
            {
                _logger.LogWarning("GenerateTitle: чат {ChatId} не найден или не принадлежит user {UserId}", chatId, userId);
                return null;
            }

            var history = await _chatService.GetMessagesAsync(chatId, userId, HistoryLimit, cancellationToken);
            var firstUserMsg = history.FirstOrDefault(m =>
                string.Equals(m.Role, "user", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(m.Content));

            if (firstUserMsg == null)
            {
                _logger.LogDebug("GenerateTitle: в чате {ChatId} нет user-сообщений", chatId);
                return null;
            }

            var userText = firstUserMsg.Content.Trim();
            if (userText.Length > 500)
            {
                // Обрезаем очень длинные сообщения — экономим токены.
                userText = userText.Substring(0, 500);
            }

            var prompt = BuildPrompt(userText);
            var messages = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            };

            try
            {
                var response = await _lmStudioClient.CompleteAsync(
                    messages,
                    tools: null,
                    cancellationToken);

                var title = SanitizeTitle(response?.Content);

                if (string.IsNullOrWhiteSpace(title))
                {
                    _logger.LogWarning(
                        "GenerateTitle: LM Studio вернул пустое название для чата {ChatId}",
                        chatId);
                    return null;
                }

                await _chatService.UpdateChatAsync(
                    chatId,
                    userId,
                    title: title,
                    model: null,
                    systemPrompt: null,
                    cancellationToken);

                _logger.LogInformation(
                    "GenerateTitle: чат {ChatId} -> \"{Title}\"",
                    chatId, title);

                return title;
            }
            catch (OperationCanceledException)
            {
                // Отмена — тихий выход (пользователь ушёл).
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "GenerateTitle: не удалось сгенерировать название для чата {ChatId}",
                    chatId);
                return null;
            }
        }

        /// <summary>
        /// Формирует промпт для LLM.
        /// </summary>
        private static string BuildPrompt(string userMessage)
        {
            return
                "Ты — генератор коротких названий для чатов. " +
                "На основе первого сообщения пользователя придумай краткое название (2-5 слов), " +
                "отражающее суть запроса. Название должно быть на русском языке, " +
                "без кавычек, без точки в конце, без пояснений. Ответь ТОЛЬКО названием.\n\n" +
                $"Сообщение пользователя: {userMessage}";
        }

        /// <summary>
        /// Очищает ответ LLM: убирает кавычки, переносы строк, обрезает по длине.
        /// </summary>
        /// <param name="raw">Сырой ответ</param>
        /// <returns>Очищенное название или null</returns>
        private static string SanitizeTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Только первая строка (LLM может дописать пояснение).
            var firstLine = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .FirstOrDefault() ?? string.Empty;

            var title = firstLine.Trim();

            // Убираем обрамляющие кавычки (одинарные, двойные, «ёлочки»).
            title = title.Trim('"', '\'', '«', '»', '“', '”', '‘', '’');

            // Убираем завершающую точку/восклицательный/вопросительный.
            title = title.TrimEnd('.', '!', '?', '。', '！', '？');

            // Обрезаем по MaxTitleLength.
            if (title.Length > MaxTitleLength)
            {
                title = title.Substring(0, MaxTitleLength).TrimEnd();
            }

            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
    }
}