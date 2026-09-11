using System.Threading.Tasks;
using IIChatTools.Services.DTO;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис получения сводной информации о состоянии системы.
    /// </summary>
    public interface IStatusService
    {
        /// <summary>
        /// Возвращает снимок состояния системы.
        /// </summary>
        /// <returns>Снимок состояния</returns>
        Task<StatusSnapshot> GetSnapshotAsync();
    }
}