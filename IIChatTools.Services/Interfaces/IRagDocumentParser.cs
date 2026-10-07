using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Парсер одного формата документа для RAG
    /// (v1.5.0, KI-083, Шаг 4A).
    ///
    /// <para>
    /// Реализации (MVP + v1.5.x):
    /// <list type="bullet">
    ///   <item><c>PlainTextParser</c> (MVP) — .txt, .md, .csv, code, configs (28 расширений).</item>
    ///   <item><c>PdfParser</c> (v1.5.x) — .pdf (PdfPig).</item>
    ///   <item><c>DocxParser</c> (v1.5.x) — .docx (OpenXml).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Парсеры — stateless, регистрируются как <b>Singleton</b>.
    /// Маршрутизация по расширению — через <c>IRagDocumentParserRegistry</c>
    /// (Шаг 4B, KI-083).
    /// </para>
    /// </summary>
    public interface IRagDocumentParser
    {
        /// <summary>
        /// Имя парсера (для логов и диагностики). Например: <c>PlainText</c>, <c>PdfPig</c>.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Поддерживаемые расширения (с точкой, lowercase, например <c>".txt"</c>).
        /// </summary>
        IReadOnlyList<string> SupportedExtensions { get; }

        /// <summary>
        /// Может ли парсер обработать файл с данным путём (по расширению).
        /// Регистр расширения — не важен.
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <returns>true — если расширение поддерживается</returns>
        bool CanParse(string filePath);

        /// <summary>
        /// Извлекает текст из файла.
        /// </summary>
        /// <param name="filePath">Абсолютный путь (уже валидирован вызывающим кодом)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Извлечённый текст + метаданные</returns>
        /// <exception cref="System.ArgumentNullException">Если <paramref name="filePath"/> = null</exception>
        /// <exception cref="System.IO.FileNotFoundException">Если файл не существует</exception>
        /// <exception cref="System.NotSupportedException">
        /// Если расширение не поддерживается (<see cref="CanParse"/> = false)
        /// </exception>
        Task<ParsedDocument> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Извлекает текст из файла с дополнительными опциями
        /// (v1.13.x, KI-205).
        ///
        /// <para>
        /// <b>Default-метод:</b> игнорирует <paramref name="options"/> и
        /// вызывает базовый <see cref="ParseAsync(string, CancellationToken)"/>.
        /// Парсеры, которым важны опции (например, <c>PdfParser</c> для
        /// сохранения PNG-страниц), переопределяют этот метод.
        /// </para>
        /// </summary>
        /// <param name="filePath">Абсолютный путь (уже валидирован).</param>
        /// <param name="options">
        /// Опции парсинга (<see cref="ParseOptions"/>). Может быть <c>null</c> —
        /// эквивалентно дефолтному поведению.
        /// </param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Извлечённый текст + метаданные.</returns>
        Task<ParsedDocument> ParseAsync(
            string filePath,
            ParseOptions options,
            CancellationToken cancellationToken = default)
        {
            // Default-реализация (C# 8+): игнорируем options, вызываем базовый overload.
            return ParseAsync(filePath, cancellationToken);
        }
    }
}