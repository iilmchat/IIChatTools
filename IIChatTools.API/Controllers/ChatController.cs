using System;
using System.Linq;
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
        private readonly ILogger<ChatController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="chatService">Сервис чатов</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatController(
            IChatService chatService,
            ILogger<ChatController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает список чатов текущего пользователя (сортировка по UpdatedAt desc).
        /// </summary>
        /// <returns>JSON { success, data: ChatListItemDto[] }</returns>
        [HttpGet]
        public async Task<IActionResult> GetChatsAsync()
        {
            try
            {
                var userId = GetCurrentUserId();
                var chats = await _chatService.GetUserChatsAsync(userId);

                var data = chats.Select(c => new ChatListItemDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Model = c.Model,
                    UpdatedAt = c.UpdatedAt,
                    // MessageCount = 0 до реализации отдельного метода подсчёта (см. KI-046).
                    MessageCount = 0
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
                        CreatedAt = m.CreatedAt
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