using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.BackgroundJobs
{
    public class StartBackgroundJobTool : ITool
    {
        private readonly IBackgroundJobService _jobService;
        private readonly IBackgroundJobQueue _queue;

        public StartBackgroundJobTool(IBackgroundJobService jobService, IBackgroundJobQueue queue)
        {
            _jobService = jobService;
            _queue = queue;
        }

        public string Name => "start_background_job";
        public string Description => "Запускает долгую задачу в фоне (Orchestrator, Vision, Smoke), чтобы не блокировать чат. Возвращает jobId, по которому можно отслеживать статус.";
        public bool RequiresApprovalByDefault => true;

        public IReadOnlyList<ToolParameterDescriptor> Parameters => new List<ToolParameterDescriptor>
        {
            new() { Name = "type", Type = "string", Description = "Тип задачи: Orchestrator, Vision, Smoke", Required = true },
            new() { Name = "parameters", Type = "object", Description = "Параметры задачи (JSON-объект)", Required = true }
        };

        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var type = arguments["type"]?.ToString();
            var parameters = arguments["parameters"] as JObject;
            if (string.IsNullOrWhiteSpace(type) || parameters == null)
                return ToolResult.Fail("Параметры 'type' и 'parameters' обязательны.");

            if (type != "Orchestrator" && type != "Vision" && type != "Smoke")
                return ToolResult.Fail($"Неизвестный тип задачи: '{type}'. Допустимые: Orchestrator, Vision, Smoke.");

            var job = await _jobService.CreateAsync(context.UserId, context.ChatId, type, parameters.ToString(Newtonsoft.Json.Formatting.None));
            
            if (!_queue.TryEnqueue(job.Id))
            {
                await _jobService.UpdateStatusAsync(job.Id, "Failed", 0, "Очередь переполнена", error: "Очередь переполнена");
                return ToolResult.Fail("Очередь переполнена. Попробуйте позже.");
            }

            return ToolResult.Ok(
                new { jobId = job.Id, type = job.Type, status = job.Status },
                $"Задача '{type}' запущена в фоне. ID: {job.Id}");
        }
    }
}