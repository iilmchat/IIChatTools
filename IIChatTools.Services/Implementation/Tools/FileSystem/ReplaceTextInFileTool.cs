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
    /// Инструмент: точечная замена текста в файле.
    /// </summary>
    public class ReplaceTextInFileTool : ITool
    {
        /// <inheritdoc />
        public string Name => "replace_text_in_file";

        /// <inheritdoc />
        public string Description => "Заменяет все вхождения указанной подстроки в файле на новую строку.";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Относительный путь к файлу.", Required = true },
            new ToolParameterDescriptor { Name = "oldText", Type = "string", Description = "Искомая подстрока.", Required = true },
            new ToolParameterDescriptor { Name = "newText", Type = "string", Description = "Замена.", Required = true }
        };

        /// <inheritdoc />
        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path");
            var oldText = arguments.GetString("oldText");
            var newText = arguments.GetString("newText", string.Empty);

            if (string.IsNullOrWhiteSpace(userPath))
                return ToolResult.Fail("Не указан путь к файлу");
            if (string.IsNullOrEmpty(oldText))
                return ToolResult.Fail("Не указан искомый текст");

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return ToolResult.Fail("Недопустимый путь");

            if (!File.Exists(safePath))
                return ToolResult.Fail($"Файл не найден: {userPath}");

            var content = await File.ReadAllTextAsync(safePath);
            var count = CountOccurrences(content, oldText);

            if (count == 0)
                return ToolResult.Fail("Искомый текст не найден в файле");

            var updated = content.Replace(oldText, newText);
            await File.WriteAllTextAsync(safePath, updated);

            return ToolResult.Ok(new
            {
                relativePath = PathHelper.ToRelative(safePath, context.WorkspaceRoot),
                replacements = count
            });
        }

        private static int CountOccurrences(string source, string needle)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(needle))
                return 0;

            var count = 0;
            var idx = 0;
            while ((idx = source.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}