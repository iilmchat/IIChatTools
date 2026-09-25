using System;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API вложений к чату (v1.5.0, KI-083, Шаг 6B).
    ///
    /// <para>
    /// 4 endpoints:
    /// <list type="bullet">
    ///   <item><c>POST   /api/chat/{chatId}/attachments</c> — загрузить (multipart).</item>
    ///   <item><c>GET    /api/chat/{chatId}/attachments</c> — список вложений.</item>
    ///   <item><c>DELETE /api/chat/{chatId}/attachments/{attachmentId}</c> — удалить одно.</item>
    ///   <item><c>POST   /api/chat/{chatId}/attachments/clear</c> — очистить все.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Не использует</b> атрибут <c>[ApiController]</c>-автоматику ModelState
    /// (ProblemDetails): ошибки возвращаются в едином формате
    /// <c>{ success = false, message = ... }</c> — как во всех контроллерах проекта.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/chat/{chatId:int}/attachments")]
    [Authorize]
    public class ChatAttachmentsController : ControllerBase
    {
        /// <summary>
        /// Лимит multipart-запроса (40 MB) — с запасом на 32 MB файл + overhead.
        /// Согласован с Rag:Attachments:MaxFileSizeBytes (32 MB) и FormOptions/Kestrel.
        /// </summary>
        private const long MaxRequestSizeBytes = 41_943_040;

        private readonly IChatAttachmentService _attachmentService;
        private readonly IChatService _chatService;
        private readonly ILogger<ChatAttachmentsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="attachmentService">Сервис вложений (Scoped)</param>
        /// <param name="chatService">Сервис чатов (для проверки владения)</param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatAttachmentsController(
            IChatAttachmentService attachmentService,
            IChatService chatService,
            ILogger<ChatAttachmentsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        // ============================================================
        // POST /api/chat/{chatId}/attachments — загрузить файл
        // ============================================================

        /// <summary>
        /// Загружает файл в чат и индексирует его в <c>my_rag_docs</c>.
        /// </summary>
        /// <param name="chatId">Идентификатор чата (route)</param>
        /// <param name="file">Файл (multipart, поле <c>file</c>)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>
        /// JSON { success, data: ChatAttachmentDto } при успехе,
        /// либо { success: false, message } при ошибке валидации.
        /// </returns>
        [HttpPost]
        [RequestSizeLimit(MaxRequestSizeBytes)]
        public async Task<IActionResult> UploadAsync(
            int chatId,
            [FromForm] IFormFile file,
            CancellationToken cancellationToken)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Ok(new { success = false, message = "Файл не выбран или пустой." });
                }

                var userId = GetCurrentUserId();

                // Проверка владения чатом — не тратим время на парсинг/ingestion, если чат чужой.
                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                using var stream = file.OpenReadStream();

                var dto = await _attachmentService.UploadAsync(
                    chatId,
                    userId,
                    stream,
                    file.FileName,
                    file.ContentType,
                    cancellationToken);

                _logger.LogInformation(
                    "Attachment загружен: chatId={ChatId}, userId={UserId}, file={File}, size={Size}, chunks={Chunks}",
                    chatId, userId, dto.FileName, dto.SizeBytes, dto.ChunksCount);

                return Ok(new { success = true, data = dto });
            }
            catch (ArgumentException ex)
            {
                // Валидация лимитов / формата — из ChatAttachmentService.
                return Ok(new { success = false, message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки вложения: chatId={ChatId}", chatId);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============================================================
        // GET /api/chat/{chatId}/attachments — список
        // ============================================================

        /// <summary>
        /// Возвращает список вложений чата (сортировка по CreatedAt asc).
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: ChatAttachmentDto[] }</returns>
        [HttpGet]
        public async Task<IActionResult> GetListAsync(
            int chatId,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                // Проверка владения чатом — иначе возвращаем пустой список,
                // не палим существование чужого чата (обобщённый ответ).
                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                var list = await _attachmentService.GetForChatAsync(chatId, userId, cancellationToken);

                return Ok(new { success = true, data = list });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения вложений: chatId={ChatId}", chatId);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============================================================
        // DELETE /api/chat/{chatId}/attachments/{attachmentId}
        // ============================================================

        /// <summary>
        /// Удаляет вложение: файл с диска + чанки из <c>my_rag_docs</c> + запись из БД.
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="attachmentId">Идентификатор вложения</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success } или { success: false, message }</returns>
        [HttpDelete("{attachmentId:int}")]
        public async Task<IActionResult> DeleteAsync(
            int chatId,
            int attachmentId,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                var removed = await _attachmentService.DeleteAsync(
                    attachmentId, userId, cancellationToken);

                if (!removed)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                _logger.LogInformation(
                    "Attachment удалён: chatId={ChatId}, attachmentId={Id}, userId={UserId}",
                    chatId, attachmentId, userId);

                return Ok(new { success = true });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка удаления вложения: chatId={ChatId}, attachmentId={Id}",
                    chatId, attachmentId);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============================================================
        // POST /api/chat/{chatId}/attachments/clear
        // ============================================================

        /// <summary>
        /// Удаляет все вложения чата («Очистить RAG»).
        /// </summary>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>JSON { success, data: { removed } }</returns>
        [HttpPost("clear")]
        public async Task<IActionResult> ClearAsync(
            int chatId,
            CancellationToken cancellationToken)
        {
            try
            {
                var userId = GetCurrentUserId();

                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                var removed = await _attachmentService.ClearForChatAsync(
                    chatId, userId, cancellationToken);

                _logger.LogInformation(
                    "Очистка вложений: chatId={ChatId}, userId={UserId}, removed={Removed}",
                    chatId, userId, removed);

                return Ok(new { success = true, data = new { removed } });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка очистки вложений: chatId={ChatId}", chatId);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        // ============================================================
        // Helpers
        // ============================================================

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