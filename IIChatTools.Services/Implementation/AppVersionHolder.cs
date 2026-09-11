namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Хранит текущую версию приложения.
    /// Класс-разрыв зависимости: слой Services не должен ссылаться на слой API,
    /// поэтому версия устанавливается извне (в Program.cs) и доступна сервисам.
    /// </summary>
    public static class AppVersionHolder
    {
        /// <summary>
        /// Текущая версия приложения. Устанавливается при старте из Program.cs.
        /// Значение по умолчанию используется в тестах и до инициализации.
        /// </summary>
        public static string Current { get; set; } = "1.0.0";
    }
}