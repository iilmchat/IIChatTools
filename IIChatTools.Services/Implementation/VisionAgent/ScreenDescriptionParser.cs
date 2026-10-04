using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.VisionAgent;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Парсер ответа Vision LLM в <see cref="ScreenDescriptionDto"/>.
    /// Устойчив к:
    /// <list type="bullet">
    ///   <item>markdown-обёрткам (<c>```json ... ```</c> и <c>``` ... ```</c>);</item>
    ///   <item>тексту до/после JSON;</item>
    ///   <item>case-insensitive именам полей (description / Description / DESCRIPTION);</item>
    ///   <item>отсутствию <c>ui_elements</c> (default — пустой список);</item>
    ///   <item>BOM в начале ответа;</item>
    ///   <item>полностью невалидному JSON — fallback: <c>Description = сырой текст</c>,
    ///     <c>UiElements = []</c>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф5.1). См. DESIGN § 4.4.
    /// </remarks>
    public static class ScreenDescriptionParser
    {
        /// <summary>
        /// Парсит ответ VL-модели в <see cref="ScreenDescriptionDto"/>.
        /// Никогда не бросает — при любой ошибке возвращает fallback.
        /// </summary>
        /// <param name="rawLlmResponse">Сырой текст ответа от LLM.</param>
        /// <returns>Описание экрана. Гарантированно не <c>null</c>.</returns>
        public static ScreenDescriptionDto Parse(string rawLlmResponse)
        {
            // 1. Защита от null / пустой строки.
            if (string.IsNullOrWhiteSpace(rawLlmResponse))
            {
                return new ScreenDescriptionDto
                {
                    Description = string.Empty,
                    UiElements = new List<UiElementDto>()
                };
            }

            // 2. Strip BOM и whitespace.
            var text = rawLlmResponse.Trim().TrimStart('\uFEFF');

            // 3. Извлечь JSON-подстроку (снять markdown-обёртки, отрезать текст до/после).
            var jsonText = ExtractJsonObject(text);
            if (jsonText == null)
            {
                // JSON не найден — fallback: весь текст как description.
                return new ScreenDescriptionDto
                {
                    Description = text,
                    UiElements = new List<UiElementDto>()
                };
            }

            // 4. Попытаться распарсить JSON.
            JObject root;
            try
            {
                root = JObject.Parse(jsonText);
            }
            catch
            {
                return new ScreenDescriptionDto
                {
                    Description = text,
                    UiElements = new List<UiElementDto>()
                };
            }

            // 5. Извлечь description (case-insensitive).
            var description = GetStringIgnoreCase(root, "description") ?? string.Empty;

            // 6. Извлечь ui_elements.
            var elements = new List<UiElementDto>();
            var elementsToken = GetTokenIgnoreCase(root, "ui_elements");
            if (elementsToken is JArray array)
            {
                foreach (var item in array)
                {
                    var element = ParseUiElement(item as JObject);
                    if (element != null) elements.Add(element);
                }
            }

            return new ScreenDescriptionDto
            {
                Description = description,
                UiElements = elements
            };
        }

        /// <summary>
        /// Извлекает первый валидный JSON-объект из строки: снимает markdown-обёртки,
        /// отрезает текст до первой <c>{</c> и после последней <c>}</c>.
        /// Возвращает <c>null</c>, если JSON не найден.
        /// </summary>
        private static string ExtractJsonObject(string text)
        {
            // Снять markdown-обёртку: ```json ... ``` или ``` ... ```.
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0)
                {
                    var body = text.Substring(firstNewline + 1);
                    var closeFence = body.LastIndexOf("```", StringComparison.Ordinal);
                    if (closeFence >= 0)
                    {
                        text = body.Substring(0, closeFence).Trim();
                    }
                    else
                    {
                        text = body.Trim();
                    }
                }
            }

            // Найти первую { и последнюю }.
            var start = text.IndexOf('{');
            if (start < 0) return null;

            var end = text.LastIndexOf('}');
            if (end < start) return null;

            return text.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Читает строку из <see cref="JObject"/> с case-insensitive именем поля.
        /// </summary>
        private static string GetStringIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return null;
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        /// <summary>
        /// Ищет поле в <see cref="JObject"/> без учёта регистра.
        /// </summary>
        private static JToken GetTokenIgnoreCase(JObject obj, string name)
        {
            if (obj == null) return null;
            foreach (var prop in obj.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Парсит один UI-элемент из <see cref="JObject"/>. Возвращает <c>null</c>,
        /// если элемент невалиден (без id или type).
        /// </summary>
        private static UiElementDto ParseUiElement(JObject obj)
        {
            if (obj == null) return null;

            var id = GetStringIgnoreCase(obj, "id");
            var type = GetStringIgnoreCase(obj, "type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
            {
                return null;   // без id/type элемент бесполезен.
            }

            var element = new UiElementDto
            {
                Id = id,
                Type = type,
                Label = GetStringIgnoreCase(obj, "label"),
                Value = GetStringIgnoreCase(obj, "value"),
                Enabled = GetBoolIgnoreCase(obj, "enabled")
            };

            var boundsToken = GetTokenIgnoreCase(obj, "bounds");
            if (boundsToken is JObject boundsObj)
            {
                element.Bounds = new UiElementBoundsDto
                {
                    X = GetIntIgnoreCase(boundsObj, "x"),
                    Y = GetIntIgnoreCase(boundsObj, "y"),
                    W = GetIntIgnoreCase(boundsObj, "w"),
                    H = GetIntIgnoreCase(boundsObj, "h")
                };
            }

            var centerToken = GetTokenIgnoreCase(obj, "center");
            if (centerToken is JObject centerObj)
            {
                element.Center = new UiElementCenterDto
                {
                    X = GetIntIgnoreCase(centerObj, "x"),
                    Y = GetIntIgnoreCase(centerObj, "y")
                };
            }

            return element;
        }

        /// <summary>
        /// Читает bool или <c>null</c>.
        /// </summary>
        private static bool? GetBoolIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return null;

            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.String)
            {
                var s = token.Value<string>();
                if (bool.TryParse(s, out var b)) return b;
            }
            return null;
        }

        /// <summary>
        /// Читает int (0 — если отсутствует / невалидно).
        /// </summary>
        private static int GetIntIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return 0;

            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.Float) return (int)token.Value<double>();
            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out var v)) return v;
            return 0;
        }
    }
}