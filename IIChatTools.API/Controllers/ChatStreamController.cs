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
using Newtonsoft.Json.Serialization;

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
        private readonly IChatApprovalCoordinator _approvalCoordinator;
        private readonly ILogger<ChatStreamController> _logger;

        /// <summary>
        /// Создаёт экземпляр контроллера.
        /// </summary>
        /// <param name="chatStreamService">Сервис стриминга чата</param>
        /// <param name="approvalCoordinator">Координатор подтверждений (Singleton)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public ChatStreamController(
            IChatStreamService chatStreamService,
            IChatApprovalCoordinator approvalCoordinator,
            ILogger<ChatStreamController> logger)
        {
            _chatStreamService = chatStreamService ?? throw new ArgumentNullException(nameof(chatStreamService));
            _approvalCoordinator = approvalCoordinator ?? throw new ArgumentNullException(nameof(approvalCoordinator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Стримит ответ LLM на сообщение пользователя через SSE.
        /// </summary>
        /// <param name="request">Запрос (ChatId + Message + UseTools)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>SSE-поток событий</returns>
        [HttpPost("stream")]
        public Task StreamAsync(
            [FromBody] ChatStreamRequest request,
            CancellationToken cancellationToken)
        {
            return StreamInternalAsync(request, cancellationToken);
        }

        /// <summary>
        /// Перегенерирует последний ответ ассистента в чате (Фаза 2.1.2).
        /// Удаляет последний assistant-exchange (assistant + tool) и заново стримит ответ
        /// на последнее user-сообщение. Ожидает <c>{ chatId }</c>; поле Message игнорируется.
        /// </summary>
        /// <param name="request">Запрос (только ChatId)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>SSE-поток событий (как в StreamAsync)</returns>
        [HttpPost("regenerate")]
        public Task RegenerateAsync(
            [FromBody] ChatStreamRequest request,
            CancellationToken cancellationToken)
        {
            if (request != null)
            {
                request.Regenerate = true;
                request.UseTools = true;   // Regenerate всегда с tools
            }
            return StreamInternalAsync(request, cancellationToken);
        }

        /// <summary>
        /// Общая логика SSE-стриминга для <c>stream</c> и <c>regenerate</c>.
        /// Настраивает SSE-заголовки, пробрасывает события из <see cref="IChatStreamService"/>.
        /// </summary>
        /// <param name="request">Запрос</param>
        /// <param name="cancellationToken">Токен отмены</param>
        private async Task StreamInternalAsync(
            ChatStreamRequest request,
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
                "SSE-стрим начат: chatId={ChatId}, userId={UserId}, regenerate={Regen}, msgLen={MsgLen}",
                request.ChatId, userId, request.Regenerate, request.Message?.Length ?? 0);

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
        /// Настройки JSON-сериализации для SSE: camelCase, без BOM.
        /// Единый стиль для всех типов событий (start/delta/done/tool_call/...).
        /// </summary>
        private static readonly JsonSerializerSettings SseJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

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

            var json = JsonConvert.SerializeObject(evt.Data ?? new { }, SseJsonSettings);
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


        // ============================================================
        // Approvals (Фаза 1.7) — REST для resolve tool-call decisions
        // ============================================================

        /// <summary>
        /// Подтверждает вызов инструмента, ожидающего approval в чате.
        /// </summary>
        /// <param name="callId">Идентификатор вызова (от LM Studio)</param>
        /// <returns>JSON { success, data: { resolved } }</returns>
        [HttpPost("approvals/{callId}/approve")]
        public async Task<IActionResult> ApproveAsync(string callId)
        {
            return await ResolveApprovalAsync(callId, ChatApprovalDecision.Approved);
        }

        /// <summary>
        /// Отклоняет вызов инструмента, ожидающего approval в чате.
        /// </summary>
        /// <param name="callId">Идентификатор вызова (от LM Studio)</param>
        /// <returns>JSON { success, data: { resolved } }</returns>
        [HttpPost("approvals/{callId}/reject")]
        public async Task<IActionResult> RejectAsync(string callId)
        {
            return await ResolveApprovalAsync(callId, ChatApprovalDecision.Rejected);
        }

        /// <summary>
        /// Общий метод approve/reject: находит ожидающего, будит его.
        /// </summary>
        /// <param name="callId">Идентификатор вызова</param>
        /// <param name="decision">Решение</param>
        /// <returns>JSON { success, data: { resolved } }</returns>
        private async Task<IActionResult> ResolveApprovalAsync(string callId, ChatApprovalDecision decision)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(callId))
                {
                    return Ok(new { success = false, message = "Некорректный идентификатор вызова." });
                }

                var userId = GetCurrentUserId();

                _logger.LogInformation(
                    "Approval resolve: callId={CallId}, decision={Decision}, userId={UserId}",
                    callId, decision, userId);

                var resolved = await _approvalCoordinator.ResolveAsync(callId, decision);

                if (!resolved)
                {
                    _logger.LogWarning(
                        "Approval resolve: callId={CallId} не найден (таймаут или неверный id)",
                        callId);
                    return Ok(new
                    {
                        success = false,
                        message = "Запрос уже неактуален (таймаут истёк или неверный id)."
                    });
                }

                return Ok(new { success = true, data = new { resolved = true } });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка resolve approval callId={CallId}", callId);
                return Ok(new { success = false, message = "Внутренняя ошибка сервера." });
            }
        }        
    }
}