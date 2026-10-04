using System.Collections.Generic;
using IIChatTools.Services.DTO.VisionAgent;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Общий helper для формирования user-message Planner LLM (JSON-пейлоад
    /// task / history / screen / plan). Используется и LmStudio, и External клиентами.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф5.3). См. DESIGN § 4.5.
    /// </remarks>
    public static class VisionPlannerPayloadBuilder
    {
        /// <summary>Настройки сериализации: camelCase, indented, без null.</summary>
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

        /// <summary>
        /// Формирует user-message с сериализованным контекстом (camelCase, indented).
        /// </summary>
        /// <param name="task">Задача пользователя.</param>
        /// <param name="history">История шагов (уже обрезанная).</param>
        /// <param name="screen">Описание текущего экрана.</param>
        /// <param name="plan">Список подзадач.</param>
        /// <returns>JSON-строка для user-сообщения.</returns>
        public static string Build(
            string task,
            IReadOnlyList<VisionStepDto> history,
            ScreenDescriptionDto screen,
            IReadOnlyList<string> plan)
        {
            var payload = new
            {
                task = task ?? string.Empty,
                history = history ?? new List<VisionStepDto>(),
                screen = screen ?? new ScreenDescriptionDto(),
                plan = plan ?? new List<string>()
            };
            return JsonConvert.SerializeObject(payload, Settings);
        }
    }
}