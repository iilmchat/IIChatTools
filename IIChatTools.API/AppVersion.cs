using System.Reflection;

namespace IIChatTools.API
{
    /// <summary>
    /// Содержит сведения о версии приложения IIChatTools.
    /// Версия автоматически читается из метаданных сборки
    /// (AssemblyInformationalVersion), которые формируются MSBuild
    /// из свойства &lt;Version&gt; в Directory.Build.props.
    /// Это исключает необходимость править версию в нескольких местах.
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// Текущая версия приложения (например, "1.1.0").
        /// Читается из AssemblyInformationalVersionAttribute,
        /// суффикс "+&lt;hash&gt;" (если MSBuild его добавляет) отбрасывается.
        /// </summary>
        public static string Current { get; } = ResolveCurrent();

        /// <summary>
        /// Человекочитаемое наименование продукта.
        /// </summary>
        public const string ProductName = "IIChatTools";

        /// <summary>
        /// Информация об авторских правах для UI и логов.
        /// Формируется автоматически из <see cref="Current"/>.
        /// </summary>
        public static string Copyright => $"© 2026 RuChating (iilmchat) · IIChatTools v{Current}";

        /// <summary>
        /// Извлекает версию из атрибута AssemblyInformationalVersion.
        /// </summary>
        /// <returns>Строка версии без суффикса сборки (например, "1.1.0").</returns>
        private static string ResolveCurrent()
        {
            var attr = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var raw = attr?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "0.0.0";
            }
            // MSBuild может дописать "+<git-hash>" — отбрасываем
            var plus = raw.IndexOf('+');
            return plus >= 0 ? raw.Substring(0, plus) : raw;
        }
    }
}