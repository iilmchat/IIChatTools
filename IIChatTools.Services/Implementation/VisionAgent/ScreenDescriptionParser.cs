using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        /// KI-215: regex для удаления блока <c>"center": { ... }</c> из JSON.
        /// VL-модель иногда возвращает арифметическое выражение вместо числа
        /// (<c>"x": 546 + (378 / 2)</c>). Значение не валидно для JSON-парсера,
        /// но KI-199 всё равно игнорирует VL-center и пересчитывает из bounds,
        /// поэтому безопасно удалить поле полностью до <c>JObject.Parse</c>.
        /// <para>
        /// <c>[^{}]*</c> — не пропускает вложенные фигурные скобки
        /// (center всегда плоский: только x / y).
        /// Опциональная запятая в конце — на случай, если center не последний ключ.
        /// </para>
        /// </summary>
        private static readonly Regex CenterObjectRegex = new Regex(
            @"""center""\s*:\s*\{[^{}]*\}\s*,?",
            RegexOptions.Compiled);

        /// <summary>
        /// KI-215: regex для удаления trailing comma перед <c>}</c> или <c>]</c>.
        /// Появляется как следствие удаления <c>"center"</c>: если center был
        /// последним ключом объекта, после его удаления остаётся
        /// <c>{ "x": 1, "y": 2, }</c> — невалидный для <c>JObject.Parse</c>.
        /// </summary>
        private static readonly Regex TrailingCommaRegex = new Regex(
            @",\s*([\]}])",
            RegexOptions.Compiled);

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

            // 6. Извлечь ui_elements с дедупликацией и cap.
            // KI-160 (v1.12.x): слабые VL-модели (3B) зацикливаются, генерируя
            // десятки копий одного элемента с одинаковым id → max_tokens
            // обрезает JSON на середине массива → парсер получает битый JSON
            // → ui_elements = [] → Planner видит «пустой экран» → fail.
            //
            // KI-201 (v1.13.x): та же проблема у Qwen2.5-VL-7B — генерация
            // 768+ токенов и обрыв JSON на середине 8-го элемента. Парсер
            // JObject.Parse бросал исключение → fallback с пустым ui_elements.
            // Решение: (а) сначала пробуем JObject.Parse — норм. путь;
            // (б) при провале — regex-recovery: вытаскиваем все ПОЛНЫЕ
            // объекты {...} из ui_elements[] через балансировку скобок.
            const int MaxElements = 8;
            const int MaxIdLength = 40;

            var elements = new List<UiElementDto>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var elementsToken = GetTokenIgnoreCase(root, "ui_elements");
            if (elementsToken is JArray array)
            {
                foreach (var item in array)
                {
                    if (elements.Count >= MaxElements) break;

                    var element = ParseUiElement(item as JObject);
                    if (element == null) continue;
                    if (element.Id.Length > MaxIdLength) continue;
                    if (!seenIds.Add(element.Id)) continue;

                    elements.Add(element);
                }
            }

            // KI-201: если JObject.Parse упал (битый/обрезанный JSON) — не
            // доходим до этой точки (см. catch выше). Но если JObject.Parse
            // УСПЕШНО распарсил (JSON оказался валидным), а elementsToken
            // вернул null (поле переименовано/усечено) — тоже recovery.

            // KI-201: recovery для случая, когда JObject.Parse УСПЕШНО съел
            // невалидный JSON (например, Newtonsoft толерантен к trailing comma),
            // но массив ui_elements[] не сформирован. Плюс fallback для
            // частично-обрезанного ответа.
            if (elements.Count == 0)
            {
                var recovered = TryRecoverPartialElements(text, MaxElements, MaxIdLength);
                if (recovered.Count > 0)
                {
                    elements = recovered;
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
        /// <para>
        /// KI-215: дополнительно удаляет блок <c>"center": {...}</c>, если VL-модель
        /// сгенерировала арифметическое выражение (<c>"x": 546 + (378 / 2)</c>)
        /// вместо числа — не валидный JSON, ломающий <c>JObject.Parse</c>.
        /// </para>
        /// </summary>
        /// <returns>JSON-строка или <c>null</c>, если объект не найден.</returns>
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

            var json = text.Substring(start, end - start + 1);

            // KI-215: VL-модель иногда возвращает "center" как арифметическое выражение
            //   "center": { "x": 546 + (378 / 2), "y": 120 + (49 / 2) }
            // Это не валидный JSON (значения — не числа, а expression-строки без
            // кавычек). JObject.Parse падает → ui_elements=[] → Planner видит
            // пустой экран и не может кликнуть. KI-199 уже игнорирует VL-center
            // и пересчитывает из bounds, поэтому безопасно удалить "center"
            // полностью до парсинга.
            json = CenterObjectRegex.Replace(json, string.Empty);

            // KI-215 (следствие): если "center" был последним ключом объекта,
            // после замены останется trailing comma: `{ "x": 1, "y": 2, }`.
            // Убираем запятые перед } или ].
            json = TrailingCommaRegex.Replace(json, "$1");

            return json;
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

            // KI-193: нормализация id — VL-модель нестабильна в именовании
            // (search_btn на одном кадре, search_button на следующем).
            id = VisionIdNormalizer.Normalize(id);

            // KI-160: id только из цифр (10000000000...) — галлюцинация.
            if (id.Length >= 8 && id.All(c => char.IsDigit(c)))
            {
                return null;
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

            // KI-199: VL-модель нестабильна в вычислении center.
            // Согласно наблюдениям (smoke KI-197, chatId=57), Qwen2.5-VL-7B
            // может вернуть center.y=20 при bounds.y=375 — это область
            // адресной строки Chrome, а не центр элемента.
            //
            // Правило: если есть bounds — center ВСЕГДА пересчитывается
            // как bounds.x + w/2, bounds.y + h/2. Если bounds нет —
            // используется center из ответа VL (fallback).
            var centerToken = GetTokenIgnoreCase(obj, "center");
            var parsedCenterX = (centerToken as JObject)?["x"]?.Value<int?>();
            var parsedCenterY = (centerToken as JObject)?["y"]?.Value<int?>();

            int centerX, centerY;
            if (element.Bounds != null && element.Bounds.W > 0 && element.Bounds.H > 0)
            {
                centerX = element.Bounds.X + element.Bounds.W / 2;
                centerY = element.Bounds.Y + element.Bounds.H / 2;
            }
            else
            {
                centerX = parsedCenterX ?? 0;
                centerY = parsedCenterY ?? 0;
            }

            element.Center = new UiElementCenterDto
            {
                X = centerX,
                Y = centerY
            };

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
            if (obj == null) return 0;
            foreach (var prop in obj.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value?.Value<int>() ?? 0;
                }
            }
            return 0;
        }

        // ============================================================
        // KI-201: recovery из обрезанного JSON.
        // ============================================================

        /// <summary>
        /// KI-201: восстанавливает частичный <c>ui_elements[]</c> из обрезанного
        /// JSON. Находит все <b>полные</b> JSON-объекты <c>{...}</c> внутри
        /// массива <c>ui_elements</c> (или во всём тексте, если массив не найден),
        /// используя балансировку фигурных скобок. Непарсибельные элементы
        /// игнорируются.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Зачем:</b> VL-модели (Qwen2.5-VL-7B) иногда превышают MaxTokens
        /// и обрывают JSON на середине элемента. <c>JObject.Parse</c> падает,
        /// <c>ui_elements</c> пустой → Planner не видит UI → валидатор
        /// отклоняет click.
        /// </para>
        /// <para>
        /// <b>Стратегия:</b> найти в тексте позицию <c>"ui_elements"</c> →
        /// найти <c>[</c> после неё → сканировать от <c>[</c>, отслеживая
        /// баланс <c>{</c>/<c>}</c> и <c>[</c>/<c>]</c> (с учётом строк и
        /// escape-последовательностей) → каждый раз, когда баланс возвращается
        /// к нулю (закрылась очередная <c>}</c>), пытаться распарсить
        /// накопленный фрагмент как <c>JObject</c> → если валидно, добавить
        /// элемент.
        /// </para>
        /// </remarks>
        private static List<UiElementDto> TryRecoverPartialElements(
            string text, int maxElements, int maxIdLength)
        {
            var result = new List<UiElementDto>();
            if (string.IsNullOrEmpty(text)) return result;

            // 1. Найти начало массива ui_elements.
            var keyIdx = text.IndexOf("\"ui_elements\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return result;

            var arrStart = text.IndexOf('[', keyIdx);
            if (arrStart < 0) return result;

            // 2. Сканировать от arrStart, собирая полные {...} объекты.
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depth = 0;          // баланс { }
            var inString = false;
            var escape = false;
            var objStart = -1;

            for (int i = arrStart; i < text.Length; i++)
            {
                var c = text[i];

                if (inString)
                {
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') { inString = false; continue; }
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        // Найден полный объект [objStart..i].
                        var fragment = text.Substring(objStart, i - objStart + 1);
                        try
                        {
                            var obj = JObject.Parse(fragment);
                            var element = ParseUiElement(obj);
                            if (element != null
                                && element.Id.Length <= maxIdLength
                                && seenIds.Add(element.Id))
                            {
                                result.Add(element);
                                if (result.Count >= maxElements) break;
                            }
                        }
                        catch
                        {
                            // Один невалидный фрагмент — не блокируем остальные.
                        }
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    // Конец массива.
                    break;
                }
            }

            return result;
        }
    }
}