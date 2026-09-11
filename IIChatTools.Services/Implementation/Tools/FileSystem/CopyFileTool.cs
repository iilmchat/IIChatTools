using System;
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
    /// Инструмент: копирование файла.
    /// </summary>
    public class CopyFileTool : ITool
    {
        /// <inheritdoc />
        public string Name => "copy_file";

        /// <inheritdoc />
        public string Description => "Копирует файл внутри рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "source", Type = "string", Description = "Исходный файл (относительный).", Required = true },
            new ToolParameterDescriptor { Name = "destination", Type = "string", Description = "Целевой путь (относительный).", Required = true },
            new ToolParameterDescriptor { Name = "overwrite", Type = "bool", Description = "Перезаписывать существующий.", Required = false, Default = false }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var source = arguments.GetString("source");
            var destination = arguments.GetString("destination");
            var overwrite = arguments.GetBool("overwrite");

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                return Task.FromResult(ToolResult.Fail("Не указаны исходный и целевой пути"));

            if (!PathHelper.TryGetSafeFullPath(source, context.WorkspaceRoot, out var safeSource) ||
                !PathHelper.TryGetSafeFullPath(destination, context.WorkspaceRoot, out var safeDest))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            if (!File.Exists(safeSource))
                return Task.FromResult(ToolResult.Fail($"Файл-источник не найден: {source}"));

            try
            {
                var dir = Path.GetDirectoryName(safeDest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(safeSource, safeDest, overwrite);
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"Ошибка копирования: {ex.Message}"));
            }

            return Task.FromResult(ToolResult.Ok(new
            {
                copied = true,
                source = PathHelper.ToRelative(safeSource, context.WorkspaceRoot),
                destination = PathHelper.ToRelative(safeDest, context.WorkspaceRoot)
            }));
        }
    }
}