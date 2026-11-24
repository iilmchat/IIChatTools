using System;

namespace IIChatTools.Services.Utils
{
    /// <summary>
    /// D06: ссылка на модель в формате <c>&lt;provider&gt;:&lt;model&gt;</c>
    /// (например, <c>routerai:qwen/qwen3.8-flash</c>). Обратная совместимость:
    /// старые значения без префикса трактуются как провайдер <c>lmstudio</c>.
    /// </summary>
    public sealed class ModelReference
    {
        /// <summary>Создаёт ссылку.</summary>
        public ModelReference(string provider, string model)
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? "lmstudio" : provider;
            Model = model ?? string.Empty;
        }

        /// <summary>Имя провайдера.</summary>
        public string Provider { get; }

        /// <summary>Имя модели внутри провайдера.</summary>
        public string Model { get; }

        /// <summary>Строковое представление <c>provider:model</c>.</summary>
        public override string ToString() => $"{Provider}:{Model}";

        /// <summary>
        /// Разбирает строку; если префикса нет — provider = "lmstudio".
        /// </summary>
        /// <param name="value">Сырое значение Chat.Model</param>
        /// <returns>Разобранная ссылка</returns>
        public static ModelReference Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new ModelReference("lmstudio", "local-model");

            var colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1)
                return new ModelReference("lmstudio", value.Trim());

            return new ModelReference(value.Substring(0, colon).Trim(), value.Substring(colon + 1).Trim());
        }
    }
}
