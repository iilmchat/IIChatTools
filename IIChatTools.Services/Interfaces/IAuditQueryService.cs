using System.Threading.Tasks;
using IIChatTools.Data.Entities;
using IIChatTools.Services.DTO;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис чтения журнала аудита с пагинацией.
    /// </summary>
    public interface IAuditQueryService
    {
        /// <summary>
        /// Возвращает страницу журнала аудита.
        /// </summary>
        /// <param name="page">Номер страницы (1-индексация)</param>
        /// <param name="pageSize">Размер страницы (1–200)</param>
        /// <param name="userId">Фильтр по пользователю (опционально)</param>
        /// <param name="toolName">Фильтр по имени инструмента (опционально)</param>
        /// <returns>Страница записей</returns>
        Task<PagedResult<AuditLog>> GetPagedAsync(int page, int pageSize, int? userId = null, string toolName = null);
    }
}