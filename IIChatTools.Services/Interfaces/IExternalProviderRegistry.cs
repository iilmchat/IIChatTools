using System.Collections.Generic;
using IIChatTools.Services.DTO.ExternalLlm;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр провайдеров внешних LLM, загруженных из конфигурации
    /// (v1.8.1, KI-109, DESIGN_EXTERNAL_LLM § 4.1).
    ///
    /// <para>
    /// Источник — секция <c>ExternalLlm:Providers</c> (см. <see cref="ExternalLlmOptions"/>).
    /// Имена провайдеров case-insensitive (OrdinalIgnoreCase).
    /// </para>
    ///
    /// <para>
    /// <b>Singleton.</b> Читается один раз при старте; изменения через runtime
    /// не поддерживаются (в отличие от <c>SqlAgentOptionsProvider</c>).
    /// </para>
    /// </summary>
    public interface IExternalProviderRegistry
    {
        /// <summary>
        /// Имя провайдера по умолчанию (из <c>ExternalLlm:DefaultProvider</c>).
        /// </summary>
        string DefaultProvider { get; }

        /// <summary>
        /// Все зарегистрированные имена провайдеров (для `list_external_providers`).
        /// Порядок — как в конфигурации.
        /// </summary>
        /// <returns>Список имён (может быть пустым, если конфиг некорректен).</returns>
        IReadOnlyList<string> GetNames();

        /// <summary>
        /// Настройки провайдера по имени.
        /// </summary>
        /// <param name="name">Имя провайдера (case-insensitive)</param>
        /// <returns>Настройки или <c>null</c>, если провайдер не зарегистрирован.</returns>
        ExternalProviderOptions Get(string name);
    }
}