using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Парсер текстовых форматов для RAG
    /// (v1.5.0, KI-083, Шаг 4A).
    ///
    /// <para>
    /// Поддерживает 28 расширений: обычный текст, разметка, код, конфиги.
    /// Определяет кодировку через BOM, с fallback UTF-8 → Windows-1251.
    /// Нормализует переносы строк (<c>\r\n</c> → <c>\n</c>).
    /// Для <c>.md</c> — опционально strip'ает frontmatter (<c>--- ... ---</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, регистрируется как Singleton.</b> В конструкторе
    /// выполняется <see cref="Encoding.RegisterProvider"/> для Windows-1251 —
    /// идемпотентная операция.
    /// </para>
    /// </summary>
    public sealed class PlainTextParser : IRagDocumentParser
    {
        /// <summary>
        /// Поддерживаемые расширения (нижний регистр, с точкой).
        /// </summary>
        private static readonly string[] _extensions = new[]
        {
            // Текст
            ".txt", ".md", ".csv", ".tsv", ".log",
            // Разметка
            ".json", ".xml", ".yaml", ".yml", ".html", ".htm",
            // Код
            ".cs", ".py", ".js", ".ts", ".java", ".go", ".rs", ".sql", ".sh", ".ps1",
            // Web
            ".razor", ".cshtml", ".css", ".scss",
            // Конфиги
            ".dockerfile", ".gitignore", ".editorconfig"
        };

        /// <summary>
        /// HashSet для O(1) проверки расширений в <see cref="CanParse"/>.
        /// </summary>
        private static readonly HashSet<string> _extensionSet =
            new HashSet<string>(_extensions, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Кодировка для fallback (Windows-1251 — для русских текстов).
        /// Инициализируется лениво после <see cref="Encoding.RegisterProvider"/>.
        /// </summary>
        private static readonly Encoding _fallbackEncoding = CreateFallbackEncoding();

        /// <summary>
        /// Создаёт парсер. Регистрирует <see cref="CodePagesEncodingProvider"/>
        /// (для Windows-1251) — идемпотентно.
        /// </summary>
        public PlainTextParser()
        {
            // Идемпотентная регистрация провайдера кодировок.
            // Нужно для Encoding.GetEncoding(1251) в .NET Core+.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <inheritdoc />
        public string Name => "PlainText";

        /// <inheritdoc />
        public IReadOnlyList<string> SupportedExtensions => _extensions;

        /// <inheritdoc />
        public bool CanParse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) && _extensionSet.Contains(ext);
        }

        /// <inheritdoc />
        public async Task<ParsedDocument> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл не найден", filePath);

            if (!CanParse(filePath))
                throw new NotSupportedException(
                    $"Расширение '{Path.GetExtension(filePath)}' не поддерживается PlainTextParser");

            // Читаем весь файл байтами — это позволяет явно обработать BOM
            // (StreamReader с явным encoding не всегда стрипает BOM в .NET 10).
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var originalSize = bytes.LongLength;

            // Определяем кодировку и смещение после BOM.
            var (encoding, bomOffset) = DetectEncodingFromBytes(bytes);

            // Декодируем с учётом BOM.
            string text = encoding.GetString(bytes, bomOffset, bytes.Length - bomOffset);

            // Defensive strip BOM-символа (\uFEFF) из начала текста.
            //
            // Это ГАРАНТИРУЕТ отсутствие BOM в результате, даже если:
            //  - byte-based детекция по какой-то причине не сработала (bomOffset=0);
            //  - encoding по какой-то причине оставил \uFEFF в начале строки.
            // Обрезка дешёвая и не влияет на нормальные тексты.
            // Defensive strip: убираем ВСЕ ведущие BOM-символы (\uFEFF).
            // Обычно BOM один, но в редких случаях (склейка файлов,
            // некорректные инструменты) их может быть несколько.
            // Обрезка дешёвая, не влияет на нормальные тексты.
            int bomCharCount = 0;
            while (bomCharCount < text.Length && text[bomCharCount] == '\uFEFF')
            {
                bomCharCount++;
            }
            if (bomCharCount > 0)
            {
                text = text.Substring(bomCharCount);
            }

            // Нормализация переносов строк: \r\n → \n, \r → \n.
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');

            // Для .md — опционально strip frontmatter (по умолчанию true).
            var ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
            {
                text = StripMarkdownFrontmatter(text);
            }

            // Метаданные.
            var metadata = new Dictionary<string, string>
            {
                ["format"] = ext.TrimStart('.').ToLowerInvariant(),
                ["parser"] = Name
            };

            return new ParsedDocument
            {
                Text = text,
                Metadata = metadata,
                OriginalSizeBytes = originalSize,
                PageCount = null   // для PlainText страницы не применимы
            };
        }

        /// <summary>
        /// Определяет кодировку по массиву байт и возвращает смещение (длину BOM),
        /// с которого нужно начинать декодирование.
        /// </summary>
        /// <param name="bytes">Содержимое файла</param>
        /// <returns>Кодировка + смещение начала текста (после BOM)</returns>
        private static (Encoding encoding, int bomOffset) DetectEncodingFromBytes(byte[] bytes)
        {
            // 1) BOM detection: UTF-8 (3 байта), UTF-16 LE/BE (2 байта).
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (new UTF8Encoding(false), 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2);

            // 2) Strict UTF-8 (без BOM) — проверяем на валидность.
            try
            {
                var strictUtf8 = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true);
                strictUtf8.GetString(bytes);   // бросит DecoderFallbackException при invalid bytes
                return (new UTF8Encoding(false), 0);
            }
            catch
            {
                // 3) Fallback — Windows-1251.
                return (_fallbackEncoding, 0);
            }
        }

        /// <summary>
        /// Создаёт fallback-кодировку (Windows-1251). Требует предварительной
        /// регистрации <see cref="CodePagesEncodingProvider.Instance"/>.
        /// </summary>
        private static Encoding CreateFallbackEncoding()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251);
            }
            catch
            {
                // Если провайдер недоступен — используем ASCII (безопасный fallback).
                return Encoding.ASCII;
            }
        }

        /// <summary>
        /// Убирает Markdown-frontmatter (<c>--- ... ---</c> в начале файла),
        /// если он есть.
        /// </summary>
        /// <param name="text">Текст Markdown</param>
        /// <returns>Текст без frontmatter</returns>
        private static string StripMarkdownFrontmatter(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("---"))
                return text;

            var lines = text.Split('\n');
            if (lines.Length < 2 || lines[0].TrimEnd() != "---")
                return text;

            // Ищем закрывающий "---" на отдельной строке.
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].TrimEnd() == "---")
                {
                    // Всё после закрывающего "---", без ведущих пустых строк.
                    var remaining = lines.Skip(i + 1);
                    return string.Join("\n", remaining).TrimStart('\n');
                }
            }

            // Незакрытый frontmatter — не трогаем.
            return text;
        }
    }
}