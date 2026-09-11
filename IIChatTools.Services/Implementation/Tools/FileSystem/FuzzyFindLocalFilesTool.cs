using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IIChatTools.Services.DTO;
using IIChatTools.Services.Extensions;
using IIChatTools.Services.Interfaces;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Tools.FileSystem
{
    /// <summary>
    /// Инструмент: нечёткий поиск файлов по имени (расстояние Левенштейна).
    /// </summary>
    public class FuzzyFindLocalFilesTool : ITool
    {
        private const int MaxResults = 50;

        /// <inheritdoc />
        public string Name => "fuzzy_find_local_files";

        /// <inheritdoc />
        public string Description => "Нечёткий поиск файлов по имени внутри рабочего пространства (по расстоянию Левенштейна).";

        /// <inheritdoc />
        public bool RequiresApprovalByDefault => false;

        /// <inheritdoc />
        public IReadOnlyList<ToolParameterDescriptor> Parameters => new[]
        {
            new ToolParameterDescriptor { Name = "query", Type = "string", Description = "Строка поиска (частичное имя).", Required = true },
            new ToolParameterDescriptor { Name = "maxDistance", Type = "integer", Description = "Максимальное расстояние Левенштейна.", Required = false, Default = 3 },
            new ToolParameterDescriptor { Name = "limit", Type = "integer", Description = "Ограничение числа результатов.", Required = false, Default = 20 }
        };

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, JObject arguments)
        {
            var query = arguments.GetString("query");
            var maxDistance = arguments.GetInt("maxDistance", 3);
            var limit = arguments.GetInt("limit", 20);

            if (string.IsNullOrWhiteSpace(query))
                return Task.FromResult(ToolResult.Fail("Не указана строка поиска"));

            if (maxDistance < 0) maxDistance = 0;
            if (limit <= 0 || limit > MaxResults) limit = MaxResults;

            var queryNorm = query.Trim().ToLowerInvariant();

            var candidates = new List<(string fullPath, int distance)>();

            foreach (var file in Directory.EnumerateFiles(context.WorkspaceRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                var nameNorm = name.ToLowerInvariant();

                var distance = LevenshteinDistance(queryNorm, nameNorm);

                // Также учитываем вариант "подстрока"
                var substringScore = nameNorm.Contains(queryNorm) ? distance / 2 : distance;

                if (substringScore <= maxDistance)
                    candidates.Add((file, substringScore));
            }

            var top = candidates
                .OrderBy(c => c.distance)
                .ThenBy(c => c.fullPath)
                .Take(limit)
                .Select(c =>
                {
                    var info = new FileInfo(c.fullPath);
                    return new
                    {
                        relativePath = PathHelper.ToRelative(c.fullPath, context.WorkspaceRoot),
                        name = info.Name,
                        sizeBytes = info.Length,
                        distance = c.distance
                    };
                })
                .ToList();

            return Task.FromResult(ToolResult.Ok(new { count = top.Count, files = top }));
        }

        /// <summary>
        /// Вычисляет расстояние Левенштейна между двумя строками.
        /// </summary>
        /// <param name="s">Первая строка</param>
        /// <param name="t">Вторая строка</param>
        /// <returns>Расстояние</returns>
        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s.Length;

            var d = new int[s.Length + 1, t.Length + 1];

            for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= t.Length; j++) d[0, j] = j;

            for (var i = 1; i <= s.Length; i++)
            {
                for (var j = 1; j <= t.Length; j++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }
    }
}