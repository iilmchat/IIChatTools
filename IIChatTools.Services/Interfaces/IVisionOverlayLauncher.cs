using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Launcher on-screen indicator'а Vision Agent
    /// (обязателен для local-harness и sandbox — DESIGN § 6.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.8). См. DESIGN § 4.6, § 6.4.
    /// </para>
    /// <para>
    /// <b>Текущая реализация</b> — <c>NoopVisionOverlayLauncher</c>
    /// (IsAvailable = false). Реальный WPF overlay
    /// (<c>IIChatTools.VisionOverlay</c>) — Ф6.7, отдельный проект.
    /// </para>
    /// </remarks>
    public interface IVisionOverlayLauncher
    {
        /// <summary>
        /// Доступен ли overlay в текущей сборке / на текущей ОС.
        /// <c>false</c> → loop не будет вызывать <see cref="StartAsync"/>.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Запускает overlay для задачи.
        /// </summary>
        /// <param name="taskId">Идентификатор задачи (для отображения в overlay).</param>
        /// <param name="maxSteps">Лимит шагов (для прогресса «N из M»).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Handle для обновления прогресса и закрытия. Никогда не <c>null</c>.
        /// </returns>
        Task<IVisionOverlayHandle> StartAsync(
            string taskId,
            int maxSteps,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Handle одного экземпляра overlay. Используется loop'ом для обновления
    /// прогресса и закрытия по завершении задачи.
    /// </summary>
    /// <remarks>
    /// <b>IDisposable.</b> Закрытие обязательно в <c>finally</c> — даже при ошибке.
    /// </remarks>
    public interface IVisionOverlayHandle : IDisposable
    {
        /// <summary>
        /// Обновляет статус в overlay: «Шаг N из M — action».
        /// </summary>
        /// <param name="stepIndex">Номер текущего шага (1-based).</param>
        /// <param name="maxSteps">Всего шагов.</param>
        /// <param name="action">Описание действия (короткое, 1-3 слова).</param>
        void UpdateProgress(int stepIndex, int maxSteps, string action);

        /// <summary>
        /// Устанавливает финальное сообщение перед закрытием (успех / ошибка).
        /// </summary>
        /// <param name="summary">Короткий текст («Готово», «Отменено», ...).</param>
        /// <param name="success">Окраска: зелёный / красный.</param>
        void SetFinalStatus(string summary, bool success);
    }
}