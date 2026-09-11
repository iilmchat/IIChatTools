using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент: удаление файлов по регулярному выражению в имени.
    /// </summary>
    public class DeleteFilesByPatternTool : ITool
    {
        /// <inheritdoc />
        public string Name => "delete_files_by_pattern";

        /// <inheritdoc />
        public string Description => "Удаляет файлы, имя которых соответствует регулярному выражению (только внутри указанной директории).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => true;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "path", Type = "string", Description = "Каталог поиска (относительный).", Required = false, Default = "." },
            new ToolParameterDescriptor { Name = "pattern", Type = "string", Description = "Регулярное выражение для имени файла.", Required = true },
            new ToolParameterDescriptor { Name = "recursive", Type = "bool", Description = "Искать во вложенных каталогах.", Required = false, Default = false }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var userPath = arguments.GetString("path", ".");
            var pattern = arguments.GetString("pattern");
            var recursive = arguments.GetBool("recursive");

            if (string.IsNullOrWhiteSpace(pattern))
                return Task.FromResult(ToolResult.Fail("Не указан шаблон"));

            Regex regex;
            try { regex = new Regex(pattern, RegexOptions.Compiled); }
            catch (Exception ex) { return Task.FromResult(ToolResult.Fail($"Некорректное регулярное выражение: {ex.Message}")); }

            if (!PathHelper.TryGetSafeFullPath(userPath, context.WorkspaceRoot, out var safePath))
                return Task.FromResult(ToolResult.Fail("Недопустимый путь"));

            if (!Directory.Exists(safePath))
                return Task.FromResult(ToolResult.Fail($"Каталог не найден: {userPath}"));

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var deleted = new List<string>();

            foreach (var file in Directory.GetFiles(safePath, "*", option))
            {
                if (!regex.IsMatch(Path.GetFileName(file)))
                    continue;

                try
                {
                    File.Delete(file);
                    deleted.Add(PathHelper.ToRelative(file, context.WorkspaceRoot));
                }
                catch
                {
                    // пропускаем файлы, которые не удалось удалить
                }
            }

            return Task.FromResult(ToolResult.Ok(new { deletedCount = deleted.Count, deleted }));
        }
    }
}