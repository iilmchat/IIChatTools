using IIChatTools.Services.DTO;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Кэш результатов инструментов (v1.8.2).
    ///
    /// <para>
    /// <b>Назначение:</b> повторный вызов того же инструмента с теми же
    /// аргументами → мгновенный ответ без внешнего вызова (Wikipedia, web_search,
    /// RAG-поиск). Экономит время и внешние запросы.
    /// </para>
    ///
    /// <para>
    /// <b>Whitelist:</b> кэшируются только инструменты из
    /// <see cref="DTO.Cache.ToolResultCacheOptions.Tools"/>. Остальные —
    /// не кэшируются (safer-by-default).
    /// </para>
    ///
    /// <para>
    /// <b>Ключ:</b> <c>tool:{name}:u{userId}:{sha256(canonical_json(args))}</c>.
    /// <c>userId</c> в ключе — защита от утечки данных между пользователями
    /// (per-user инструменты: <c>search_chat_history</c>, <c>search_workspace</c>,
    /// <c>search_knowledge_base</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> Обёртка над <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>.
    /// </para>
    /// </summary>
    public interface IToolResultCache
    {
        /// <summary>
        /// Кэшируется ли инструмент (проверка whitelist).
        /// Дешёвая операция — можно вызывать перед построением ключа.
        /// </summary>
        /// <param name="toolName">Имя инструмента (<c>ITool.Name</c>)</param>
        /// <returns><c>true</c> — инструмент в whitelist и кэш включён.</returns>
        bool IsCacheable(string toolName);

        /// <summary>
        /// Попытаться получить результат из кэша.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="userId">Идентификатор пользователя (per-user изоляция)</param>
        /// <param name="arguments">Аргументы вызова (канонизируются для ключа)</param>
        /// <returns>
        /// Кэшированный <see cref="ToolResult"/> или <c>null</c>, если записи нет
        /// / истёк TTL / инструмент не в whitelist.
        /// </returns>
        ToolResult TryGet(string toolName, int userId, JObject arguments);

        /// <summary>
        /// Положить результат в кэш. Вызывать только при
        /// <c>ToolResult.Success == true</c> — Fail не кэшируется.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        /// <param name="userId">Идентификатор пользователя</param>
        /// <param name="arguments">Аргументы вызова</param>
        /// <param name="result">Успешный результат</param>
        void Set(string toolName, int userId, JObject arguments, ToolResult result);

        /// <summary>
        /// Полностью очистить кэш для инструмента (все пользователи).
        /// Использование: после reindex RAG-индекса, чтобы не отдавать
        /// устаревшие фрагменты <c>search_knowledge_base</c>.
        /// </summary>
        /// <param name="toolName">Имя инструмента</param>
        void InvalidateAll(string toolName);
    }
}