using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Extensions
{
    /// <summary>
    /// Расширения для безопасного извлечения значений из JObject-аргументов.
    /// </summary>
    public static class JObjectExtensions
    {
        /// <summary>
        /// Извлекает строковое значение по ключу.
        /// </summary>
        /// <param name="obj">Аргументы вызова</param>
        /// <param name="key">Имя параметра</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Строка или значение по умолчанию</returns>
        public static string GetString(this JObject obj, string key, string defaultValue = null)
        {
            if (obj == null || !obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
                return defaultValue;
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        /// <summary>
        /// Извлекает целочисленное значение по ключу.
        /// </summary>
        /// <param name="obj">Аргументы вызова</param>
        /// <param name="key">Имя параметра</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Число или значение по умолчанию</returns>
        public static int GetInt(this JObject obj, string key, int defaultValue = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
                return defaultValue;
            try { return token.Value<int>(); }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Извлекает булево значение по ключу.
        /// </summary>
        /// <param name="obj">Аргументы вызова</param>
        /// <param name="key">Имя параметра</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Булево или значение по умолчанию</returns>
        public static bool GetBool(this JObject obj, string key, bool defaultValue = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var token) || token.Type == JTokenType.Null)
                return defaultValue;
            try { return token.Value<bool>(); }
            catch { return defaultValue; }
        }

        /// <summary>
        /// Извлекает массив строк по ключу.
        /// </summary>
        /// <param name="obj">Аргументы вызова</param>
        /// <param name="key">Имя параметра</param>
        /// <returns>Массив строк (возможно пустой)</returns>
        public static IReadOnlyList<string> GetStringArray(this JObject obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var token) || token.Type != JTokenType.Array)
                return Array.Empty<string>();

            return token
                .Select(t => t.Type == JTokenType.String ? t.Value<string>() : t.ToString())
                .ToList();
        }
    }
}