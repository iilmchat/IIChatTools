using System;

namespace IIChatTools.Services.DTO.Rag
{
    /// <summary>
    /// Статус per-user Workspace-индекса для UI <c>/profile</c>
    /// (v1.5.0, KI-083, Шаг 7C.1).
    ///
    /// <para>
    /// Возвращается всеми endpoint'ами <c>/api/profile/workspace-index/*</c>.
    /// При <see cref="IsIndexing"/> = <c>true</c> UI поллит статус каждые 2 с
    /// и обновляет прогресс-бар.
    /// </para>
    /// </summary>
    public class WorkspaceIndexStatusDto
    {
        /// <summary>
        /// Включён ли Workspace-индекс для пользователя
        /// (per-user настройка <c>Workspace.Index.Enabled</c>).
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Количество уникальных файлов в индексе
        /// (<c>DISTINCT DocumentPath</c>).
        /// </summary>
        public int FilesIndexed { get; set; }

        /// <summary>
        /// Количество чанков в индексе (<c>workspace</c>, UserId).
        /// </summary>
        public int ChunkCount { get; set; }

        /// <summary>
        /// Дата последней индексации (UTC). <c>null</c>, если чанков нет.
        /// </summary>
        public DateTime? LastIndexedAt { get; set; }

        /// <summary>
        /// Идёт ли индексация прямо сейчас.
        /// </summary>
        public bool IsIndexing { get; set; }

        /// <summary>
        /// Всего файлов к обработке (0, если индексация не запущена).
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Обработано файлов в текущем прогоне.
        /// </summary>
        public int FilesProcessed { get; set; }

        /// <summary>
        /// Создано чанков в текущем прогоне.
        /// </summary>
        public int ChunksCreated { get; set; }

        /// <summary>
        /// Текст ошибки последнего неудачного файла (для UI-плашки).
        /// <c>null</c> — ошибок нет.
        /// </summary>
        public string LastError { get; set; }
    }
}