using System;

namespace IIChatTools.Services.Implementation.VisionAgent
{
    /// <summary>
    /// Хелперы для прокрутки колеса мыши: clamp deltaY и конвертация
    /// user-facing значения в Win32 <c>mouseData</c> для
    /// <c>MOUSEEVENTF_WHEEL</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф2.7). Вынесено отдельно для unit-тестов.
    /// </para>
    /// <para>
    /// <b>Семантика знаков (важно!):</b>
    /// <list type="bullet">
    ///   <item>User-facing <c>deltaY</c>: положительный = «прокрутить вниз»
    ///     (увидеть контент ниже), отрицательный = «прокрутить вверх».</item>
    ///   <item>Win32 <c>mouseData</c>: положительный = «колесо от себя»
    ///     (контент вниз = прокрутка вверх), отрицательный = «колесо к себе»
    ///     (контент вверх = прокрутка вниз).</item>
    /// </list>
    /// Знаки инвертированы. <see cref="ToWheelMouseData"/> делает это явно,
    /// чтобы не ошибиться в inline-коде.
    /// </para>
    /// <para>
    /// <b>Единицы измерения:</b> <c>deltaY</c> — в единицах Win32
    /// <c>WHEEL_DELTA</c> (120 = один «щелчок»). Один щелчок колеса
    /// в большинстве приложений прокручивает ~3 строки текста.
    /// </para>
    /// </remarks>
    public static class VisionScrollHelper
    {
        /// <summary>
        /// Ограничивает <paramref name="deltaY"/> диапазоном
        /// <c>[-maxAbs, maxAbs]</c>. Защита от случайных огромных значений.
        /// </summary>
        /// <param name="deltaY">Желаемая прокрутка.</param>
        /// <param name="maxAbs">
        /// Максимальное абсолютное значение. Если ≤ 0 — берётся 1.
        /// </param>
        /// <returns>
        /// <c>deltaY</c>, если модуль ≤ <paramref name="maxAbs"/>;
        /// иначе — знак × <paramref name="maxAbs"/>.
        /// </returns>
        public static int Clamp(int deltaY, int maxAbs)
        {
            if (maxAbs <= 0) maxAbs = 1;
            return Math.Clamp(deltaY, -maxAbs, maxAbs);
        }

        /// <summary>
        /// Конвертирует user-facing <paramref name="deltaY"/> в Win32
        /// <c>mouseData</c> для <c>MOUSEEVENTF_WHEEL</c>. Инвертирует знак
        /// (см. remarks класса).
        /// </summary>
        /// <param name="deltaY">+ = scroll down, − = scroll up.</param>
        /// <returns>
        /// Отрицание <paramref name="deltaY"/> как <c>int</c>
        /// (каст в <c>uint</c> делает вызывающая сторона).
        /// </returns>
        public static int ToWheelMouseData(int deltaY)
        {
            return -deltaY;
        }
    }
}