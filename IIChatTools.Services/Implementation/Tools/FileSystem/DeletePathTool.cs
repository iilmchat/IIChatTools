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
    /// Инструмент: удаление файла или каталога (рекурсивно).
    /// </summary>
    public class DeletePathTool : ITool
    {
        /// <inheritdoc />
        public string Name => "delete_path";

        /// <inheritdoc />
        public string Description => "Удаляет файл или каталог (рекурсивно) внутри рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Относительный путь к файлу или каталогу.", Required = true }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            if (string.IsNullOrWhiteSpace(userPath))
                return Task.FromResult(ToolResult.Fail("Не указан путь"));

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            // Защита от удаления самого корня workspace
            var root = Path.GetFullPath(context.WorkspaceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (safePath.Equals(root, System.StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(ToolResult.Fail("Удаление корня рабочего пространства запрещено"));

            if (File.Exists(safePath))
            {
                File.Delete(safePath);
                return Task.FromResult(ToolResult.Ok(new { deleted = true, type = "file", relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot) }));
            }

            if (Directory.Exists(safePath))
            {
                Directory.Delete(safePath, recursive: true);
                return Task.FromResult(ToolResult.Ok(new { deleted = true, type = "directory", relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot) }));
            }

            return Task.FromResult(ToolResult.Fail($"Путь не найден: {userPath}"));
        }
    }
}