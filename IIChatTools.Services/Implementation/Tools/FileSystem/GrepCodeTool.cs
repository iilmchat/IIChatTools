using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент для поиска по содержимому файлов (аналог grep).
    /// </summary>
    public class GrepCodeTool : ITool
    {
        private readonly IWorkspaceResolver _workspaceResolver;

        public GrepCodeTool(IWorkspaceResolver workspaceResolver)
        {
            _workspaceResolver = workspaceResolver ?? throw new ArgumentNullException(nameof(workspaceResolver));
        }

        public string Name => "grep_code";
        public string Description => "Ищет текст или регулярное выражение внутри файлов workspace. Возвращает совпадения с контекстом. Используй вместо перебора read_file для поиска по коду.";
        public bool RequiresApprovalByDefault => false;

        public IReadOnlyList<ToolParameterDescriptor> Parameters => new List<ToolParameterDescriptor>
        {
            new() { Name = "pattern", Type = "string", Description = "Строка или regex для поиска", Required = true },
            new() { Name = "path", Type = "string", Description = "Относительный путь от workspace (по умолчанию '.')", Required = false },
            new() { Name = "include", Type = "array", Description = "Маски файлов (например, ['*.cs', '*.md'])", Required = false },
            new() { Name = "exclude", Type = "array", Description = "Исключения (например, ['bin/**', 'obj/**'])", Required = false },
            new() { Name = "contextLines", Type = "integer", Description = "Строк контекста до/после (0-5)", Required = false },
            new() { Name = "maxResults", Type = "integer", Description = "Максимум результатов (по умолчанию 50, макс 200)", Required = false },
            new() { Name = "caseSensitive", Type = "boolean", Description = "Учитывать регистр (по умолчанию false)", Required = false },
            new() { Name = "regex", Type = "boolean", Description = "Использовать регулярное выражение (по умолчанию false)", Required = false }
        };

        public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            try
            {
                var pattern = arguments["pattern"]?.ToString();
                if (string.IsNullOrWhiteSpace(pattern))
                    return ToolResult.Fail("Параметр 'pattern' обязателен.");

                var relativePath = arguments["path"]?.ToString() ?? ".";
                var include = arguments["include"]?.ToObject<string[]>() ?? Array.Empty<string>();
                var exclude = arguments["exclude"]?.ToObject<string[]>() ?? new[] { "bin/**", "obj/**", ".git/**", "node_modules/**" };
                var contextLines = Math.Clamp(arguments["contextLines"]?.Value<int>() ?? 0, 0, 5);
                var maxResults = Math.Clamp(arguments["maxResults"]?.Value<int>() ?? 50, 1, 200);
                var caseSensitive = arguments["caseSensitive"]?.Value<bool>() ?? false;
                var useRegex = arguments["regex"]?.Value<bool>() ?? false;

                // 1. Безопасное разрешение пути
                var workspaceRoot = await _workspaceResolver.GetWorkspacePathAsync(context.UserId);
                if (!PathHelper.TryGetSafeFullPath(relativePath, workspaceRoot, out var fullPath))
                    return ToolResult.Fail($"Недопустимый путь: {relativePath}");

                if (!Directory.Exists(fullPath))
                    return ToolResult.Fail($"Директория не найдена: {relativePath}");

                // 2. Подготовка regex
                var regexOptions = RegexOptions.Compiled;
                if (!caseSensitive) regexOptions |= RegexOptions.IgnoreCase;

                Regex regex;
                try
                {
                    var regexPattern = useRegex ? pattern : Regex.Escape(pattern);
                    regex = new Regex(regexPattern, regexOptions, TimeSpan.FromSeconds(1)); // Защита от ReDoS
                }
                catch (ArgumentException ex)
                {
                    return ToolResult.Fail($"Ошибка в регулярном выражении: {ex.Message}");
                }

                // 3. Сбор файлов
                var searchOption = SearchOption.AllDirectories;
                var files = Directory.EnumerateFiles(fullPath, "*.*", searchOption)
                    .Where(f => !exclude.Any(e => f.Contains(e.Replace("**", ""), StringComparison.OrdinalIgnoreCase)))
                    .Where(f => include.Length == 0 || include.Any(i =>
                    {
                        var mask = i.Replace("*", "");
                        return i.StartsWith("*") ? f.EndsWith(mask, StringComparison.OrdinalIgnoreCase) : f.Contains(mask, StringComparison.OrdinalIgnoreCase);
                    }))
                    .Where(f => new FileInfo(f).Length < 5 * 1024 * 1024) // Skip >5MB
                    .ToList();

                // 4. Поиск
                var results = new List<object>();
                var totalMatches = 0;
                var isTruncated = false;

                foreach (var file in files)
                {
                    if (totalMatches >= maxResults) { isTruncated = true; break; }

                    var lines = await File.ReadAllLinesAsync(file, CancellationToken.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            totalMatches++;
                            if (results.Count >= maxResults) { isTruncated = true; break; }

                            var start = Math.Max(0, i - contextLines);
                            var end = Math.Min(lines.Length - 1, i + contextLines);
                            var snippetLines = lines.Skip(start).Take(end - start + 1).ToArray();

                            results.Add(new
                            {
                                file = Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/'),
                                line = i + 1,
                                snippet = string.Join("\n", snippetLines)
                            });
                        }
                    }
                }

                return ToolResult.Ok(new
                {
                    results,
                    totalMatches,
                    truncated = isTruncated
                }, $"Найдено совпадений: {totalMatches}" + (isTruncated ? " (обрезано)" : ""));
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Ошибка поиска: {ex.Message}");
            }
        }
    }
}
