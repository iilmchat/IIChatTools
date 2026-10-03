using System;
using System.Collections.Generic;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Маппинг имён клавиш (из Planner LLM) в Win32 virtual-key codes (VK).
    /// Имена case-insensitive. Поддерживаются синонимы (<c>Enter</c>/<c>Return</c>,
    /// <c>Escape</c>/<c>Esc</c>, <c>Delete</c>/<c>Del</c>, <c>PageUp</c>/<c>PgUp</c>).
    /// </summary>
    /// <remarks>
    /// v1.12.0 (KI-131, Ф2.6). См. DESIGN § 4.3, § 4.5.
    /// </remarks>
    public static class VisionKeyMapper
    {
        /// <summary>Известные клавиши: имя (lowercase) → VK-код.</summary>
        private static readonly Dictionary<string, ushort> s_keys =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                // Специальные
                ["enter"] = 0x0D, ["return"] = 0x0D,
                ["tab"] = 0x09,
                ["escape"] = 0x1B, ["esc"] = 0x1B,
                ["space"] = 0x20,
                ["backspace"] = 0x08, ["back"] = 0x08,
                ["delete"] = 0x2E, ["del"] = 0x2E,
                ["insert"] = 0x2D, ["ins"] = 0x2D,

                // Навигация
                ["home"] = 0x24,
                ["end"] = 0x23,
                ["pageup"] = 0x21, ["pgup"] = 0x21,
                ["pagedown"] = 0x22, ["pgdn"] = 0x22,
                ["up"] = 0x26, ["arrowup"] = 0x26,
                ["down"] = 0x28, ["arrowdown"] = 0x28,
                ["left"] = 0x25, ["arrowleft"] = 0x25,
                ["right"] = 0x27, ["arrowright"] = 0x27,
            };

        /// <summary>Модификаторы: имя → VK-код (Left-вариант по умолчанию).</summary>
        private static readonly Dictionary<string, ushort> s_modifiers =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                ["ctrl"] = 0xA2,     // VK_LCONTROL
                ["control"] = 0xA2,
                ["lctrl"] = 0xA2,
                ["rctrl"] = 0xA3,    // VK_RCONTROL
                ["alt"] = 0xA4,      // VK_LMENU
                ["lalt"] = 0xA4,
                ["ralt"] = 0xA5,     // VK_RMENU
                ["shift"] = 0xA0,    // VK_LSHIFT
                ["lshift"] = 0xA0,
                ["rshift"] = 0xA1,   // VK_RSHIFT
                ["win"] = 0x5B,      // VK_LWIN
                ["meta"] = 0x5B,
                ["cmd"] = 0x5B,
                ["lwin"] = 0x5B,
                ["rwin"] = 0x5C,     // VK_RWIN
            };

        /// <summary>
        /// Возвращает VK-код для имени клавиши. Поддерживает:
        /// буквы A-Z, цифры 0-9, F1-F12, именованные клавиши.
        /// </summary>
        /// <param name="keyName">Имя (case-insensitive). Пример: <c>Enter</c>, <c>F5</c>, <c>A</c>.</param>
        /// <returns>VK-код или <c>null</c>, если имя не распознано.</returns>
        public static ushort? GetVirtualKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return null;

            var key = keyName.Trim();

            // 1. Буквы A-Z → 0x41..0x5A.
            if (key.Length == 1)
            {
                var ch = char.ToUpperInvariant(key[0]);
                if (ch >= 'A' && ch <= 'Z') return (ushort)ch;
                if (ch >= '0' && ch <= '9') return (ushort)ch;   // 0x30..0x39
            }

            // 2. F1-F12 → 0x70..0x7B.
            if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f'))
            {
                if (int.TryParse(key.Substring(1), out var n) && n >= 1 && n <= 12)
                {
                    return (ushort)(0x70 + (n - 1));
                }
            }

            // 3. Именованные.
            if (s_keys.TryGetValue(key, out var vk)) return vk;

            return null;
        }

        /// <summary>
        /// Возвращает VK-код для имени модификатора. Left-вариант по умолчанию.
        /// </summary>
        /// <param name="modifierName">Пример: <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Win</c>, <c>RCtrl</c>.</param>
        /// <returns>VK-код или <c>null</c>.</returns>
        public static ushort? GetModifierVirtualKey(string modifierName)
        {
            if (string.IsNullOrWhiteSpace(modifierName)) return null;
            return s_modifiers.TryGetValue(modifierName.Trim(), out var vk) ? (ushort?)vk : null;
        }

        /// <summary>
        /// Является ли имя модификатором (<c>Ctrl</c> / <c>Alt</c> / <c>Shift</c> / <c>Win</c>).
        /// </summary>
        public static bool IsModifier(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;
            return s_modifiers.ContainsKey(keyName.Trim());
        }
    }
}