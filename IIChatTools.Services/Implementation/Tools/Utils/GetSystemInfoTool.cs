using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.Utils
{
    /// <summary>
    /// Инструмент: информация о системе и среде исполнения.
    /// </summary>
    public class GetSystemInfoTool : ITool
    {
        /// <inheritdoc />
        public string Name => "get_system_info";

        /// <inheritdoc />
        public string Description => "Возвращает информацию о версии ОС, .NET, машине и текущей дате.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => Array.Empty<ToolParameterDescriptor>();

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var info = new
            {
                osDescription = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                frameworkDescription = RuntimeInformation.FrameworkDescription,
                machineName = Environment.MachineName,
                processorCount = Environment.ProcessorCount,
                userInteractive = Environment.UserInteractive,
                currentUtcTime = DateTime.UtcNow,
                currentLocalTime = DateTime.Now,
                workspaceRoot = context.WorkspaceRoot
            };

            return Task.FromResult(ToolResult.Ok(info));
        }
    }
}