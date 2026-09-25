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

        /// <summary>
        /// Кодирует текст в последовательность токенов (v1.5.0, KI-083, Шаг 3A).
        ///
        /// <para>
        /// Используется <c>FixedChunkingStrategy</c> для жёсткого разреза
        /// по границам токенов (а не по символам/предложениям).
        /// </para>
        /// </summary>
        /// <param name="text">Текст (пустой → пустой массив)</param>
        /// <returns>Список ID токенов</returns>
        System.Collections.Generic.IReadOnlyList<int> Encode(string text);

        /// <summary>
        /// Декодирует последовательность токенов обратно в текст
        /// (v1.5.0, KI-083, Шаг 3A).
        ///
        /// <para>
        /// Кодировка/декодирование обратимы: <c>Decode(Encode(x)) == x</c>
        /// для большинства текстов (кроме нестандартных Unicode-последовательностей).
        /// </para>
        /// </summary>
        /// <param name="tokens">Последовательность токенов (пустой/null → пустая строка)</param>
        /// <returns>Декодированный текст</returns>
        string Decode(System.Collections.Generic.IReadOnlyList<int> tokens);
    }
}