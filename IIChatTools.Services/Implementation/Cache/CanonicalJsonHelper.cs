using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.Cache
{
    /// <summary>
    /// Хелпер канонизации JSON + SHA-256 (v1.8.2).
    ///
    /// <para>
    /// <b>Проблема:</b> <see cref="JObject"/> не гарантирует порядок ключей.
    /// <c>{"a":1,"b":2}</c> и <c>{"b":2,"a":1}</c> — семантически идентичны,
    /// но сериализуются в разные строки → разные хэши → промах кэша.
    /// </para>
    ///
    /// <para>
    /// <b>Решение:</b> рекурсивная сортировка ключей <see cref="JObject"/>
    /// по имени (<see cref="StringComparer.Ordinal"/> — не зависит от локали).
    /// Порядок элементов <see cref="JArray"/> сохраняется как есть
    /// (порядок может быть семантически значим).
    /// </para>
    ///
    /// <para>
    /// <b>Static helper.</b> Не Singleton, не инжектится — вызывается
    /// из <c>ToolResultCache</c>.
    /// </para>
    /// </summary>
    public static class CanonicalJsonHelper
    {
        /// <summary>
        /// Возвращает каноническое (детерминированное) строковое представление
        /// <see cref="JToken"/> с отсортированными ключами.
        /// </summary>
        /// <param name="token">Токен для канонизации (JObject / JArray / JValue)</param>
        /// <returns>Канонический JSON без пробелов.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="token"/> равен null</exception>
        public static string Canonicalize(JToken token)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            var sorted = SortRecursive(token);
            return sorted.ToString(Formatting.None);
        }

        /// <summary>
        /// Возвращает SHA-256 хэш строки в hex (lowercase).
        /// </summary>
        /// <param name="value">Входная строка (UTF-8)</param>
        /// <returns>64 символа hex (lowercase).</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="value"/> равен null</exception>
        public static string Sha256Hex(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Строит ключ кэша: <c>tool:{name}:u{userId}:{sha256}</c>.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="arguments">Аргументы вызова</param>
        /// <returns>Ключ для <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>.</returns>
        public static string BuildCacheKey(string toolName, int userId, JObject arguments)
        {
            var canonical = Canonicalize(arguments ?? new JObject());
            var hash = Sha256Hex(canonical);

            return $"tool:{toolName}:u{userId}:{hash}";
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Рекурсивно сортирует ключи <see cref="JObject"/>. Массивы сохраняют порядок.
        /// </summary>
        private static JToken SortRecursive(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                {
                    var sorted = new JObject();
                    foreach (var prop in obj.Properties()
                        .OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        sorted.Add(prop.Name, SortRecursive(prop.Value));
                    }
                    return sorted;
                }
                case JArray arr:
                {
                    var sorted = new JArray();
                    foreach (var item in arr)
                    {
                        sorted.Add(SortRecursive(item));
                    }
                    return sorted;
                }
                default:
                    return token;
            }
        }
    }
}