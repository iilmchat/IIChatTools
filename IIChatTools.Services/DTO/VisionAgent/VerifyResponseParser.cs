using System;
using IIChatTools.Services.DTO.VisionAgent;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Парсер ответа VL-модели в режиме Coordinate-then-Verify (KI-162).
    /// Ожидаемый формат: <c>{ "x": 123, "y": 456, "confidence": 0.95, "found": true }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.13.x (KI-162). Устойчив к:
    /// <list type="bullet">
    ///   <item>markdown-обёрткам (<c>```json ... ```</c>);</item>
    ///   <item>тексту до / после JSON;</item>
    ///   <item>case-insensitive именам полей (<c>x</c> / <c>X</c>);</item>
    ///   <item>отсутствию <c>confidence</c> / <c>found</c> — дефолты;</item>
    ///   <item>полностью невалидному JSON — <c>Found = false</c> с причиной.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Никогда не бросает — при любой ошибке возвращает <see cref="VerifyTargetResultDto"/>
    /// с <c>Found = false</c> и заполненным <c>Error</c>. Вызывающий код
    /// делает fallback на bounds center.
    /// </para>
    /// </remarks>
    public static class VerifyResponseParser
    {
        /// <summary>Уверенность по умолчанию, если VL не указала в ответе.</summary>
        private const double DefaultConfidence = 0.7;

        /// <summary>
        /// Парсит ответ VL в <see cref="VerifyTargetResultDto"/>.
        /// </summary>
        /// <param name="rawLlmResponse">Сырой текст ответа VL.</param>
        /// <returns>Результат верификации (гарантированно не <c>null</c>).</returns>
        public static VerifyTargetResultDto Parse(string rawLlmResponse)
        {
            if (string.IsNullOrWhiteSpace(rawLlmResponse))
            {
                return Fail("VL вернула пустой ответ.");
            }

            var text = rawLlmResponse.Trim().TrimStart('\uFEFF');
            var jsonText = ExtractJsonObject(text);
            if (jsonText == null)
            {
                return Fail("VL вернула не-JSON ответ.");
            }

            JObject root;
            try
            {
                root = JObject.Parse(jsonText);
            }
            catch (Exception ex)
            {
                return Fail($"VL вернула невалидный JSON: {ex.Message}");
            }

            // found — если VL явно сказала false, не доверяем координатам.
            var foundToken = GetTokenIgnoreCase(root, "found");
            if (foundToken != null && foundToken.Type == JTokenType.Boolean
                && !foundToken.Value<bool>())
            {
                return Fail("VL не нашла элемент в кропе (found=false).");
            }

            var x = GetIntIgnoreCase(root, "x");
            var y = GetIntIgnoreCase(root, "y");
            if (x <= 0 && y <= 0)
            {
                return Fail("VL не вернула координаты (x/y = 0).");
            }

            // KI-162-fix: грубая защита от копирования примера из промпта.
            // Qwen2.5-VL-7B возвращает ровно `{ x: 123, y: 456 }` — это пример
            // из старой версии промпта. Считаем это галлюцинацией и делаем fallback.
            // (В новой версии промпта — плейсхолдеры, но подстраховка не помешает.)
            if (x == 123 && y == 456)
            {
                return Fail("VL скопировала пример из промпта (123, 456) — fallback.");
            }

            var confidence = GetDoubleIgnoreCase(root, "confidence") ?? DefaultConfidence;

            return new VerifyTargetResultDto
            {
                X = x,
                Y = y,
                Confidence = confidence,
                Found = true,
                Error = null
            };
        }

        // ============================================================
        // Private helpers
        // ============================================================

        /// <summary>Создаёт неуспешный результат с причиной.</summary>
        private static VerifyTargetResultDto Fail(string reason) =>
            new VerifyTargetResultDto
            {
                Found = false,
                Error = reason
            };

        /// <summary>
        /// Снимает markdown-обёртки, отрезает текст до первой <c>{</c>
        /// и после последней <c>}</c>. Возвращает <c>null</c>, если JSON не найден.
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

        /// <summary>Ищет поле в <see cref="JObject"/> без учёта регистра.</summary>
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

        /// <summary>Читает int (0 — если отсутствует / невалидно).</summary>
        private static int GetIntIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return 0;

            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.Float) return (int)token.Value<double>();
            if (token.Type == JTokenType.String
                && int.TryParse(token.Value<string>(), out var v)) return v;
            return 0;
        }

        /// <summary>Читает double или <c>null</c>.</summary>
        private static double? GetDoubleIgnoreCase(JObject obj, string name)
        {
            var token = GetTokenIgnoreCase(obj, name);
            if (token == null || token.Type == JTokenType.Null) return null;

            if (token.Type == JTokenType.Float) return token.Value<double>();
            if (token.Type == JTokenType.Integer) return token.Value<int>();
            if (token.Type == JTokenType.String
                && double.TryParse(token.Value<string>(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v)) return v;
            return null;
        }
    }
}