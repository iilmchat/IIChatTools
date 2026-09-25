using System;
using System.Collections.Generic;
using System.Linq;
using IIChatTools.Services.Interfaces;

namespace IIChatTools.Services.Implementation.Rag.Parsers
{
    /// <summary>
    /// Реализация <see cref="IRagDocumentParserRegistry"/>
    /// (v1.5.0, KI-083, Шаг 4B).
    ///
    /// <para>
    /// Принимает <c>IEnumerable&lt;IRagDocumentParser&gt;</c> из DI
    /// (все зарегистрированные парсеры), строит плоский список для
    /// линейного обхода в <see cref="Resolve"/> и объединённый набор
    /// расширений для <see cref="GetAllSupportedExtensions"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Порядок обхода</b> — порядок регистрации в DI.
    /// Если один и тот же файл может обработать несколько парсеров,
    /// побеждает первый зарегистрированный.
    /// </para>
    /// </summary>
    public sealed class RagDocumentParserRegistry : IRagDocumentParserRegistry
    {
        /// <summary>Все зарегистрированные парсеры (для линейного обхода).</summary>
        private readonly IReadOnlyList<IRagDocumentParser> _parsers;

        /// <summary>Объединённый набор расширений (все парсеры).</summary>
        private readonly IReadOnlyList<string> _allExtensions;

        /// <summary>
        /// Создаёт реестр из коллекции парсеров (получает через DI).
        /// </summary>
        /// <param name="parsers">
        /// Все зарегистрированные парсеры. Может быть пустым
        /// (тогда <see cref="Resolve"/> всегда возвращает <c>null</c>).
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="parsers"/> = null</exception>
        public RagDocumentParserRegistry(IEnumerable<IRagDocumentParser> parsers)
        {
            if (parsers == null)
                throw new ArgumentNullException(nameof(parsers));

            _parsers = parsers.ToList();

            // Собираем union всех расширений (case-insensitive, без дублей).
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parser in _parsers)
            {
                if (parser?.SupportedExtensions == null)
                    continue;

                foreach (var ext in parser.SupportedExtensions)
                {
                    if (!string.IsNullOrWhiteSpace(ext))
                        extensions.Add(ext);
                }
            }
            _allExtensions = extensions.ToList();
        }

        /// <inheritdoc />
        public IRagDocumentParser Resolve(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            foreach (var parser in _parsers)
            {
                if (parser?.CanParse(filePath) == true)
                    return parser;
            }

            return null;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetAllSupportedExtensions() => _allExtensions;
    }
}