using System;
using System.IO;
using System.Linq;
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
        private readonly IOcrProgressTracker _ocrProgressTracker;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly ILogger<ChatAttachmentsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="attachmentService">Сервис вложений (Scoped)</param>
        /// <param name="chatService">Сервис чатов (для проверки владения)</param>
        /// <param name="ocrProgressTracker">
        /// Трекер прогресса OCR (KI-204) — обогащает DTO в <c>GetListAsync</c>.
        /// </param>
        /// <param name="workspaceResolver">
        /// Резолвер workspace (KI-205) — для подсчёта PNG-страниц и чтения файлов.
        /// </param>
        /// <param name="logger">Логгер</param>
        /// <param name="localizer">Локализатор</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatAttachmentsController(
            IChatAttachmentService attachmentService,
            IChatService chatService,
            IOcrProgressTracker ocrProgressTracker,
            IWorkspaceResolver workspaceResolver,
            ILogger<ChatAttachmentsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _attachmentService = attachmentService ?? throw new ArgumentNullException(nameof(attachmentService));
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _ocrProgressTracker = ocrProgressTracker ?? throw new ArgumentNullException(nameof(ocrProgressTracker));
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
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

                // KI-204: обогащаем DTO прогрессом OCR (если идёт обработка).
                // KI-205: + количество сохранённых PNG-страниц.
                var userId2 = userId;   // для лямбды
                foreach (var dto in list)
                {
                    if (string.IsNullOrWhiteSpace(dto.FileName)) continue;

                    // KI-204: прогресс OCR.
                    var key = $"{chatId}:{dto.FileName}";
                    var progress = _ocrProgressTracker.Get(key);
                    if (progress != null && progress.TotalPages > 0)
                    {
                        dto.OcrProgress = progress;
                    }

                    // KI-205: количество PNG-страниц (best-effort, I/O).
                    try
                    {
                        var workspaceRoot = await _workspaceResolver
                            .GetWorkspacePathAsync(userId2);
                        var subfolder = "chat-attachments";   // совпадает с default
                        var safeName = System.IO.Path.GetFileName(dto.FileName);
                        var pagesDir = System.IO.Path.Combine(
                            workspaceRoot, subfolder, chatId.ToString(),
                            $"{safeName}-pages");

                        if (System.IO.Directory.Exists(pagesDir))
                        {
                            var count = System.IO.Directory
                                .EnumerateFiles(pagesDir, "*.png").Count();
                            dto.PagesCount = count;
                            dto.PagesAvailable = count > 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "GetListAsync: не удалось подсчитать PNG-страницы " +
                            "для attachment id={Id}", dto.Id);
                    }
                }

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
        // v1.13.x (KI-205): постраничный просмотр PNG сканов
        // ============================================================

        /// <summary>
        /// Возвращает список сохранённых PNG-страниц вложения (метаданные).
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="attachmentId">Идентификатор вложения.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// JSON <c>{ success, data: { pages: [{ pageNumber, sizeBytes }], count } }</c>
        /// или <c>{ success: false, message }</c>.
        /// </returns>
        [HttpGet("{attachmentId:int}/pages")]
        public async Task<IActionResult> GetPagesAsync(
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

                // Получаем метаданные вложения (проверка владения — в сервисе).
                var attachments = await _attachmentService.GetForChatAsync(
                    chatId, userId, cancellationToken);
                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                // Резолвим путь к папке pages.
                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId);
                var pagesDir = BuildPagesDirectory(
                    workspaceRoot, chatId, attachment.FileName);

                if (!System.IO.Directory.Exists(pagesDir))
                {
                    return Ok(new { success = true, data = new { pages = new object[0], count = 0 } });
                }

                var pages = System.IO.Directory
                    .EnumerateFiles(pagesDir, "*.png")
                    .Select(path =>
                    {
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                        // "page-1.png" → 1
                        if (fileName.StartsWith("page-", StringComparison.Ordinal)
                            && int.TryParse(fileName.Substring("page-".Length), out var num))
                        {
                            var fileInfo = new System.IO.FileInfo(path);
                            return new { pageNumber = num, sizeBytes = fileInfo.Length };
                        }
                        return null;
                    })
                    .Where(p => p != null)
                    .OrderBy(p => p.pageNumber)
                    .ToList();

                return Ok(new
                {
                    success = true,
                    data = new { pages, count = pages.Count }
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка получения PNG-страниц: chatId={ChatId}, attachmentId={Id}",
                    chatId, attachmentId);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        /// <summary>
        /// Возвращает PNG-страницу вложения.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="attachmentId">Идентификатор вложения.</param>
        /// <param name="pageNumber">Номер страницы (1-based).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>PNG (image/png) или 404.</returns>
        [HttpGet("{attachmentId:int}/pages/{pageNumber:int}")]
        public async Task<IActionResult> GetPageImageAsync(
            int chatId,
            int attachmentId,
            int pageNumber,
            CancellationToken cancellationToken)
        {
            try
            {
                if (pageNumber < 1)
                    return NotFound();

                var userId = GetCurrentUserId();

                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null) return NotFound();

                var attachments = await _attachmentService.GetForChatAsync(
                    chatId, userId, cancellationToken);
                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment == null) return NotFound();

                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId);
                var pagesDir = BuildPagesDirectory(
                    workspaceRoot, chatId, attachment.FileName);

                var pagePath = System.IO.Path.Combine(pagesDir, $"page-{pageNumber}.png");
                if (!System.IO.File.Exists(pagePath))
                    return NotFound();

                var bytes = await System.IO.File.ReadAllBytesAsync(
                    pagePath, cancellationToken);
                return File(bytes, "image/png");
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка получения PNG-страницы: chatId={ChatId}, " +
                    "attachmentId={Id}, page={Page}",
                    chatId, attachmentId, pageNumber);
                return NotFound();
            }
        }

        // ============================================================
        // v1.13.x (KI-207): text layer страницы (для выделения + поиска)
        // ============================================================

        /// <summary>
        /// Возвращает text layer страницы (words + bbox) — для рендера
        /// прозрачного слоя поверх PNG и поиска/выделения.
        ///
        /// <para>
        /// Если для страницы нет <c>page-N.json</c> (не OCR-страница и не
        /// текстовый PDF, или файл загружен до v1.13.x) — возвращается
        /// <c>{ success: false, message: "Text layer not found" }</c>.
        /// Frontend просто не рендерит слой.
        /// </para>
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="attachmentId">Идентификатор вложения.</param>
        /// <param name="pageNumber">Номер страницы (1-based).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// JSON <c>{ success, data: { width, height, words: [...] } }</c>
        /// или <c>{ success: false, message }</c>.
        /// </returns>
        [HttpGet("{attachmentId:int}/pages/{pageNumber:int}/ocr")]
        public async Task<IActionResult> GetPageTextLayerAsync(
            int chatId,
            int attachmentId,
            int pageNumber,
            CancellationToken cancellationToken)
        {
            try
            {
                if (pageNumber < 1)
                    return Ok(new { success = false, message = "Text layer not found" });

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

                var attachments = await _attachmentService.GetForChatAsync(
                    chatId, userId, cancellationToken);
                var attachment = attachments.FirstOrDefault(a => a.Id == attachmentId);
                if (attachment == null)
                {
                    return Ok(new
                    {
                        success = false,
                        message = _localizer["Ресурс не найден."].Value
                    });
                }

                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId);
                var pagesDir = BuildPagesDirectory(
                    workspaceRoot, chatId, attachment.FileName);

                var jsonPath = System.IO.Path.Combine(
                    pagesDir, $"page-{pageNumber}.json");

                if (!System.IO.File.Exists(jsonPath))
                {
                    return Ok(new { success = false, message = "Text layer not found" });
                }

                var json = await System.IO.File.ReadAllTextAsync(
                    jsonPath, System.Text.Encoding.UTF8, cancellationToken);

                // Возвращаем как есть — парсинг JSON на стороне фронта
                // (структура PageTextLayerDto фиксирована).
                return Content(json, "application/json");
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка получения text layer: chatId={ChatId}, " +
                    "attachmentId={Id}, page={Page}",
                    chatId, attachmentId, pageNumber);
                return Ok(new
                {
                    success = false,
                    message = _localizer["Внутренняя ошибка сервера."].Value
                });
            }
        }

        /// <summary>
        /// v1.13.x (KI-205): путь к папке PNG-страниц вложения.
        /// </summary>
        /// <param name="workspaceRoot">Корень workspace пользователя.</param>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="fileName">Оригинальное имя файла вложения.</param>
        private static string BuildPagesDirectory(
            string workspaceRoot, int chatId, string fileName)
        {
            const string subfolder = "chat-attachments";
            var safeName = System.IO.Path.GetFileName(fileName);
            return System.IO.Path.Combine(
                workspaceRoot, subfolder, chatId.ToString(),
                $"{safeName}-pages");
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