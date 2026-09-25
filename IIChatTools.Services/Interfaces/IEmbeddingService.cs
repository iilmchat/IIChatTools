using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис генерации эмбеддингов для RAG (v1.5.0, KI-083, Фаза 1).
    ///
    /// <para>
    /// Обёртка над <see cref="ILmStudioClient.GetEmbeddingsAsync"/>:
    /// разбивает большие списки на батчи (по <c>Rag:Embedding:BatchSize</c>),
    /// валидирует вход, агрегирует результат.
    /// </para>
    ///
    /// <para>
    /// Реализация — <b>Singleton</b>: не держит состояния, все методы
    /// потокобезопасны. Зависимость <see cref="ILmStudioClient"/> тоже
    /// переведена в Singleton (см. Startup.cs).
    /// </para>
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Размерность вектора текущей модели эмбеддингов
        /// (768 для <c>nomic-embed-text-v1.5</c>).
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Возвращает эмбеддинг для одного текста.
        /// </summary>
        /// <param name="text">Текст (не пустой)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Вектор float[N] (N = <see cref="Dimensions"/>)</returns>
        /// <exception cref="System.ArgumentException">Если <paramref name="text"/> пустой</exception>
        Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает эмбеддинги для набора текстов.
        ///
        /// <para>
        /// Внутри разбивает на батчи по <c>Rag:Embedding:BatchSize</c>
        /// (по умолчанию 64 — лимит LM Studio) и склеивает результаты
        /// в порядке исходных текстов.
        /// </para>
        /// </summary>
        /// <param name="texts">Список текстов (не пустой)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Список векторов в порядке <paramref name="texts"/></returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="texts"/> равен null</exception>
        /// <exception cref="System.ArgumentException">Если <paramref name="texts"/> пустой</exception>
        Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default);
    }
}