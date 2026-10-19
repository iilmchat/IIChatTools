using System;
using IIChatTools.Data.Entities;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.DTO.BackgroundJobs
{
    /// <summary>
    /// DTO фоновой задачи для REST/UI (v2.1.0, Фаза 4).
    /// </summary>
    public sealed class BackgroundJobDto
    {
        /// <summary>Идентификатор задачи.</summary>
        public int Id { get; set; }

        /// <summary>Чат, из которого запущена задача (nullable).</summary>
        public int? ChatId { get; set; }

        /// <summary>Orchestrator / Vision / Smoke.</summary>
        public string Type { get; set; }

        /// <summary>Короткое название для карточки (task из parameters или type).</summary>
        public string Title { get; set; }

        /// <summary>Pending / Running / Completed / Failed / Cancelled.</summary>
        public string Status { get; set; }

        /// <summary>0…100.</summary>
        public int Progress { get; set; }

        /// <summary>Текущий шаг (ProgressMessage).</summary>
        public string Message { get; set; }

        /// <summary>Время создания.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Время старта.</summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>Время завершения.</summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>Текст ошибки (для Failed).</summary>
        public string Error { get; set; }

        /// <summary>Результат (только в detail-ответе; в списке — null).</summary>
        public string Result { get; set; }

        /// <summary>
        /// Маппинг entity → DTO.
        /// </summary>
        /// <param name="job">Задача.</param>
        /// <param name="includeResult">Включать ли полный ResultJson.</param>
        /// <returns>DTO.</returns>
        public static BackgroundJobDto FromEntity(BackgroundJob job, bool includeResult = false)
        {
            if (job == null) return null;

            return new BackgroundJobDto
            {
                Id = job.Id,
                ChatId = job.ChatId,
                Type = job.Type,
                Title = ExtractTitle(job),
                Status = job.Status,
                Progress = job.Progress,
                Message = job.ProgressMessage,
                CreatedAt = job.CreatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt,
                Error = job.Error,
                Result = includeResult ? job.ResultJson : null
            };
        }

        private static string ExtractTitle(BackgroundJob job)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(job.ParametersJson))
                {
                    var parameters = JObject.Parse(job.ParametersJson);
                    var task = parameters["task"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(task))
                    {
                        return task.Length > 120 ? task.Substring(0, 120) + "…" : task;
                    }
                }
            }
            catch
            {
                // Битый JSON не должен ломать список задач.
            }

            return job.Type ?? "Job";
        }
    }
}
