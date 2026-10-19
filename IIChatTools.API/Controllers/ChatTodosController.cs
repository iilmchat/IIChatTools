using System;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.BackgroundJobs;
using IIChatTools.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIChatTools.API.Controllers
{
    /// <summary>
    /// REST для плана задач чата (v2.1.0, Фаза 5).
    ///
    /// <para>
    /// GET    /api/chats/{chatId}/todos
    /// POST   /api/chats/{chatId}/todos
    /// PATCH  /api/chats/{chatId}/todos/{id}
    /// DELETE /api/chats/{chatId}/todos/{id}
    /// </para>
    /// </summary>
    [ApiController]
    [Route("api/chats/{chatId:int}/todos")]
    [Authorize]
    public class ChatTodosController : ControllerBase
    {
        private readonly ITodoService _todos;

        /// <summary>
        /// Создаёт контроллер.
        /// </summary>
        /// <param name="todos">Сервис плана задач.</param>
        public ChatTodosController(ITodoService todos)
        {
            _todos = todos ?? throw new ArgumentNullException(nameof(todos));
        }

        /// <summary>
        /// Список пунктов плана чата.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        [HttpGet]
        public async Task<IActionResult> Get(int chatId)
        {
            try
            {
                var items = await _todos.GetForChatAsync(chatId, GetCurrentUserId());
                return Ok(new
                {
                    success = true,
                    data = items.Select(ChatTodoDto.FromEntity).ToList()
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Создаёт пункт плана.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="request">Заголовок + описание.</param>
        [HttpPost]
        public async Task<IActionResult> Create(int chatId, [FromBody] CreateTodoRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
                return BadRequest(new { success = false, message = "Название задачи обязательно." });

            try
            {
                var todo = await _todos.CreateAsync(
                    chatId, GetCurrentUserId(), request.Title, request.Description);

                return Ok(new
                {
                    success = true,
                    data = ChatTodoDto.FromEntity(todo),
                    message = "Задача добавлена."
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Меняет статус пункта.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="id">Идентификатор пункта.</param>
        /// <param name="request">Новый статус.</param>
        [HttpPatch("{id:int}")]
        public async Task<IActionResult> UpdateStatus(
            int chatId, int id, [FromBody] UpdateTodoStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
                return BadRequest(new { success = false, message = "Статус обязателен." });

            try
            {
                // Проверяем, что todo принадлежит именно этому чату/пользователю.
                var before = await _todos.GetForChatAsync(chatId, GetCurrentUserId());
                if (before.All(t => t.Id != id))
                    return NotFound(new { success = false, message = "Задача не найдена." });

                await _todos.UpdateStatusAsync(id, GetCurrentUserId(), request.Status);

                var after = await _todos.GetForChatAsync(chatId, GetCurrentUserId());
                var updated = after.FirstOrDefault(t => t.Id == id);

                return Ok(new
                {
                    success = true,
                    data = updated == null ? null : ChatTodoDto.FromEntity(updated),
                    message = "Статус обновлён."
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Удаляет пункт.
        /// </summary>
        /// <param name="chatId">Идентификатор чата.</param>
        /// <param name="id">Идентификатор пункта.</param>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int chatId, int id)
        {
            try
            {
                var before = await _todos.GetForChatAsync(chatId, GetCurrentUserId());
                if (before.All(t => t.Id != id))
                    return NotFound(new { success = false, message = "Задача не найдена." });

                await _todos.DeleteAsync(id, GetCurrentUserId());
                return Ok(new { success = true, message = "Задача удалена." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        private int GetCurrentUserId()
        {
            var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(raw, out var id))
                throw new UnauthorizedAccessException("Пользователь не аутентифицирован");
            return id;
        }
    }
}
