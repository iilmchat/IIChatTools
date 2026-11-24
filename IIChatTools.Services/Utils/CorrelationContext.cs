using System;
using System.Threading;

namespace IIChatTools.Services.Utils
{
    /// <summary>
    /// D10: correlationId сквозь SSE → tool → subagent → LLM.
    /// Хранится в AsyncLocal, чтобы не менять сигнатуры всех сервисов.
    /// </summary>
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string> CurrentValue = new();

        /// <summary>Текущий correlationId (может быть null).</summary>
        public static string Current => CurrentValue.Value;

        /// <summary>
        /// Устанавливает correlationId на время операции.
        /// </summary>
        /// <param name="id">Идентификатор (если пуст — генерируется)</param>
        /// <returns>Disposable для восстановления предыдущего значения</returns>
        public static IDisposable Begin(string id = null)
        {
            var previous = CurrentValue.Value;
            CurrentValue.Value = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _previous;
            public Scope(string previous) => _previous = previous;
            public void Dispose() => CurrentValue.Value = _previous;
        }
    }
}
