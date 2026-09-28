using System;
using System.Collections.Generic;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Хелпер сборки citations из результатов веб-поиска
    /// (v1.6.1, KI-086-post).
    ///
    /// <para>
    /// Преобразует <see cref="RetrievedWebResult"/> → <see cref="ChatSourceDto"/>
    /// для отображения в UI-блоке «Источники» под ответом ассистента.
    /// Используется тремя Web-инструментами (<c>wikipedia_search</c>,
    /// <c>web_search</c>, <c>fetch_web_content</c>) — во избежание
    /// дублирования логики.
    /// </para>
    ///
    /// <para>
    /// <b>Типы источников:</b>
    /// <list type="bullet">
    ///   <item><c>wiki</c> — Wikipedia;</item>
    ///   <item><c>web</c> — обычные веб-страницы.</item>
    /// </list>
    /// Валидация типа не производится (на случай будущих расширений).
    /// </para>
    ///
    /// <para>
    /// <b>Дедупликация не производится</b> — этим занимается
    /// <c>ChatStreamService</c> (по ключу <c>type|documentPath|chunkIndex</c>).
    /// </para>
    /// </summary>
    public static class WebSourceBuilder
    {
        /// <summary>
        /// Максимальное количество источников по умолчанию
        /// (для инструментов, возвращающих много результатов).
        /// </summary>
        public const int DefaultMaxCount = 5;

        /// <summary>
        /// Преобразует список результатов веб-поиска в список citations.
        ///
        /// <para>
        /// Если <paramref name="results"/> пуст / null — возвращает пустой список
        /// (не <c>null</c>, чтобы вызывающий код мог проверить <c>.Count</c>).
        /// </para>
        ///
        /// <para>
        /// Ограничение <paramref name="maxCount"/> применяется первым
        /// (top-N результатов). Пустые результаты (без URL и без title)
        /// отфильтровываются.
        /// </para>
        /// </summary>
        /// <param name="results">Результаты веб-поиска</param>
        /// <param name="type">Тип источника: <c>wiki</c> / <c>web</c> / ...</param>
        /// <param name="maxCount">
        /// Максимум источников (default <see cref="DefaultMaxCount"/>).
        /// <c>0</c> или отрицательное — использовать default.
        /// </param>
        /// <returns>Список <see cref="ChatSourceDto"/> (порядок сохранён)</returns>
        public static IReadOnlyList<ChatSourceDto> Build(
            IReadOnlyList<RetrievedWebResult> results,
            string type,
            int maxCount = DefaultMaxCount)
        {
            if (results == null || results.Count == 0)
                return Array.Empty<ChatSourceDto>();

            if (maxCount <= 0)
                maxCount = DefaultMaxCount;

            var effectiveType = NormalizeType(type);
            var list = new List<ChatSourceDto>(Math.Min(results.Count, maxCount));

            // v1.6.1: дедупликация внутри самого билдера. DuckDuckGo HTML
            // иногда отдаёт несколько `<div class="result">` с одинаковой
            // ссылкой (например, разные сниппеты). Убираем повторы по
            // (Type|Url), чтобы не раздувать список.
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var result in results)
            {
                if (list.Count >= maxCount)
                    break;

                if (result == null)
                    continue;

                // Пропускаем совсем пустые результаты (без title и url).
                if (string.IsNullOrWhiteSpace(result.Title)
                    && string.IsNullOrWhiteSpace(result.Url))
                {
                    continue;
                }

                // Дедуп по URL (case-insensitive). Если URL пуст — пропускаем
                // дедуп (нечего сравнивать), но всё равно добавляем.
                if (!string.IsNullOrWhiteSpace(result.Url)
                    && !seenUrls.Add(result.Url.Trim()))
                {
                    continue;
                }

                list.Add(BuildItem(result, effectiveType));
            }

            return list;
        }

        /// <summary>
        /// Создаёт один citation (для <c>fetch_web_content</c> и
        /// <c>wikipedia_search</c>, где обычно возвращается 1 результат).
        ///
        /// <para>
        /// Возвращает <c>null</c>, если <paramref name="url"/> пуст — без URL
        /// citation бессмысленна (нельзя построить кликабельную ссылку).
        /// </para>
        /// </summary>
        /// <param name="title">Заголовок (может быть пустым — fallback на URL)</param>
        /// <param name="url">URL (обязателен)</param>
        /// <param name="snippet">Краткое описание (может быть null)</param>
        /// <param name="type">Тип источника</param>
        /// <returns>Citation или <c>null</c></returns>
        public static ChatSourceDto BuildSingle(
            string title,
            string url,
            string snippet,
            string type)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            return BuildItem(
                new RetrievedWebResult
                {
                    Title = title,
                    Url = url,
                    Snippet = snippet
                },
                NormalizeType(type));
        }

        /// <summary>
        /// Возвращает «человеческое» имя источника для UI:
        /// <c>title</c>, если задан; иначе — <c>url</c>; иначе — «(unknown)».
        /// </summary>
        /// <param name="title">Заголовок</param>
        /// <param name="url">URL</param>
        /// <returns>Label для UI</returns>
        public static string BuildLabel(string title, string url)
        {
            if (!string.IsNullOrWhiteSpace(title))
                return title.Trim();

            if (!string.IsNullOrWhiteSpace(url))
                return url.Trim();

            return "(unknown)";
        }

        // ============================================================
        // Внутренние
        // ============================================================

        /// <summary>
        /// Общая логика преобразования одного результата в citation.
        /// </summary>
        private static ChatSourceDto BuildItem(RetrievedWebResult result, string type)
        {
            return new ChatSourceDto
            {
                Type = type,
                Label = BuildLabel(result.Title, result.Url),
                Url = result.Url,
                DocumentPath = null,
                ChunkIndex = null,
                Score = null,
                // Переиспользуем TruncateSnippet из RagSourceBuilder
                // (одинаковая семантика: ≤ 200 символов, с «…»).
                Snippet = RagSourceBuilder.TruncateSnippet(result.Snippet)
            };
        }

        /// <summary>
        /// Нормализует тип: trim + lower. Если пусто — возвращает <c>"web"</c>
        /// (безопасный fallback).
        /// </summary>
        private static string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return "web";

            return type.Trim().ToLowerInvariant();
        }
    }
}