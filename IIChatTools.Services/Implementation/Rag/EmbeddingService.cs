using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.LmStudio;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Реализация сервиса эмбеддингов (v1.5.0, KI-083, Фаза 1).
    ///
    /// <para>
    /// Singleton — не держит состояния, все методы потокобезопасны.
    /// Инжектит <see cref="ILmStudioClient"/> (тоже Singleton), читает
    /// параметры из секции <c>Rag:Embedding</c> в appsettings.
    /// </para>
    ///
    /// <para>
    /// Кэш эмбеддингов <b>не реализован</b> в Фазе 1 (отложен до Фазы 2
    /// или отдельного шага — см. KI-083 § 4.1.2).
    /// </para>
    /// </summary>
    public class EmbeddingService : IEmbeddingService
    {
        private readonly ILmStudioClient _lmStudioClient;
        private readonly ILogger<EmbeddingService> _logger;

        /// <summary>Размерность вектора (из конфига, default 768).</summary>
        private readonly int _dimensions;

        /// <summary>Максимум текстов за один вызов LM Studio (из конфига, default 64).</summary>
        private readonly int _batchSize;

        /// <summary>
        /// Создаёт сервис эмбеддингов.
        /// </summary>
        /// <param name="lmStudioClient">Клиент LM Studio (Singleton)</param>
        /// <param name="configuration">Конфигурация (секция <c>Rag:Embedding</c>)</param>
        /// <param name="logger">Логгер</param>
        /// <exception cref="ArgumentNullException">Если один из параметров равен null</exception>
        public EmbeddingService(
            ILmStudioClient lmStudioClient,
            IConfiguration configuration,
            ILogger<EmbeddingService> logger)
        {
            _lmStudioClient = lmStudioClient
                ?? throw new ArgumentNullException(nameof(lmStudioClient));

            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _dimensions = ReadInt(configuration, "Rag:Embedding:Dimensions", 768);
            _batchSize = ReadInt(configuration, "Rag:Embedding:BatchSize", 64);

            if (_batchSize <= 0)
            {
                _logger.LogWarning(
                    "Rag:Embedding:BatchSize={BatchSize} некорректен, использую 64",
                    _batchSize);
                _batchSize = 64;
            }
        }

        /// <inheritdoc />
        public int Dimensions => _dimensions;

        /// <inheritdoc />
        public async Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Текст не может быть пустым", nameof(text));

            var vectors = await _lmStudioClient.GetEmbeddingsAsync(
                new[] { text }, model: null, cancellationToken);

            var vector = ExtractVector(vectors, 0);
            ValidateDimension(vector);
            return vector;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            if (texts == null)
                throw new ArgumentNullException(nameof(texts));

            if (texts.Count == 0)
                throw new ArgumentException("Список текстов не может быть пустым", nameof(texts));

            // Быстрый путь: всё в один батч.
            if (texts.Count <= _batchSize)
            {
                var single = await _lmStudioClient.GetEmbeddingsAsync(
                    texts, model: null, cancellationToken);
                return NormalizeAndValidate(single, texts.Count);
            }

            // Медленный путь: разбиваем на батчи по _batchSize.
            var batches = (texts.Count + _batchSize - 1) / _batchSize;
            _logger.LogDebug(
                "EmbeddingService: {Total} текстов разбито на {Batches} батчей по {Size}",
                texts.Count, batches, _batchSize);

            var result = new List<float[]>(texts.Count);

            for (int i = 0; i < texts.Count; i += _batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var take = Math.Min(_batchSize, texts.Count - i);
                var batch = new List<string>(take);
                for (int j = 0; j < take; j++)
                    batch.Add(texts[i + j]);

                var batchResponse = await _lmStudioClient.GetEmbeddingsAsync(
                    batch, model: null, cancellationToken);

                var batchVectors = NormalizeAndValidate(batchResponse, take);
                result.AddRange(batchVectors);
            }

            return result;
        }

        /// <summary>
        /// Извлекает вектор по индексу из <see cref="EmbeddingResponse.Data"/>,
        /// предварительно отсортированного по <see cref="EmbeddingData.Index"/>.
        /// </summary>
        /// <param name="response">Ответ LM Studio</param>
        /// <param name="position">Позиция (0-based)</param>
        /// <returns>Вектор</returns>
        /// <exception cref="InvalidOperationException">Если позиция вне диапазона</exception>
        private static float[] ExtractVector(EmbeddingResponse response, int position)
        {
            if (response?.Data == null || position < 0 || position >= response.Data.Count)
            {
                throw new InvalidOperationException(
                    $"LM Studio вернул некорректный ответ embeddings: ожидалось ≥{position + 1} элементов, " +
                    $"получено {response?.Data?.Count ?? 0}");
            }
            return response.Data[position].Embedding ?? Array.Empty<float>();
        }

        /// <summary>
        /// Проверяет, что вектор имеет ожидаемую размерность, и собирает список.
        /// </summary>
        /// <param name="response">Ответ LM Studio</param>
        /// <param name="expectedCount">Ожидаемое количество векторов</param>
        /// <returns>Список векторов в порядке index</returns>
        /// <exception cref="InvalidOperationException">Если количество векторов не совпало</exception>
        private IReadOnlyList<float[]> NormalizeAndValidate(EmbeddingResponse response, int expectedCount)
        {
            if (response?.Data == null || response.Data.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"LM Studio вернул {response?.Data?.Count ?? 0} векторов, ожидалось {expectedCount}");
            }

            var result = new List<float[]>(expectedCount);
            foreach (var d in response.Data)
            {
                var vector = d.Embedding ?? Array.Empty<float>();
                ValidateDimension(vector);
                result.Add(vector);
            }
            return result;
        }

        /// <summary>
        /// Логирует предупреждение, если размерность вектора не совпала
        /// с конфигом. Не падает — модель могла быть заменена в LM Studio.
        /// </summary>
        /// <param name="vector">Вектор от LM Studio</param>
        private void ValidateDimension(float[] vector)
        {
            if (vector.Length == 0)
            {
                _logger.LogWarning("LM Studio вернул пустой вектор эмбеддинга");
                return;
            }

            if (vector.Length != _dimensions)
            {
                _logger.LogWarning(
                    "Размерность эмбеддинга {Actual} не совпадает с конфигом {Expected} " +
                    "(Rag:Embedding:Dimensions). Модель в LM Studio могла быть заменена.",
                    vector.Length, _dimensions);
            }
        }

        /// <summary>
        /// Читает int из конфигурации (с fallback).
        /// </summary>
        /// <param name="configuration">Конфигурация</param>
        /// <param name="key">Ключ</param>
        /// <param name="defaultValue">Значение по умолчанию</param>
        /// <returns>Значение</returns>
        private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
        {
            var raw = configuration[key];
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }
    }
}