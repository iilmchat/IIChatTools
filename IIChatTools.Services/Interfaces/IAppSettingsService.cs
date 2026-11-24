using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// D02 / KI-274: атомарно применяет набор настроек (создание/обновление)
        /// одним вызовом SaveChanges. Реализации по умолчанию — последовательный
        /// fallback (для fake-сервисов в тестах); production-реализация
        /// использует один контекст БД и одну транзакцию.
        /// </summary>
        /// <param name="settings">Настройки к применению</param>
        /// <returns>Количество обработанных записей</returns>
        async Task<int> UpsertBatchAsync(IReadOnlyList<SettingDto> settings)
        {
            // Default interface method — вызывается только через интерфейс
            // (RULES § 4.46). Последовательный fallback для fake-сервисов;
            // production-реализация переопределяет метод для атомарности.
            if (settings == null) return 0;

            var existing = await GetAllAsync();
            var count = 0;
            foreach (var setting in settings)
            {
                var found = existing.FirstOrDefault(s => s.Key == setting.Key);
                if (found == null)
                    await CreateAsync(setting);
                else
                    await UpdateAsync(found.Id, setting);
                count++;
            }
            return count;
        }
    }
}