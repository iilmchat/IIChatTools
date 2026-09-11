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
    /// Инструмент: проверка и нормализация пути внутри рабочего пространства.
    /// В stateless-режиме не меняет состояние, но возвращает абсолютный путь и признак существования.
    /// </summary>
    public class ChangeDirectoryTool : ITool
    {
        /// <inheritdoc />
        public string Name => "change_directory";

        /// <inheritdoc />
        public string Description => "Проверяет существование каталога внутри рабочего пространства и возвращает его абсолютный путь.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor
            {
                Name = "path",
                Type = "string",
                Description = "Относительный путь к каталогу.",
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

            if (!Directory.Exists(safePath))
                return Task.FromResult(ToolResult.Fail($"Каталог не найден: {userPath}"));

            return Task.FromResult(ToolResult.Ok(new
            {
                absolutePath = safePath,
                relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                exists = true
            }));
        }
    }
}