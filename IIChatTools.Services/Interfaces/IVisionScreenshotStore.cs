using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Хранилище скриншотов Vision Agent в workspace пользователя:
    /// <c>{workspace}/screenshots/{taskId}/step-NNN.png</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.5). См. DESIGN § 6.5 (Privacy), § 7.6.
    /// </para>
    /// <para>
    /// Все пути — через <c>PathHelper</c> (RULES § 1.9) — защита от
    /// path-traversal. taskId нормализуется (только <c>[a-zA-Z0-9_-]</c>).
    /// </para>
    /// </remarks>
    public interface IVisionScreenshotStore
    {
        /// <summary>
        /// Сохраняет PNG-скриншот шага в workspace пользователя.
        /// </summary>
        /// <param name="userId">Владелец workspace.</param>
        /// <param name="taskId">Идентификатор задачи (используется в имени папки).</param>
        /// <param name="stepIndex">Номер шага (1-based).</param>
        /// <param name="pngBytes">PNG-байты.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>
        /// Относительный путь от workspace root
        /// (например, <c>screenshots/vt_8f2a/step-001.png</c>).
        /// <c>null</c>, если сохранение отключено
        /// (<c>VisionAgent:Privacy:SaveToWorkspace = false</c>).
        /// </returns>
        Task<string> SaveAsync(
            int userId,
            string taskId,
            int stepIndex,
            byte[] pngBytes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает абсолютный путь к папке задачи
        /// (<c>{workspace}/screenshots/{taskId}</c>). Если папки нет — <c>null</c>.
        /// </summary>
        /// <param name="userId">Владелец workspace.</param>
        /// <param name="taskId">Идентификатор задачи.</param>
        /// <returns>Абсолютный путь или <c>null</c>.</returns>
        Task<string> GetTaskDirectoryAsync(int userId, string taskId);
    }
}