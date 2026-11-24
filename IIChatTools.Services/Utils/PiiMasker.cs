using System;
using System.Text.RegularExpressions;

namespace IIChatTools.Services.Utils
{
    /// <summary>
    /// D09 / KI-138: маскирование PII перед отправкой во внешние LLM/VL.
    /// <para>
    /// Паттерны: email, телефон (RU/международный), номер карты (16 цифр),
    /// СНИЛС (11 цифр с дефисами). Замена — на плейсхолдеры вида [EMAIL].
    /// </para>
    /// </summary>
    public static class PiiMasker
    {
        private static readonly Regex Email = new(
            @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Phone = new(
            @"(?<!\d)(?:\+7|8)[\s\-()]*\d{3}[\s\-()]*\d{3}[\s\-()]*\d{2}[\s\-()]*\d{2}(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex Card = new(
            @"(?<!\d)\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex Snils = new(
            @"(?<!\d)\d{3}-\d{3}-\d{3}[\s\-]?\d{2}(?!\d)",
            RegexOptions.Compiled);

        /// <summary>
        /// Маскирует PII в тексте. null и пустые строки возвращает как есть.
        /// </summary>
        /// <param name="text">Исходный текст</param>
        /// <returns>Текст с плейсхолдерами</returns>
        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = Email.Replace(text, "[EMAIL]");
            result = Snils.Replace(result, "[SNILS]");
            result = Card.Replace(result, "[CARD]");
            result = Phone.Replace(result, "[PHONE]");
            return result;
        }
    }
}
