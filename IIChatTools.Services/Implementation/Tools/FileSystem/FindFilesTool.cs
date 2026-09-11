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
    /// Инструмент: поиск файлов по имени и расширению.
    /// </summary>
    public class FindFilesTool : ITool
    {
        private const int MaxResults = 500;

        /// <inheritdoc />
        public string Name => "find_files";

        /// <inheritdoc />
        public string Description => "Ищет файлы по части имени и/или расширению внутри рабочего пространства.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Каталог поиска (относительный).", Required = false, Default = "." },
            new ToolParameterDescriptor { Name = "nameContains", Type = "string", Description = "Подстрока имени файла.", Required = false },
            new ToolParameterDescriptor { Name = "extension", Type = "string", Description = "Расширение (например, .cs).", Required = false },
            new ToolParameterDescriptor { Name = "recursive", Type = "bool", Description = "Искать во вложенных каталогах.", Required = false, Default = true }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path", ".");
            var nameContains = arguments.GetString("nameContains");
            var extension = arguments.GetString("extension");
            var recursive = arguments.GetBool("recursive", true);

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            if (!Directory.Exists(safePath))
                return Task.FromResult(ToolResult.Fail($"Каталог не найден: {userPath}"));

            if (!string.IsNullOrEmpty(extension) && !extension.StartsWith("."))
                extension = "." + extension;

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var results = new List<object>();

            foreach (var file in Directory.EnumerateFiles(safePath, "*", option))
            {
                var name = Path.GetFileName(file);

                if (!string.IsNullOrEmpty(nameContains) &&
                    name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!string.IsNullOrEmpty(extension) &&
                    !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = new FileInfo(file);
                results.Add(new
                {
                    relativePath = PathHelper.ToRelative(file, context.WorkspaceRoot),
                    name,
                    sizeBytes = info.Length,
                    lastModifiedUtc = info.LastWriteTimeUtc
                });

                if (results.Count >= MaxResults)
                    break;
            }

            return Task.FromResult(ToolResult.Ok(new
            {
                count = results.Count,
                truncated = results.Count >= MaxResults,
                files = results
            }));
        }
    }
}