using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.VisionAgent;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Парсер ответа Planner LLM в <see cref="VisionActionDto"/>.
    /// Устойчив к markdown-обёрткам, case-insensitive именам полей, битому JSON.
    /// При полном провале возвращает <c>action = "fail"</c> с reason = сырой текст.
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф5.2). См. DESIGN § 4.5.
    /// </remarks>
    public static class VisionActionParser
    {
        /// <summary>Известные значения <c>action</c> (whitelist).</summary>
        public static readonly IReadOnlyList<string> KnownActions = new[]
        {
            "click", "double_click", "right_click", "move_mouse",
            "type", "press_key", "hotkey", "scroll", "wait",
            "done", "fail"
        };

        /// <summary>
        /// Парсит ответ Planner LLM в <see cref="VisionActionDto"/>.
        /// Никогда не бросает — при любой ошибке возвращает <c>fail</c>.
        /// </summary>
        /// <param name="rawLlmResponse">Сырой текст ответа от LLM.</param>
        /// <returns>Действие. Гарантированно не <c>null</c>.</returns>
        public static VisionActionDto Parse(string rawLlmResponse)
        {
            // 1. Null / пусто.
            if (string.IsNullOrWhiteSpace(rawLlmResponse))
            {
                return Fail("Пустой ответ от Planner LLM.");
            }

            var text = rawLlmResponse.Trim().TrimStart('\uFEFF');

            // 2. Извлечь JSON-объект (снять markdown-обёртки, отрезать текст).
            var jsonText = ExtractJsonObject(text);
            if (jsonText == null)
            {
                return Fail("Planner LLM не вернул JSON: " + Truncate(text, 200));
            }

            // 3. Парсинг JSON.
            JObject root;
            try
            {
                root = JObject.Parse(jsonText);
            }
            catch
            {
                return Fail("Невалидный JSON от Planner LLM: " + Truncate(text, 200));
            }

            // 4. Извлечь action.
            var action = GetStringIgnoreCase(root, "action");
            if (string.IsNullOrWhiteSpace(action))
            {
                return Fail("Поле `action` отсутствует в ответе Planner LLM.");
            }

            action = action.Trim().ToLowerInvariant();
            if (!KnownActions.Contains(action))
            {
                // KI-197 (Fix 2): улучшенный reason с подсказкой о типичной
                // галлюцинации Planner LLM. qwen3-4b иногда выдумывает
                // действия вне whitelist (navigate / goto / open / search) —
                // как знакомые паттерны из Playwright / Puppeteer / browser-API.
                var hint = IsKnownBrowserHallucination(action)
                    ? " Возможно, LLM перепутала action с browser-командой " +
                      "(Playwright / Puppeteer). Навигация выполняется ДО loop'а — " +
                      "URL открывается вызывающим кодом через `run_task(url=...)`. " +
                      "Для ожидания используй `wait`, для завершения — `done` / `fail`."
                    : string.Empty;

                return Fail($"Неизвестный action: «{action}».{hint} " +
                            $"Допустимые: {string.Join(", ", KnownActions)}.");
            }

            // 5. Остальные поля.
            var dto = new VisionActionDto
            {
                Action = action,
                Target = GetStringIgnoreCase(root, "target"),
                Text = GetStringIgnoreCase(root, "text"),
                Key = GetStringIgnoreCase(root, "key"),
                Reason = GetStringIgnoreCase(root, "reason")
            };

            // 6. x / y — nullable int.
            dto.X = GetNullableIntIgnoreCase(root, "x");
            dto.Y = GetNullableIntIgnoreCase(root, "y");

            // 7. deltaY — nullable int.
            dto.DeltaY = GetNullableIntIgnoreCase(root, "deltaY");

            // 8. keys — List<string>.
            var keysToken = GetTokenIgnoreCase(root, "keys");
            if (keysToken is JArray keysArr)
            {
                dto.Keys = keysArr
                    .Select(t => t?.Type == JTokenType.String ? t.Value<string>() : t?.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            return dto;
        }

        /// <summary>Создаёт fail-действие с заданным reason.</summary>
        private static VisionActionDto Fail(string reason)
        {
            return new VisionActionDto { Action = "fail", Reason = reason };
        }

        /// <summary>
        /// Извлекает JSON-объект: снимает markdown-обёртки, отрезает текст до <c>{</c>
        /// и после последней <c>}</c>.
        /// </summary>
        private static string ExtractJsonObject(string text)
        {
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline > 0)
                {
                    var body = text.Substring(firstNewline + 1);
                    var closeFence = body.LastIndexOf("```", StringComparison.Ordinal);
                    text = closeFence >= 0
                        ? body.Substring(0, closeFence).Trim()
                        : body.Trim();
                }
            }

            var start = text.IndexOf('{');
            if (start < 0) return null;

            var end = text.LastIndexOf('}');
            if (end < start) return null;

            return text.Substring(start, end - start + 1);
        }

        private static string GetStringIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return null;
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        private static int? GetNullableIntIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.Float) return (int)token.Value<double>();
            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), out var v)) return v;
            return null;
        }

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

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }

        // ============================================================
        // KI-197 (Fix 2) — эвристика «browser-hallucination».
        // ============================================================

        /// <summary>
        /// KI-197 (Fix 2): типичные «browser-actions», которые LLM выдумывает
        /// вне whitelist. Возвращаются знакомыми паттернами из Playwright /
        /// Puppeteer / других browser-API.
        /// </summary>
        private static readonly HashSet<string> KnownBrowserHallucinations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "navigate",
                "goto",
                "go",
                "open",
                "open_url",
                "open_page",
                "visit",
                "browse",
                "load",
                "search",
                "scroll_to",
                "scrollto",
                "redirect",
                "click_selector",
                "type_selector",
                "wait_for_selector",
                "waitforselector"
            };

        /// <summary>
        /// KI-197 (Fix 2): проверяет, относится ли action к типичным
        /// browser-командам (Playwright / Puppeteer).
        /// </summary>
        /// <param name="action">Action от LLM (lowercase).</param>
        /// <returns>true — если похоже на browser-команду.</returns>
        private static bool IsKnownBrowserHallucination(string action)
        {
            return !string.IsNullOrEmpty(action)
                && KnownBrowserHallucinations.Contains(action);
        }
    }
}