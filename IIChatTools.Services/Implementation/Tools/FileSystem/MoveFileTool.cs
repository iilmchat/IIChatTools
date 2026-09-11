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
    /// Инструмент: перемещение файла или каталога.
    /// </summary>
    public class MoveFileTool : ITool
    {
        /// <inheritdoc />
        public string Name => "move_file";

        /// <inheritdoc />
        public string Description => "Перемещает файл или каталог внутри рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "source", Type = "string", Description = "Исходный путь (относительный).", Required = true },
            new ToolParameterDescriptor { Name = "destination", Type = "string", Description = "Целевой путь (относительный).", Required = true },
            new ToolParameterDescriptor { Name = "overwrite", Type = "bool", Description = "Перезаписывать существующий файл.", Required = false, Default = false }
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

            if (!File.Exists(safeSource) && !Directory.Exists(safeSource))
                return Task.FromResult(ToolResult.Fail($"Источник не найден: {source}"));

            try
            {
                if (File.Exists(safeSource))
                {
                    var dir = Path.GetDirectoryName(safeDest);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.Move(safeSource, safeDest, overwrite);
                }
                else
                {
                    Directory.Move(safeSource, safeDest);
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"Ошибка перемещения: {ex.Message}"));
            }

            return Task.FromResult(ToolResult.Ok(new
            {
                moved = true,
                source = PathHelper.ToRelative(safeSource, context.WorkspaceRoot),
                destination = PathHelper.ToRelative(safeDest, context.WorkspaceRoot)
            }));
        }
    }
}