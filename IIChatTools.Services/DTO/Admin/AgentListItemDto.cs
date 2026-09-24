using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// DTO агента для админки (v1.4.0 Фаза 6, KI-052).
    /// Возвращается в списке и в ответе на обновление.
    /// Содержит все поля <see cref="DTO.SubAgent.SubAgentDescriptor"/>
    /// (для модалки редактирования) + вычисляемые счётчики.
    /// </summary>
    public class AgentListItemDto
    {
        /// <summary>Техническое имя (snake_case), например "file_system_agent".</summary>
        public string Name { get; set; }

        /// <summary>Русское отображаемое имя.</summary>
        public string DisplayName { get; set; }

        /// <summary>Краткое описание.</summary>
        public string Description { get; set; }

        /// <summary>Модель LM Studio (или null — из конфига).</summary>
        public string Model { get; set; }

        /// <summary>Максимум шагов (1–30).</summary>
        public int MaxSteps { get; set; }

        /// <summary>Требует ли подтверждения пользователя.</summary>
        public bool RequiresApprovalByDefault { get; set; }

        /// <summary>Отключён ли агент (не виден в Chat).</summary>
        public bool Disabled { get; set; }

        /// <summary>System prompt (для модалки редактирования).</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Список имён доступных инструментов.</summary>
        public IReadOnlyList<string> AllowedTools { get; set; }

        /// <summary>Количество доступных инструментов (= AllowedTools.Count).</summary>
        public int ToolCount { get; set; }
    }
}