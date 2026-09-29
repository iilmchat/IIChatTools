namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Провайдер путей приложения
    /// (v1.7.0, KI-097, KI-100).
    /// <para>
    /// Разрывает зависимость <c>Services → API</c>: слой Services не знает
    /// про <c>IWebHostEnvironment</c>, но может получить <c>ContentRootPath</c>
    /// через этот интерфейс. Реализация регистрируется в <c>Startup.cs</c>
    /// (API) на основе <c>IWebHostEnvironment</c>.
    /// </para>
    /// <para>
    /// Используется <see cref="Implementation.SqlAgent.SqlConnectionProvider"/>
    /// для резолвинга **относительных** путей Sqlite (KI-100): SQLite-провайдер
    /// открывает файл относительно <c>Environment.CurrentDirectory</c> процесса,
    /// а не относительно <c>ContentRootPath</c>. При запуске из другой директории
    /// (служба Windows, Docker, `dotnet IIChatTools.API.dll` из корня) это
    /// приводит к `SQLite Error 14: unable to open database file` или к
    /// открытию пустой новой БД.
    /// </para>
    /// </summary>
    public interface IAppPathProvider
    {
        /// <summary>
        /// Абсолютный путь к корню контента приложения
        /// (<c>IWebHostEnvironment.ContentRootPath</c>).
        /// Для dev это обычно <c>C:\Projects\AI\IIChatTools\IIChatTools.API</c>;
        /// для Docker — <c>/app</c>.
        /// </summary>
        string ContentRootPath { get; }
    }
}