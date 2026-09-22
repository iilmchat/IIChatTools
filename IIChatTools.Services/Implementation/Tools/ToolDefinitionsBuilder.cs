using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO;
using Newtonsoft.Json.Linq;
using IIChatTools.Services.Implementation.Tools;

namespace IIChatTools.Services.Implementation.Tools
{
    /// <summary>
    /// Формирует JSON-схему инструментов в формате OpenAI Function Calling
    /// для передачи в LM Studio (<c>tools[]</c>).
    /// Общий builder для Chat и SubAgent — избегаем дублирования логики.
    /// </summary>
    public static class ToolDefinitionsBuilder
    {
        /// <summary>
        /// Формирует массив схем из описаний инструментов.
        /// </summary>
        /// <param name="descriptors">Все зарегистрированные инструменты</param>
        /// <param name="allowedNames">
        /// Опциональный белый список имён (если null — включаются все).
        /// </param>
        /// <param name="excludeNames">
        /// Опциональный список имён для исключения (например, родительский инструмент суб-агента).
        /// </param>
        /// <returns>JArray схем в формате OpenAI Function Calling</returns>
        public static JArray Build(
            IEnumerable<ToolDescriptor> descriptors,
            IReadOnlyList<string> allowedNames = null,
            IReadOnlyCollection<string> excludeNames = null)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            var allowedSet = allowedNames != null && allowedNames.Count > 0
                ? new HashSet<string>(allowedNames, StringComparer.OrdinalIgnoreCase)
                : null;

            var excludeSet = excludeNames != null && excludeNames.Count > 0
                ? new HashSet<string>(excludeNames, StringComparer.OrdinalIgnoreCase)
                : null;

            var array = new JArray();
            foreach (var d in descriptors)
            {
                if (string.IsNullOrWhiteSpace(d?.Name)) continue;

                if (excludeSet != null && excludeSet.Contains(d.Name)) continue;
                if (allowedSet != null && !allowedSet.Contains(d.Name)) continue;

                array.Add(BuildSchema(d));
            }

            return array;
        }

        /// <summary>
        /// Формирует JSON-схему одного инструмента.
        /// </summary>
        /// <param name="descriptor">Описание инструмента</param>
        /// <returns>JObject схемы</returns>
        public static JObject BuildSchema(ToolDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var properties = new JObject();
            var required = new JArray();

            var parameters = descriptor.Parameters ?? Array.Empty<ToolParameterDescriptor>();
            foreach (var p in parameters)
            {
                if (string.IsNullOrWhiteSpace(p?.Name)) continue;

                var propSchema = new JObject { ["type"] = NormalizeType(p.Type) };
                if (!string.IsNullOrWhiteSpace(p.Description))
                    propSchema["description"] = p.Description;
                if (p.Default != null)
                    propSchema["default"] = JToken.FromObject(p.Default);

                properties[p.Name] = propSchema;

                if (p.Required)
                    required.Add(p.Name);
            }

            return new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = descriptor.Name,
                    ["description"] = descriptor.Description,
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = required
                    }
                }
            };
        }

        /// <summary>
        /// Приводит тип параметра к каноническому виду JSON Schema.
        /// LM Studio / llama.cpp принимает только:
        /// string / integer / number / boolean / array / object.
        /// </summary>
        /// <param name="rawType">Тип из <see cref="ToolParameterDescriptor.Type"/></param>
        /// <returns>Канонический тип</returns>
        public static string NormalizeType(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
                return "string";

            switch (rawType.Trim().ToLowerInvariant())
            {
                case "string":
                case "str":
                case "text":
                    return "string";

                case "int":
                case "int32":
                case "int64":
                case "long":
                case "integer":
                    return "integer";

                case "float":
                case "double":
                case "decimal":
                case "number":
                    return "number";

                case "bool":
                case "boolean":
                    return "boolean";

                case "array":
                case "list":
                    return "array";

                case "object":
                case "dict":
                    return "object";

                default:
                    return "string";
            }
        }
    }
}