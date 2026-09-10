namespace IIChatTools.Data.Entities
{
    /// <summary>
    /// Настройка приложения (ключ-значение), хранимая в БД
    /// </summary>
    public class AppSetting : BaseEntity
    {
        /// <summary>
        /// Уникальный ключ настройки
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Значение настройки (в виде строки)
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Тип данных (string, int, bool, json и т.д.)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Категория для группировки
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Признак значения по умолчанию (из appsettings.json)
        /// </summary>
        public bool IsDefault { get; set; }
    }
}