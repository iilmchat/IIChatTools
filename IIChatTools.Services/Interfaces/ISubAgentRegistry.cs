using System.Collections.Generic;
using IIChatTools.Services.DTO.SubAgent;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр специализированных суб-агентов (v1.4.0, KI-052).
    ///
    /// Загружается из конфигурации (<c>SubAgents:*</c> в appsettings.json).
    /// В Фазе 6 (админка) — дополняется runtime-переопределениями
    /// через <c>AppSettings</c>.
    ///
    /// Singleton (как <see cref="IToolRegistry"/>).
    /// </summary>
    public interface ISubAgentRegistry
    {
        /// <summary>
        /// Все зарегистрированные агенты (включая отключённые).
        /// Сортировка — по имени (детерминированно).
        /// </summary>
        IReadOnlyList<SubAgentDescriptor> GetAll();

        /// <summary>
        /// Только включённые агенты (<c>Disabled == false</c>).
        /// Используется в Chat для построения списка tools.
        /// </summary>
        IReadOnlyList<SubAgentDescriptor> GetEnabled();

        /// <summary>
        /// Описание агента по имени.
        /// </summary>
        /// <param name="name">Техническое имя (snake_case)</param>
        /// <returns>Дескриптор или <c>null</c>, если не найден</returns>
        SubAgentDescriptor Get(string name);

        /// <summary>
        /// Обновить дескриптор (для админки — сохраняется в <c>AppSettings</c>).
        /// </summary>
        /// <param name="descriptor">Новый дескриптор (имя — ключ)</param>
        /// <exception cref="System.ArgumentNullException">Если descriptor == null</exception>
        void Update(SubAgentDescriptor descriptor);

        /// <summary>
        /// Сбросить дескриптор к значениям из appsettings.json.
        /// </summary>
        /// <param name="name">Техническое имя агента</param>
        void Reset(string name);
    }
}