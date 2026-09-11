using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис файлового логирования аудита (JSONL-формат с ротацией по дням).
    /// Используется, когда БД недоступна или требуется независимый журнал.
    /// </summary>
    public interface IFileAuditService
    {
        /// <summary>
        /// Записывает запись аудита в файл.
        /// </summary>
        /// <param name="entry">Запись аудита</param>
        /// <returns>Асинхронная задача</returns>
        Task WriteAsync(AuditLog entry);
    }
}