using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.VisionAgent;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Валидатор URL по whitelist/blacklist доменов.
    /// Используется всеми backend'ами Vision Agent (Local/Sandbox/RemoteVnc).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф2.3). См. DESIGN § 6.1, § 5.1.
    /// </remarks>
    public static class VisionWhitelistValidator
    {
        /// <summary>
        /// Проверяет, разрешён ли переход по указанному URL.
        /// </summary>
        /// <param name="url">URL (http/https). Может быть null / пустым / невалидным.</param>
        /// <param name="whitelist">Настройки whitelist / blacklist.</param>
        /// <param name="error">
        /// Сообщение об ошибке (заполняется, если <c>false</c>). Для логов и UI.
        /// </param>
        /// <returns><c>true</c> — URL разрешён; <c>false</c> — отклонён.</returns>
        public static bool IsAllowed(string url, VisionWhitelistOptions whitelist, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                error = "URL пустой.";
                return false;
            }

            if (whitelist == null)
            {
                error = "Настройки whitelist не заданы.";
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                error = $"Некорректный URL: {url}";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = $"Разрешены только http/https. Получено: {uri.Scheme}";
                return false;
            }

            var host = uri.Host.ToLowerInvariant();

            // 1. DeniedDomains — приоритет выше whitelist.
            if (MatchesAny(host, whitelist.DeniedDomains))
            {
                error = $"Домен «{host}» в чёрном списке.";
                return false;
            }

            // 2. AllowAnyDomain — «открытый режим», но Denied уже отфильтрован.
            if (whitelist.AllowAnyDomain)
            {
                return true;
            }

            // 3. Whitelist.
            if (!MatchesAny(host, whitelist.Domains))
            {
                error = $"Домен «{host}» не в whitelist. Разрешены: " +
                        string.Join(", ", whitelist.Domains ?? new List<string>());
                return false;
            }

            return true;
        }

        /// <summary>
        /// Проверяет, совпадает ли host с одним из паттернов.
        /// Поддерживается wildcard-поддомен: <c>*.rzd.ru</c> матчит <c>a.rzd.ru</c>,
        /// <c>b.a.rzd.ru</c>, но не <c>rzd.ru</c> (без поддомена).
        /// </summary>
        /// <param name="host">Хост (lowercase).</param>
        /// <param name="patterns">Паттерны из конфига (например, <c>["rzd.ru", "*.wikipedia.org"]</c>).</param>
        /// <returns><c>true</c>, если хотя бы один паттерн сматчился.</returns>
        private static bool MatchesAny(string host, IEnumerable<string> patterns)
        {
            if (patterns == null || string.IsNullOrEmpty(host))
            {
                return false;
            }

            foreach (var raw in patterns)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var pattern = raw.Trim().ToLowerInvariant();

                // Точное совпадение: "rzd.ru" → host == "rzd.ru".
                if (!pattern.Contains('*'))
                {
                    if (string.Equals(host, pattern, StringComparison.Ordinal))
                    {
                        return true;
                    }
                    continue;
                }

                // Wildcard-поддомен: "*.rzd.ru" → host == "a.rzd.ru", "b.a.rzd.ru".
                // НЕ матчит голый "rzd.ru".
                if (pattern.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = pattern.Substring(1);      // ".rzd.ru"
                    if (host.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}