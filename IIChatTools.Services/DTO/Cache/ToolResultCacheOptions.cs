using System;
using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Cache
{
    /// <summary>
    /// Настройки кэша результатов инструментов
    /// (v1.8.2, KI-121-followup, DESIGN § 2.1).
    ///
    /// <para>
    /// <b>Safer-by-default:</b> кэшируются ТОЛЬКО инструменты, перечисленные
    /// в <see cref="Tools"/>. Если инструмента нет в whitelist — результат
    /// не кэшируется, даже если <see cref="Enabled"/> = <c>true</c>.
    /// </para>
    ///
    /// <para>
    /// Инструменты с побочными эффектами (<c>send_email</c>, <c>save_file</c>,
    /// <c>run_python</c>, <c>execute_query</c>, <c>ask_external_llm</c>, ...)
    /// в whitelist попадать не должны: повторный вызов изменит состояние
    /// системы, а не вернёт тот же результат.
    /// </para>
    /// </summary>
    public class ToolResultCacheOptions
    {
        /// <summary>
        /// Глобальный выключатель. Если <c>false</c> — кэш не используется
        /// ни для одного инструмента, даже если он есть в <see cref="Tools"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Максимальное количество записей в кэше. При превышении
        /// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
        /// вытесняет старые записи (политика — LRU-подобная).
        /// Clamp: [100, 1_000_000].
        /// </summary>
        public int SizeLimit { get; set; } = 10_000;

        /// <summary>
        /// Whitelist инструментов, подлежащих кэшированию.
        /// Ключ — <c>ITool.Name</c> (case-insensitive).
        /// </summary>
        public Dictionary<string, ToolCacheEntryOptions> Tools { get; set; }
            = new Dictionary<string, ToolCacheEntryOptions>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Настройки кэширования одного инструмента (v1.8.2).
    /// </summary>
    public class ToolCacheEntryOptions
    {
        /// <summary>
        /// Индивидуальный выключатель для инструмента.
        /// Позволяет временно отключить кэш без правки whitelist.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Time-to-live записи в секундах.
        /// Clamp: [1, 86400] (1 сутки).
        /// </summary>
        public int TtlSeconds { get; set; } = 900;   // 15 мин по умолчанию
    }
}