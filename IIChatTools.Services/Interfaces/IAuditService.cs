// IAuditService.cs
using System.Threading.Tasks;
using IIChatTools.Data.Entities;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса аудита для записи действий
    /// </summary>
    public interface IAuditService
    {
        /// <summary>
        /// Асинхронно записывает запись аудита в БД
        /// </summary>
        Task LogActionAsync(AuditLog logEntry);
    }
}