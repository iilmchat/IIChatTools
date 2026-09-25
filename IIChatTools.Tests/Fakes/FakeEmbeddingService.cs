using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Tests.Fakes
{
    /// <summary>
    /// Переиспользуемый fake <see cref="IEmbeddingService"/> для тестов
    /// (v1.5.0, KI-083, Шаг 5A).
    ///
    /// <para>
    /// Поддерживает два режима:
    /// <list type="bullet">
    ///   <item>Дефолтный — детерминированный ненулевой вектор из <c>string.GetHashCode()</c>
    ///     (одинаковый текст → одинаковый вектор, разные → разные).</item>
    ///   <item>Кастомный — <see cref="SetVector"/> задаёт конкретный вектор
    ///     для конкретного текста (для точного контроля score в тестах).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Размерность по умолчанию — 3 (маленькая, тесты быстрее).
    /// </para>
    /// </summary>
    public sealed class FakeEmbeddingService : IEmbeddingService
    {
        private readonly Dictionary<string, float[]> _customVectors =
            new Dictionary<string, float[]>(StringComparer.Ordinal);

        /// <summary>Размерность вектора (3 по умолчанию).</summary>
        public int Dimensions { get; set; } = 3;

        /// <summary>Счётчик вызовов <see cref="GetEmbeddingsAsync"/> (для ассертов).</summary>
        public int BatchCallCount { get; private set; }

        /// <summary>Счётчик вызовов <see cref="GetEmbeddingAsync"/>.</summary>
        public int SingleCallCount { get; private set; }

        /// <summary>
        /// Задаёт конкретный вектор для конкретного текста (для точного контроля score).
        /// </summary>
        /// <param name="text">Текст</param>
        /// <param name="vector">Вектор (не нормализованный — нормализуется внутри store)</param>
        public void SetVector(string text, float[] vector)
        {
            _customVectors[text ?? string.Empty] = vector
                ?? throw new ArgumentNullException(nameof(vector));
        }

        /// <inheritdoc />
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            return Task.FromResult(VectorFromText(text));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            var result = new List<float[]>(texts.Count);
            foreach (var t in texts)
                result.Add(VectorFromText(t));
            return Task.FromResult<IReadOnlyList<float[]>>(result);
        }

        /// <summary>
        /// Возвращает кастомный вектор или детерминированный дефолт.
        /// </summary>
        private float[] VectorFromText(string text)
        {
            var key = text ?? string.Empty;

            if (_customVectors.TryGetValue(key, out var custom))
                return custom;

            // Дефолт: [1.0, hash%1000/1000, (hash/1000)%1000/1000]
            // Компонент [0] = 1.0f — гарантирует ненулевую норму.
            var hash = key.GetHashCode();
            return new[]
            {
                1.0f,
                Math.Abs(hash % 1000) / 1000.0f,
                Math.Abs((hash / 1000) % 1000) / 1000.0f
            };
        }
    }
}