using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;              // ← добавить
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// API стриминга ответов LLM через Server-Sent Events (SSE).
    /// Возвращает поток событий: <c>start</c> → <c>delta</c>* → <c>done</c> | <c>error</c>.
    /// </summary>
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatStreamController : ControllerBase
    {
        private readonly IChatStreamService _chatStreamService;
        private readonly ILogger<ChatStreamController> _logger;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="chatStreamService">Сервис стриминга чата</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatStreamController(
            IChatStreamService chatStreamService,
            ILogger<ChatStreamController> logger)
        {
            _chatStreamService = chatStreamService ?? throw new ArgumentNullException(nameof(chatStreamService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Стримит ответ LLM на сообщение пользователя через SSE.
        /// </summary>
        /// <param name="request">Запрос (ChatId + Message + UseTools)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>SSE-поток событий</returns>
        [HttpPost("stream")]
        public async Task StreamAsync(
            [FromBody] ChatStreamRequest request,
            CancellationToken cancellationToken)
        {
            // Настраиваем SSE-ответ ДО первой записи
            Response.StatusCode = 200;
            Response.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no"; // отключаем буферизацию в nginx
            Response.Headers["Connection"] = "keep-alive";

            if (request == null)
            {
                await WriteEventAsync(ChatStreamEvent.Error("Пустой запрос"), cancellationToken);
                return;
            }

            var userId = GetCurrentUserId();
            _logger.LogInformation(
                "SSE-стрим начат: chatId={ChatId}, userId={UserId}, msgLen={MsgLen}",
                request.ChatId, userId, request.Message?.Length ?? 0);

            try
            {
                await foreach (var evt in _chatStreamService.StreamAsync(request, userId, cancellationToken))
                {
                    await WriteEventAsync(evt, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SSE-стрим отменён клиентом: chatId={ChatId}", request.ChatId);
                // Клиент отключился — писать уже некуда
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка SSE-стрима: chatId={ChatId}", request.ChatId);
                try
                {
                    await WriteEventAsync(ChatStreamEvent.Error($"Внутренняя ошибка: {ex.Message}"), cancellationToken);
                }
                catch
                {
                    // Если и это не удалось — игнорируем (клиент отключился)
                }
            }

            _logger.LogInformation("SSE-стрим завершён: chatId={ChatId}", request.ChatId);
        }

        /// <summary>
        /// Записывает одно SSE-событие в ответ и делает flush.
        /// Формат: <c>event: {type}\ndata: {json}\n\n</c>.
        /// </summary>
        /// <param name="evt">Событие</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Асинхронная задача</returns>
        private async Task WriteEventAsync(ChatStreamEvent evt, CancellationToken cancellationToken)
        {
            if (evt == null) return;

            var json = JsonConvert.SerializeObject(evt.Data ?? new { }, Formatting.None);
            var payload = $"event: {evt.Type}\ndata: {json}\n\n";

            try
            {
                await Response.WriteAsync(payload, Encoding.UTF8, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Клиент отключился — не логируем как ошибку
                throw;
            }
            catch (ObjectDisposedException)
            {
                // Response закрыт — игнорируем
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