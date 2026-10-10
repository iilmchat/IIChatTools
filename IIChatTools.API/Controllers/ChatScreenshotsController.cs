using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.API.Resources;
using IIChatTools.Services.Implementation;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API скриншотов Vision Agent (v1.13.x, KI-229).
    ///
    /// <para>
    /// <b>Назначение:</b> F5-fallback для PNG, которые <c>vision_agent</c>
    /// сохранил в workspace пользователя (KI-176 — base64 не хранится в БД).
    /// Live-рендер работает через SSE (base64 в <c>tool_result</c>);
    /// после F5 фронт получает из БД только <c>path</c> и запрашивает
    /// PNG через этот endpoint.
    /// </para>
    ///
    /// <para>
    /// <b>Путь:</b> <c>{user_workspace}/screenshots/{taskId}/{fileName}</c>.
    /// taskId и fileName валидируются регуляркой (только <c>[a-zA-Z0-9_.\-]</c>) —
    /// защита от path-traversal (RULES § 1.9). Дополнительно —
    /// <see cref="PathHelper.TryGetSafeFullPath"/>.
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/chat/{chatId:int}/screenshots")]
    [Authorize]
    public class ChatScreenshotsController : ControllerBase
    {
        /// <summary>
        /// Разрешённые символы в taskId / fileName. Отсекает «/», «\», «..» и
        /// любые другие спецсимволы до вызова PathHelper.
        /// </summary>
        private static readonly Regex SafeSegmentRegex = new Regex(
            @"^[a-zA-Z0-9_.\-]+$", RegexOptions.Compiled);

        /// <summary>Подпапка в workspace, куда сохраняет скриншоты VisionScreenshotStore.</summary>
        private const string ScreenshotsSubfolder = "screenshots";

        private readonly IChatService _chatService;
        private readonly IWorkspaceResolver _workspaceResolver;
        private readonly ILogger<ChatScreenshotsController> _logger;
        private readonly IStringLocalizer<SharedResources> _localizer;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="chatService">Сервис чатов (проверка владения).</param>
        /// <param name="workspaceResolver">Резолвер workspace пользователя.</param>
        /// <param name="logger">Логгер.</param>
        /// <param name="localizer">Локализатор.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public ChatScreenshotsController(
            IChatService chatService,
            IWorkspaceResolver workspaceResolver,
            ILogger<ChatScreenshotsController> logger,
            IStringLocalizer<SharedResources> localizer)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _workspaceResolver = workspaceResolver
                ?? throw new ArgumentNullException(nameof(workspaceResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <summary>
        /// Возвращает PNG-скриншот из workspace пользователя.
        /// </summary>
        /// <param name="chatId">Идентификатор чата (route).</param>
        /// <param name="taskId">Идентификатор задачи (например, <c>vt_81b46b00</c>).</param>
        /// <param name="fileName">Имя файла (например, <c>step-001.png</c>).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>PNG (image/png) или 404.</returns>
        [HttpGet("{taskId}/{fileName}")]
        public async Task<IActionResult> GetAsync(
            int chatId,
            string taskId,
            string fileName,
            CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(fileName))
                    return NotFound();

                // Ранняя защита от path-traversal: отсекаем «/», «\», «..» и др.
                if (!SafeSegmentRegex.IsMatch(taskId) || !SafeSegmentRegex.IsMatch(fileName))
                {
                    _logger.LogWarning(
                        "ChatScreenshots: недопустимый сегмент пути (taskId='{TaskId}', file='{File}')",
                        taskId, fileName);
                    return NotFound();
                }

                var userId = GetCurrentUserId();

                // Проверка владения чатом — если чат чужой, возвращаем 404 (не палим).
                var chat = await _chatService.GetChatAsync(chatId, userId, cancellationToken);
                if (chat == null) return NotFound();

                var workspaceRoot = await _workspaceResolver
                    .GetWorkspacePathAsync(userId);

                var relativePath = System.IO.Path.Combine(
                    ScreenshotsSubfolder, taskId, fileName);

                // PathHelper — второй барьер (RULES § 1.9).
                if (!PathHelper.TryGetSafeFullPath(
                        relativePath, workspaceRoot, out var fullPath))
                {
                    _logger.LogWarning(
                        "ChatScreenshots: PathHelper отклонил '{Rel}' в '{Root}'",
                        relativePath, workspaceRoot);
                    return NotFound();
                }

                if (!System.IO.File.Exists(fullPath))
                    return NotFound();

                var bytes = await System.IO.File.ReadAllBytesAsync(
                    fullPath, cancellationToken);

                // Скриншоты Vision Agent — всегда PNG (GDI / System.Drawing).
                return File(bytes, "image/png");
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ChatScreenshots: ошибка выдачи PNG (chatId={ChatId}, task={Task}, file={File})",
                    chatId, taskId, fileName);
                return NotFound();
            }
        }

        /// <summary>
        /// Извлекает идентификатор текущего пользователя из claims.
        /// </summary>
        /// <returns>Идентификатор пользователя.</returns>
        /// <exception cref="UnauthorizedAccessException">Если пользователь не аутентифицирован.</exception>
        private int GetCurrentUserId()
        {
            var idRaw = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idRaw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}