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
    /// Инструмент: получение метаданных файла или каталога.
    /// </summary>
    public class GetFileMetadataTool : ITool
    {
        /// <inheritdoc />
        public string Name => "get_file_metadata";

        /// <inheritdoc />
        public string Description => "Возвращает метаданные файла или каталога (размер, даты создания/изменения, тип).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Относительный путь.", Required = true }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            if (string.IsNullOrWhiteSpace(userPath))
                return Task.FromResult(ToolResult.Fail("Не указан путь"));

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            if (File.Exists(safePath))
            {
                var info = new FileInfo(safePath);
                return Task.FromResult(ToolResult.Ok(new
                {
                    type = "file",
                    name = info.Name,
                    relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                    sizeBytes = info.Length,
                    createdAtUtc = info.CreationTimeUtc,
                    lastModifiedUtc = info.LastWriteTimeUtc,
                    lastAccessedUtc = info.LastAccessTimeUtc,
                    extension = info.Extension
                }));
            }

            if (Directory.Exists(safePath))
            {
                var info = new DirectoryInfo(safePath);
                return Task.FromResult(ToolResult.Ok(new
                {
                    type = "directory",
                    name = info.Name,
                    relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                    createdAtUtc = info.CreationTimeUtc,
                    lastModifiedUtc = info.LastWriteTimeUtc
                }));
            }

            return Task.FromResult(ToolResult.Fail($"Путь не найден: {userPath}"));
        }
    }
}