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
    /// Инструмент: чтение текстового файла с ограничением размера.
    /// </summary>
    public class ReadFileTool : ITool
    {
        private readonly long _maxFileSizeBytes;

        /// <summary>
        /// Создаёт инструмент чтения с лимитом размера.
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        public ReadFileTool(IConfiguration configuration)
        {
            var raw = configuration?["Workspace:MaxFileSizeBytes"];
            _maxFileSizeBytes = long.TryParse(raw, out var v) && v > 0 ? v : 10 * 1024 * 1024;
        }

        /// <inheritdoc />
        public string Name => "read_file";

        /// <inheritdoc />
        public string Description => "Читает содержимое текстового файла из рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "path",
                Type = "string",
                Description = "Относительный путь к файлу.",
                Required = true
            }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            if (string.IsNullOrWhiteSpace(userPath))
                return ToolResult.Fail("Не указан путь к файлу");

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return ToolResult.Fail("Недопустимый путь");

            if (!File.Exists(safePath))
                return ToolResult.Fail($"Файл не найден: {userPath}");

            var info = new FileInfo(safePath);
            if (info.Length > _maxFileSizeBytes)
                return ToolResult.Fail($"Размер файла превышает лимит ({_maxFileSizeBytes} байт)");

            var content = await File.ReadAllTextAsync(safePath);

            return ToolResult.Ok(new
            {
                relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                sizeBytes = info.Length,
                content
            });
        }
    }
}