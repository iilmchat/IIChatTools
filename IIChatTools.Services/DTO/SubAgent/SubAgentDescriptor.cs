using System.Collections.Generic;

namespace IIChatTools.Services.DTO.SubAgent
{
    /// <summary>
    /// Описание специализированного суб-агента (для реестра и админки).
    /// Загружается из конфигурации (секция <c>SubAgents</c>) и может быть
    /// переопределён через <c>AppSettings</c> в runtime (админка).
    /// </summary>
    public class SubAgentDescriptor
    {
        /// <summary>
        /// Техническое имя (snake_case), например <c>file_system_agent</c>.
        /// Используется как ключ в <see cref="Interfaces.ISubAgentRegistry"/>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Русское отображаемое имя, например «Агент файловой системы».
        /// Показывается в админке (таблица агентов, модалка редактирования).
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Краткое описание назначения агента (1-2 предложения).
        /// Для админки и, возможно, для документации.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Полный system prompt, который уходит модели LM Studio
        /// при запуске задачи внутри агента.
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// Список имён инструментов, доступных внутри агента.
        /// Передаётся в <see cref="SubAgentTaskRequest.AllowedTools"/>.
        /// </summary>
        public IReadOnlyList<string> AllowedTools { get; set; }

        /// <summary>
        /// Модель LM Studio (например, <c>qwen/qwen3-4b-2507</c>).
        /// Если <c>null</c> — используется <c>LmStudio:Model</c> из appsettings.
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Максимум шагов внутри агента (1–30). По умолчанию 10.
        /// </summary>
        public int MaxSteps { get; set; } = 10;

        /// <summary>
        /// Требует ли вызов агента подтверждения пользователя целиком
        /// (approval на входе в агента, не на каждый tool-call).
        /// См. DESIGN v1.4.0 § 11.
        /// </summary>
        public bool RequiresApprovalByDefault { get; set; }

        /// <summary>
        /// Отключён ли агент. Отключённые не попадают в список tools,
        /// который уходит в Chat (см. <c>GetEnabled()</c>).
        /// </summary>
        public bool Disabled { get; set; }
    }
}