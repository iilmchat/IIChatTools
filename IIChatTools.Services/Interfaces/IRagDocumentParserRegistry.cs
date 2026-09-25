using System.Collections.Generic;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Реестр парсеров документов для RAG
    /// (v1.5.0, KI-083, Шаг 4B).
    ///
    /// <para>
    /// Позволяет маршрутизировать файл к подходящему парсеру по расширению.
    /// Все зарегистрированные <see cref="IRagDocumentParser"/> (Singleton)
    /// передаются в конструктор реализации через DI
    /// (<c>IEnumerable&lt;IRagDocumentParser&gt;</c>).
    /// </para>
    ///
    /// <para>
    /// Реализация — <b>Singleton</b>. Порядок обхода парсеров — порядок
    /// регистрации в DI (первый матч побеждает).
    /// </para>
    /// </summary>
    public interface IRagDocumentParserRegistry
    {
        /// <summary>
        /// Возвращает парсер, способный обработать файл, или <c>null</c>,
        /// если формат не поддержан.
        /// </summary>
        /// <param name="filePath">Путь к файлу (используется только расширение)</param>
        /// <returns>Парсер или <c>null</c></returns>
        IRagDocumentParser Resolve(string filePath);

        /// <summary>
        /// Возвращает объединение расширений, поддерживаемых всеми парсерами
        /// (для UI-валидации «какие файлы можно загружать»).
        ///
        /// <para>
        /// Расширения — в нижнем регистре, с точкой (<c>.txt</c>, <c>.md</c>).
        /// Дубликаты устранены. Порядок не гарантирован.
        /// </para>
        /// </summary>
        /// <returns>Список расширений</returns>
        IReadOnlyList<string> GetAllSupportedExtensions();
    }
}