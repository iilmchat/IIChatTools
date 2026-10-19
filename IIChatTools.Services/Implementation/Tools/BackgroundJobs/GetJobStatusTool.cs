using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.BackgroundJobs
{
    public class GetJobStatusTool : ITool
    {
        private readonly IBackgroundJobService _jobService;

        public GetJobStatusTool(IBackgroundJobService jobService)
        {
            _jobService = jobService;
        }

        public string Name => "get_job_status";
        public string Description => "Возвращает статус и результат фоновой задачи по её jobId.";
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

            return ToolResult.Ok(new
            {
                jobId = job.Id,
                type = job.Type,
                status = job.Status,
                progress = job.Progress,
                message = job.ProgressMessage,
                result = job.ResultJson,
                error = job.Error
            }, $"Статус: {job.Status} ({job.Progress}%)");
        }
    }
}