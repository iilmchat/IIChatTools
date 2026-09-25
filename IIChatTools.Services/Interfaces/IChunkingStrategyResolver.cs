namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Резолвер стратегий чанкинга по имени
    /// (v1.5.0, KI-083, Шаг 3C).
    ///
    /// <para>
    /// Позволяет выбирать стратегию из конфига (<c>Rag:Chunking:Strategy</c>)
    /// без жёсткой связи потребителя с конкретной реализацией.
    /// </para>
    ///
    /// <para>
    /// <b>Замечание про DI:</b> все стратегии регистрируются как
    /// <c>IChunkingStrategy</c> (см. <c>Startup.cs</c>). При одиночной
    /// инжекции <c>IChunkingStrategy</c> DI вернёт последнюю зарегистрированную
    /// (Fixed). Для выбора нужной стратегии — использовать этот резолвер.
    /// </para>
    /// </summary>
    public interface IChunkingStrategyResolver
    {
        /// <summary>
        /// Возвращает стратегию по имени. Если <paramref name="name"/> пустой
        /// или не найдено — возвращает default (<c>recursive</c>).
        /// </summary>
        /// <param name="name">Имя стратегии (<c>recursive</c> | <c>sentence</c> | <c>fixed</c>)</param>
        /// <returns>Стратегия чанкинга</returns>
        IChunkingStrategy Resolve(string name);

        /// <summary>
        /// Возвращает default-стратегию (<c>recursive</c>).
        /// </summary>
        /// <returns>Default-стратегия</returns>
        IChunkingStrategy GetDefault();
    }
}