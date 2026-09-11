using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент: создание каталога (включая вложенные).
    /// </summary>
    public class MakeDirectoryTool : ITool
    {
        /// <inheritdoc />
        public string Name => "make_directory";

        /// <inheritdoc />
        public string Description => "Создаёт каталог внутри рабочего пространства (включая промежуточные).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "path",
                Type = "string",
                Description = "Относительный путь к создаваемому каталогу.",
                Required = true
            }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            if (string.IsNullOrWhiteSpace(userPath))
                return Task.FromResult(ToolResult.Fail("Не указан путь"));

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            Directory.CreateDirectory(safePath);

            return Task.FromResult(ToolResult.Ok(new
            {
                created = true,
                relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot)
            }));
        }
    }
}