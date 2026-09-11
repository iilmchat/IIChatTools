using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Admin;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Сервис управления настройками приложения (хранятся в БД, дублируют appsettings.json).
    /// </summary>
    public interface IAppSettingsService
    {
        /// <summary>
        /// Возвращает все настройки из БД.
        /// </summary>
        /// <returns>Список настроек</returns>
        Task<IReadOnlyList<SettingDto>> GetAllAsync();

        /// <summary>
        /// Возвращает настройку по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>Настройка или null</returns>
        Task<SettingDto> GetByIdAsync(int id);

        /// <summary>
        /// Создаёт новую настройку.
        /// </summary>
        /// <param name="dto">Данные настройки</param>
        /// <returns>Созданная настройка</returns>
        /// <exception cref="System.InvalidOperationException">Если ключ уже существует</exception>
        Task<SettingDto> CreateAsync(SettingDto dto);

        /// <summary>
        /// Обновляет существующую настройку.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <param name="dto">Новые данные</param>
        /// <returns>Обновлённая настройка или null</returns>
        Task<SettingDto> UpdateAsync(int id, SettingDto dto);

        /// <summary>
        /// Удаляет настройку.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>true, если удалено</returns>
        Task<bool> DeleteAsync(int id);

        /// <summary>
        /// Сбрасывает значение настройки к значению по умолчанию из appsettings.json.
        /// </summary>
        /// <param name="id">Идентификатор</param>
        /// <returns>Обновлённая настройка или null</returns>
        Task<SettingDto> ResetToDefaultAsync(int id);

        /// <summary>
        /// Синхронизирует настройки из appsettings.json в БД (создаёт отсутствующие,
        /// обновляет поля DefaultValue у существующих).
        /// </summary>
        /// <returns>Количество обработанных ключей</returns>
        Task<int> SyncDefaultsFromConfigurationAsync();
    }
}