namespace IIChatTools.Services.DTO.Admin
{
    /// <summary>
    /// DTO настройки приложения.
    /// </summary>
    public class SettingDto
    {
        /// <summary>
        /// Идентификатор.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Ключ настройки.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Значение.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Тип данных (string, int, bool, json).
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Категория для группировки.
        /// </summary>
        public string Category { get; set; } = "Общие";

        /// <summary>
        /// Признак значения по умолчанию.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Значение по умолчанию (из appsettings.json), если есть.
        /// </summary>
        public string DefaultValue { get; set; }
    }
}