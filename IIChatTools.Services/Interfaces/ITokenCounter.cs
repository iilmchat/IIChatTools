namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Счётчик токенов (v1.4.x, KI-049).
    ///
    /// <para>
    /// Использует tiktoken-совместимую BPE-модель (cl100k_base) как приближение.
    /// Для моделей Qwen/Gemma точность ±5-10% — достаточно для аналитики
    /// (LM Studio не отдаёт <c>usage</c> в SSE-режиме, см. KI-049).
    /// </para>
    /// </summary>
    public interface ITokenCounter
    {
        /// <summary>
        /// Считает количество токенов в тексте.
        /// Возвращает <c>0</c> для пустой/null строки.
        /// </summary>
        /// <param name="text">Текст</param>
        /// <returns>Количество токенов</returns>
        int CountTokens(string text);

        /// <summary>
        /// Считает количество токенов в наборе сообщений (роль + контент),
        /// включая небольшой overhead на каждое сообщение (как в OpenAI API).
        /// </summary>
        /// <param name="messages">Пары (role, content)</param>
        /// <returns>Оценка количества токенов</returns>
        int CountConversation(System.Collections.Generic.IEnumerable<(string Role, string Content)> messages);
    }
}