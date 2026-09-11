using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент: получение списка файлов и директорий.
    /// </summary>
    public class ListDirectoryTool : ITool
    {
        /// <inheritdoc />
        public string Name => "list_directory";

        /// <inheritdoc />
        public string Description => "Возвращает список файлов и подкаталогов внутри рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "path",
                Type = "string",
                Description = "Относительный путь к каталогу (по умолчанию корень рабочего пространства).",
                Required = false,
                Default = "."
            }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path", ".");

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь или выход за пределы рабочего пространства"));

            if (!Directory.Exists(safePath))
                return Task.FromResult(ToolResult.Fail($"Каталог не найден: {userPath}"));

            var items = new List<object>();

            foreach (var dir in Directory.GetDirectories(safePath).OrderBy(d => d))
            {
                var info = new DirectoryInfo(dir);
                items.Add(new
                {
                    name = info.Name,
                    type = "directory",
                    relativePath = PathHelper.ToRelative(info.FullName, context.WorkspaceRoot)
                });
            }

            foreach (var file in Directory.GetFiles(safePath).OrderBy(f => f))
            {
                var info = new FileInfo(file);
                items.Add(new
                {
                    name = info.Name,
                    type = "file",
                    sizeBytes = info.Length,
                    lastModifiedUtc = info.LastWriteTimeUtc,
                    relativePath = PathHelper.ToRelative(info.FullName, context.WorkspaceRoot)
                });
            }

            return Task.FromResult(ToolResult.Ok(new
            {
                path = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                count = items.Count,
                items
            }));
        }
    }
}