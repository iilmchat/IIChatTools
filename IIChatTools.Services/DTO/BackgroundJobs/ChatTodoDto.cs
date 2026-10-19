using System;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.DTO.BackgroundJobs
{
    /// <summary>
    /// DTO пункта плана (Todo) для REST/UI (v2.1.0, Фаза 5).
    /// </summary>
    public sealed class ChatTodoDto
    {
        /// <summary>Идентификатор.</summary>
        public int Id { get; set; }

        /// <summary>Чат.</summary>
        public int ChatId { get; set; }

        /// <summary>Заголовок.</summary>
        public string Title { get; set; }

        /// <summary>Описание (может быть null).</summary>
        public string Description { get; set; }

        /// <summary>Pending / InProgress / Completed.</summary>
        public string Status { get; set; }

        /// <summary>Порядок в плане (0-based).</summary>
        public int Order { get; set; }

        /// <summary>Создан.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Завершён (только Completed).</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Маппинг entity → DTO.
        /// </summary>
        public static ChatTodoDto FromEntity(ChatTodo todo)
        {
            if (todo == null) return null;

            return new ChatTodoDto
            {
                Id = todo.Id,
                ChatId = todo.ChatId,
                Title = todo.Title,
                Description = todo.Description,
                Status = todo.Status,
                Order = todo.SortOrder,
                CreatedAt = todo.CreatedAt,
                CompletedAt = todo.CompletedAt
            };
        }
    }

    /// <summary>Запрос на создание пункта плана.</summary>
    public sealed class CreateTodoRequest
    {
        /// <summary>Заголовок (1..200).</summary>
        public string Title { get; set; }

        /// <summary>Описание (≤ 2000).</summary>
        public string Description { get; set; }
    }

    /// <summary>Запрос на смену статуса пункта плана.</summary>
    public sealed class UpdateTodoStatusRequest
    {
        /// <summary>Pending / InProgress / Completed.</summary>
        public string Status { get; set; }
    }
}
