using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.BackgroundJobs
{
    public class CancelJobTool : ITool
    {
        private readonly IBackgroundJobService _jobService;
        private readonly IJobCancellationRegistry _cancellationRegistry;

        public CancelJobTool(
            IBackgroundJobService jobService,
            IJobCancellationRegistry cancellationRegistry)
        {
            _jobService = jobService;
            _cancellationRegistry = cancellationRegistry;
        }

        public string Name => "cancel_job";
        public string Description => "Отменяет фоновую задачу по её jobId.";
        public bool RequiresApprovalByDefault => false;

        public IReadOnlyList<ToolParameterDescriptor> Parameters => new List<ToolParameterDescriptor>
        {
            new() { Name = "jobId", Type = "integer", Description = "ID задачи", Required = true }
        };

        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var jobId = arguments["jobId"]?.Value<int>();
            if (jobId == null || jobId <= 0)
                return ToolResult.Fail("Параметр 'jobId' обязателен.");

            var job = await _jobService.GetAsync(jobId.Value);
            if (job == null || job.UserId != context.UserId)
                return ToolResult.Fail("Задача не найдена.");

            if (job.Status == "Completed" || job.Status == "Failed" || job.Status == "Cancelled")
                return ToolResult.Fail($"Задача уже завершена (статус: {job.Status}).");

            await _jobService.UpdateStatusAsync(job.Id, "Cancelled", job.Progress, "Отменено пользователем");

            // KI-259: будим runner/tool реальным CancellationToken.
            // Если задача ещё в очереди — CTS отсутствует, но runner
            // увидит статус Cancelled и не запустит её.
            _cancellationRegistry.Cancel(job.Id);

            return ToolResult.Ok(new { jobId = job.Id, status = "Cancelled" }, "Задача отменена.");
        }
    }
}