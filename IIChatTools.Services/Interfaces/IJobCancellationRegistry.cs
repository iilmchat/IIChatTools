using System.Threading;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр токенов отмены для фоновых задач.
    ///
    /// <para>
    /// Singleton: <see cref="CancellationTokenSource"/> живёт столько же,
    /// сколько выполняется job, и удаляется в <c>finally</c> runner'а.
    /// </para>
    /// </summary>
    public interface IJobCancellationRegistry
    {
        /// <summary>
        /// Создаёт или возвращает <see cref="CancellationTokenSource"/> для задачи.
        /// </summary>
        /// <param name="jobId">Идентификатор задачи.</param>
        /// <returns>CTS задачи (один и тот же для повторных вызовов).</returns>
        CancellationTokenSource GetOrCreate(int jobId);

        /// <summary>
        /// Отменяет задачу по её ID. Если задачи нет в реестре — no-op
        /// (задача ещё в очереди или уже завершена).
        /// </summary>
        /// <param name="jobId">Идентификатор задачи.</param>
        void Cancel(int jobId);

        /// <summary>
        /// Удаляет задачу из реестра (после завершения) и освобождает CTS.
        /// </summary>
        /// <param name="jobId">Идентификатор задачи.</param>
        void Remove(int jobId);
    }
}
