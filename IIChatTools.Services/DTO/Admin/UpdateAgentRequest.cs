using System.Collections.Generic;

namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// Запрос на обновление агента из админки (v1.4.0 Фаза 6, KI-052).
    /// Все поля опциональны — null означает «не менять».
    /// </summary>
    public class UpdateAgentRequest
    {
        /// <summary>Русское отображаемое имя (обязательно не пустое).</summary>
        public string DisplayName { get; set; }

        /// <summary>Краткое описание.</summary>
        public string Description { get; set; }

        /// <summary>Модель LM Studio (пустое значение = null = из конфига).</summary>
        public string Model { get; set; }

        /// <summary>Максимум шагов (1–30).</summary>
        public int? MaxSteps { get; set; }

        /// <summary>Требует ли подтверждения.</summary>
        public bool? RequiresApprovalByDefault { get; set; }

        /// <summary>Отключён ли агент.</summary>
        public bool? Disabled { get; set; }

        /// <summary>System prompt.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Список имён доступных инструментов.</summary>
        public IReadOnlyList<string> AllowedTools { get; set; }
    }
}