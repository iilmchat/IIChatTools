using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.DTO.VisionAgent;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Валидатор действий Vision Agent. Проверяет действие перед выполнением
    /// в backend: blocked keys / hotkeys, лимиты длины текста, clamp deltaY,
    /// существование target в ui_elements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.1). См. DESIGN § 6.2.
    /// </para>
    /// <para>
    /// <b>Что НЕ делает:</b> не проверяет координаты x / y в верхней границе
    /// (нет доступа к размерам экрана; это делает
    /// <see cref="VisionMouseCoordinates.NormalizeToAbsolute"/> в backend).
    /// Проверяет только неотрицательность.
    /// </para>
    /// <para>
    /// <b>Sanitization:</b> если длина текста превышает лимит или deltaY
    /// больше максимума — валидатор возвращает Success + SanitizedAction
    /// (обрезанный / clamp). Backend должен использовать SanitizedAction,
    /// если он не null.
    /// </para>
    /// </remarks>
    public sealed class VisionActionValidator : IVisionActionValidator
    {
        /// <summary>Known actions (совпадает с <c>VisionActionParser.KnownActions</c>).</summary>
        private static readonly HashSet<string> KnownActions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "click", "double_click", "right_click", "move_mouse",
                "type", "press_key", "hotkey", "scroll", "wait",
                "done", "fail"
            };

        private readonly VisionActionValidationOptions _options;
        private readonly ILogger<VisionActionValidator> _logger;

        /// <summary>
        /// Создаёт валидатор.
        /// </summary>
        /// <param name="options">Настройки Vision Agent (секция <c>VisionAgent</c>).</param>
        /// <param name="logger">Логгер.</param>
        /// <exception cref="ArgumentNullException">Если один из параметров null.</exception>
        public VisionActionValidator(
            IOptions<VisionAgentOptions> options,
            ILogger<VisionActionValidator> logger)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Value.ActionValidation
                ?? new VisionActionValidationOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public VisionActionResult Validate(VisionActionDto action, ScreenDescriptionDto screen)
        {
            // 1. Базовая проверка.
            if (action == null)
            {
                return Reject("Действие не задано (action == null).");
            }

            if (string.IsNullOrWhiteSpace(action.Action))
            {
                return Reject("Поле `action` пустое.");
            }

            var actionType = action.Action.Trim().ToLowerInvariant();

            // 2. Whitelist action.
            if (!KnownActions.Contains(actionType))
            {
                return Reject($"Неизвестный action: «{action.Action}».");
            }

            // 3. done / fail — не проверяем target / coords.
            if (actionType == "done" || actionType == "fail")
            {
                return Ok();
            }

            // 4. type — clamp длины.
            var sanitizedAction = CloneIfNeeded(action);
            if (actionType == "type")
            {
                var maxLen = Math.Max(1, _options.MaxTextLength);
                if (!string.IsNullOrEmpty(action.Text) && action.Text.Length > maxLen)
                {
                    _logger.LogWarning(
                        "VisionAgent: текст {Actual} символов > лимита {Max} — обрезаем",
                        action.Text.Length, maxLen);
                    sanitizedAction.Text = action.Text.Substring(0, maxLen);
                }
            }

            // 5. scroll — clamp deltaY.
            if (actionType == "scroll" && action.DeltaY.HasValue)
            {
                var maxDelta = Math.Max(1, _options.MaxScrollDelta);
                var clamped = Math.Clamp(action.DeltaY.Value, -maxDelta, maxDelta);
                if (clamped != action.DeltaY.Value)
                {
                    _logger.LogWarning(
                        "VisionAgent: deltaY {Actual} > лимита {Max} — clamp",
                        action.DeltaY.Value, maxDelta);
                    sanitizedAction.DeltaY = clamped;
                }
            }

            // 6. press_key — блокированные клавиши.
            if (actionType == "press_key")
            {
                if (string.IsNullOrWhiteSpace(action.Key))
                {
                    return Reject("press_key: не задан `key`.");
                }

                if (IsBlockedKey(action.Key))
                {
                    return Reject($"Клавиша «{action.Key}» запрещена политикой.");
                }
            }

            // 7. hotkey — заблокированные комбинации.
            if (actionType == "hotkey")
            {
                if (action.Keys == null || action.Keys.Count == 0)
                {
                    return Reject("hotkey: не задан `keys`.");
                }

                // 7.1. Любая одиночная клавиша в BlockedKeys.
                foreach (var k in action.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k) && IsBlockedKey(k))
                    {
                        return Reject($"Клавиша «{k}» (в hotkey) запрещена политикой.");
                    }
                }

                // 7.2. Комбинация в BlockedHotkeys.
                if (IsBlockedHotkey(action.Keys))
                {
                    return Reject(
                        $"Комбинация [{string.Join("+", action.Keys)}] запрещена политикой.");
                }
            }

            // 8. target — существование в ui_elements (кроме done/fail).
            if (!string.IsNullOrWhiteSpace(action.Target))
            {
                var exists = screen?.UiElements?.Any(el =>
                    string.Equals(el.Id, action.Target, StringComparison.Ordinal))
                    ?? false;

                if (!exists)
                {
                    return Reject(
                        $"Target «{action.Target}» не найден в ui_elements текущего экрана.");
                }
            }

            // 9. Координаты — только неотрицательность.
            if (action.X.HasValue && action.X.Value < 0)
            {
                return Reject($"Координата X < 0: {action.X.Value}.");
            }

            if (action.Y.HasValue && action.Y.Value < 0)
            {
                return Reject($"Координата Y < 0: {action.Y.Value}.");
            }

            // 10. Sanitization?
            var needsSanitization =
                (sanitizedAction.Text != action.Text) ||
                (sanitizedAction.DeltaY != action.DeltaY);

            return new VisionActionResult
            {
                Success = true,
                SanitizedAction = needsSanitization ? sanitizedAction : null
            };
        }

        // ============================================================
        // Private
        // ============================================================

        /// <summary>
        /// Клавиша в <see cref="VisionActionValidationOptions.BlockedKeys"/>?
        /// Регистронезависимо.
        /// </summary>
        private bool IsBlockedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var blocked = _options.BlockedKeys;
            if (blocked == null || blocked.Count == 0) return false;

            return blocked.Any(b =>
                string.Equals(b, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Комбинация в <see cref="VisionActionValidationOptions.BlockedHotkeys"/>?
        /// Сравнение — по отсортированному lowercase-набору (порядок и регистр не важны).
        /// </summary>
        private bool IsBlockedHotkey(IReadOnlyList<string> keys)
        {
            var blocked = _options.BlockedHotkeys;
            if (blocked == null || blocked.Count == 0) return false;

            var inputNorm = NormalizeHotkey(keys);
            foreach (var blockedCombo in blocked)
            {
                if (blockedCombo == null) continue;
                if (string.Equals(inputNorm, NormalizeHotkey(blockedCombo), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Нормализация hotkey: sorted lowercase через «+».
        /// </summary>
        private static string NormalizeHotkey(IReadOnlyList<string> keys)
        {
            if (keys == null || keys.Count == 0) return string.Empty;

            var normalized = keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim().ToLowerInvariant())
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            return string.Join("+", normalized);
        }

        /// <summary>
        /// Создаёт копию действия (для sanitization без мутации входного DTO).
        /// </summary>
        private static VisionActionDto CloneIfNeeded(VisionActionDto source)
        {
            return new VisionActionDto
            {
                Action = source.Action,
                Target = source.Target,
                X = source.X,
                Y = source.Y,
                Text = source.Text,
                Key = source.Key,
                Keys = source.Keys != null ? new List<string>(source.Keys) : null,
                DeltaY = source.DeltaY,
                Reason = source.Reason
            };
        }

        private static VisionActionResult Reject(string error)
        {
            return new VisionActionResult
            {
                Success = false,
                Error = error
            };
        }

        private static VisionActionResult Ok()
        {
            return new VisionActionResult { Success = true };
        }
    }
}