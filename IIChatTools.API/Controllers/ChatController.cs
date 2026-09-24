using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API для работы с чатами: список, детали, создание, обновление, удаление.
    /// Стриминг ответов — в отдельном ChatStreamController (v1.3 Фаза 1.5).
    /// </summary>
    [ApiController]
    [Route("api/chats")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IChatTitleService _chatTitleService;
        private readonly ILogger<ChatController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="chatService">Сервис чатов</param>
        /// <param name="chatTitleService">Сервис AI-генерации названий</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatController(
            IChatService chatService,
            IChatTitleService chatTitleService,
            ILogger<ChatController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _chatTitleService = chatTitleService ?? throw new ArgumentNullException(nameof(chatTitleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает список чатов текущего пользователя (сортировка по UpdatedAt desc).
        /// При указании <paramref name="search"/> — фильтрует по подстроке в
        /// названии чата или в содержимом сообщений (KI-068).
        /// </summary>
        /// <param name="search">
        /// Опциональный поисковый запрос. Пустой / отсутствует → весь список.
        /// </param>
        /// <returns>JSON { success, data: ChatListItemDto[] }</returns>
        [HttpGet]
        public async Task<IActionResult> GetChatsAsync([FromQuery] string search = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                var counts = await _chatService.GetMessageCountsAsync(userId);

                // KI-078B: при наличии поискового запроса — используем расширенный
                // метод с превью совпадения (для ⌘K-модалки / Ctrl+K).
                // Без search — плоский список (sidebar): snippet-поля = null.
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var results = await _chatService.SearchUserChatsWithSnippetAsync(userId, search);
                    var dataWithSnippet = results.Select(r => new ChatListItemDto
                    {
                        Id = r.Id,
                        Title = r.Title,
                        Model = r.Model,
                        UpdatedAt = r.UpdatedAt,
                        MessageCount = counts.TryGetValue(r.Id, out var cnt) ? cnt : 0,
                        MatchedField = r.MatchedField,
                        Snippet = r.Snippet,
                        SnippetMatchStart = r.SnippetMatchStart,
                        SnippetMatchLength = r.SnippetMatchLength
                    }).ToList();

                    return Ok(new { success = true, data = dataWithSnippet });
                }

                var chats = await _chatService.SearchUserChatsAsync(userId, null);
                var data = chats.Select(c => new ChatListItemDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Model = c.Model,
                    UpdatedAt = c.UpdatedAt,
                    MessageCount = counts.TryGetValue(c.Id, out var cnt) ? cnt : 0
                }).ToList();

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка чатов");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Возвращает детали чата с историей сообщений.
        /// </summary>
        /// <param name="id">Идентификатор чата</param>
        /// <param name="limit">Максимум сообщений (по умолчанию 50)</param>
        /// <returns>JSON { success, data: ChatDetailDto }</returns>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetChatAsync(int id, [FromQuery] int limit = 50)
        {
            try
            {
                var userId = GetCurrentUserId();

                var chat = await _chatService.GetChatAsync(id, userId);
                if (chat == null)
                {
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });
                }

                var messages = await _chatService.GetMessagesAsync(id, userId, limit);

                var data = new ChatDetailDto
                {
                    Id = chat.Id,
                    Title = chat.Title,
                    Model = chat.Model,
                    SystemPrompt = chat.SystemPrompt,
                    UpdatedAt = chat.UpdatedAt,
                    Messages = messages.Select(m => new ChatMessageDto
                    {
                        Id = m.Id,
                        Role = m.Role,
                        Content = m.Content,
                        ToolCallsJson = m.ToolCallsJson,
                        ToolCallId = m.ToolCallId,
                        ToolName = m.ToolName,
                        CreatedAt = m.CreatedAt,
                        // KI-049b: токены для отображения в meta-строке.
                        TokensIn = m.TokensIn,
                        TokensOut = m.TokensOut,
                        // KI-084a: статистика генерации.
                        DurationMs = m.DurationMs,
                        FirstTokenMs = m.FirstTokenMs,
                        FinishReason = m.FinishReason
                    }).ToList()
                };

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения чата {ChatId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Создаёт новый чат.
        /// </summary>
        /// <param name="request">Параметры чата (модель обязательна)</param>
        /// <returns>JSON { success, data: ChatListItemDto }</returns>
        [HttpPost]
        public async Task<IActionResult> CreateChatAsync([FromBody] CreateChatRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Model))
                {
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });
                }

                var userId = GetCurrentUserId();

                var chat = await _chatService.CreateChatAsync(userId, request.Model, request.Title);

                var data = new ChatListItemDto
                {
                    Id = chat.Id,
                    Title = chat.Title,
                    Model = chat.Model,
                    UpdatedAt = chat.UpdatedAt,
                    MessageCount = 0
                };

                _logger.LogInformation("Создан чат {ChatId} для пользователя {UserId}", chat.Id, userId);

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания чата");
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Обновляет метаданные чата (title / model / systemPrompt).
        /// Поля с <c>null</c> не изменяются.
        /// </summary>
        /// <param name="id">Идентификатор чата</param>
        /// <param name="request">Новые значения (null = не менять)</param>
        /// <returns>JSON { success, data: ChatListItemDto }</returns>
        [HttpPatch("{id:int}")]
        public async Task<IActionResult> UpdateChatAsync(int id, [FromBody] UpdateChatRequest request)
        {
            try
            {
                if (request == null)
                {
                    return Ok(new { success = false, message = _localizer["Некорректные данные запроса."].Value });
                }

                var userId = GetCurrentUserId();

                var chat = await _chatService.UpdateChatAsync(
                    id, userId,
                    request.Title,
                    request.Model,
                    request.SystemPrompt);

                if (chat == null)
                {
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });
                }

                var data = new ChatListItemDto
                {
                    Id = chat.Id,
                    Title = chat.Title,
                    Model = chat.Model,
                    UpdatedAt = chat.UpdatedAt,
                    MessageCount = 0
                };

                _logger.LogInformation("Обновлён чат {ChatId} пользователя {UserId}", id, userId);

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления чата {ChatId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Удаляет чат со всеми сообщениями.
        /// </summary>
        /// <param name="id">Идентификатор чата</param>
        /// <returns>JSON { success }</returns>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteChatAsync(int id)
        {
            try
            {
                var userId = GetCurrentUserId();

                var deleted = await _chatService.DeleteChatAsync(id, userId);
                if (!deleted)
                {
                    return Ok(new { success = false, message = _localizer["Ресурс не найден."].Value });
                }

                _logger.LogInformation("Удалён чат {ChatId} пользователя {UserId}", id, userId);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка удаления чата {ChatId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Генерирует короткое название чата на основе первого сообщения пользователя
        /// (Фаза 2.2.1, ChatGPT-style). Не изменяет title, если LLM недоступен.
        /// </summary>
        /// <param name="id">Идентификатор чата</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: { title } } или { success: false, message }</returns>
        [HttpPost("{id:int}/generate-title")]
        public async Task<IActionResult> GenerateTitleAsync(
            int id,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                var title = await _chatTitleService.GenerateAndSetTitleAsync(
                    id, userId, cancellationToken);

                if (string.IsNullOrWhiteSpace(title))
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Не удалось сгенерировать название."].Value
                    });
                }

                _logger.LogInformation(
                    "Сгенерировано название для чата {ChatId}: \"{Title}\"",
                    id, title);

                return Ok(new { success = true, data = new { title } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка генерации названия чата {ChatId}", id);
                return Ok(new { success = false, message = _localizer["Внутренняя ошибка сервера."].Value });
            }
        }

        /// <summary>
        /// Извлекает идентификатор текущего пользователя из claims.
        /// </summary>
        /// <returns>Идентификатор пользователя</returns>
        /// <exception cref="UnauthorizedAccessException">Если пользователь не аутентифицирован</exception>
        private int GetCurrentUserId()
        {
            var idRaw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idRaw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}