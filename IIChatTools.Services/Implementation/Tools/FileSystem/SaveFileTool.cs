using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент: сохранение текстового файла.
    /// Поддерживает режимы: перезапись, добавление.
    /// </summary>
    public class SaveFileTool : ITool
    {
        private readonly long _maxFileSizeBytes;

        /// <summary>
        /// Создаёт инструмент сохранения с лимитом размера.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        public SaveFileTool(IConfiguration configuration)
        {
            var raw = configuration?["Workspace:MaxFileSizeBytes"];
            _maxFileSizeBytes = long.TryParse(raw, out var v) && v > 0 ? v : 10 * 1024 * 1024;
        }

        /// <inheritdoc />
        public string Name => "save_file";

        /// <inheritdoc />
        public string Description => "Сохраняет текстовый файл в рабочем пространстве. Режим 'overwrite' (по умолчанию) или 'append'.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Относительный путь к файлу.", Required = true },
            new ToolParameterDescriptor { Name = "content", Type = "string", Description = "Содержимое файла.", Required = true },
            new ToolParameterDescriptor { Name = "mode", Type = "string", Description = "Режим: 'overwrite' или 'append'.", Required = false, Default = "overwrite" }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            var content = arguments.GetString("content", string.Empty);
            var mode = arguments.GetString("mode", "overwrite").ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(userPath))
                return ToolResult.Fail("Не указан путь к файлу");

            if (System.Text.Encoding.UTF8.GetByteCount(content) > _maxFileSizeBytes)
                return ToolResult.Fail($"Размер содержимого превышает лимит ({_maxFileSizeBytes} байт)");

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return ToolResult.Fail("Недопустимый путь");

            var dir = Path.GetDirectoryName(safePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (mode == "append")
                await File.AppendAllTextAsync(safePath, content);
            else
                await File.WriteAllTextAsync(safePath, content);

            var info = new FileInfo(safePath);

            return ToolResult.Ok(new
            {
                saved = true,
                relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                sizeBytes = info.Length,
                mode
            });
        }
    }
}