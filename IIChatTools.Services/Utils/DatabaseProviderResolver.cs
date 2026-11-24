using System;

namespace IIChatTools.Services.Utils
{
    /// <summary>
    /// D07 / KI-242: резолвер провайдера БД. Поддерживаемые значения
    /// <c>Database:Provider</c>: Sqlite, SqlServer, InMemory.
    /// PostgreSQL объявлен в Design как целевой, но требует пакета Npgsql
    /// и отдельной миграции — при выборе возвращается понятная ошибка.
    /// </summary>
    public static class DatabaseProviderResolver
    {
        /// <summary>Канонические имена провайдеров.</summary>
        public static readonly string[] Supported = { "Sqlite", "SqlServer", "InMemory" };

        /// <summary>
        /// Нормализует и проверяет имя провайдера.
        /// </summary>
        /// <param name="raw">Значение конфига (может быть null)</param>
        /// <returns>Каноническое имя</returns>
        /// <exception cref="InvalidOperationException">Если провайдер не поддерживается</exception>
        public static string Resolve(string raw)
        {
            var value = string.IsNullOrWhiteSpace(raw) ? "SqlServer" : raw.Trim();

            foreach (var name in Supported)
            {
                if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            if (string.Equals(value, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Провайдер PostgreSQL пока не доступен (требуется Npgsql и миграции). " +
                    "Используйте Sqlite / SqlServer / InMemory. См. DESIGN v2.3 § 4B (D07).");
            }

            throw new InvalidOperationException(
                $"Неизвестный Database:Provider = '{value}'. Допустимо: {string.Join(", ", Supported)}.");
        }
    }
}
