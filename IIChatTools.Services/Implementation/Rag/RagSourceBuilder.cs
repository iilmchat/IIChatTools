using System;
using System.Collections.Generic;
using System.IO;
using IIChatTools.Services.DTO.Chat;
using IIChatTools.Services.DTO.Rag;

namespace IIChatTools.Services.Implementation.Rag
{
    /// <summary>
    /// Хелпер сборки citations из результатов RAG-поиска
    /// (v1.6.0, KI-086).
    ///
    /// <para>
    /// Преобразует <see cref="RetrievedChunkDto"/> → <see cref="ChatSourceDto"/>
    /// для отображения в UI-блоке «Источники» под ответом ассистента.
    /// Используется тремя RAG-инструментами
    /// (<c>search_knowledge_base</c>, <c>search_chat_history</c>,
    /// <c>search_workspace</c>) — во избежание дублирования логики.
    /// </para>
    ///
    /// <para>
    /// Тип источника — всегда <c>rag</c> (web/wiki — v1.6.1+).
    /// </para>
    /// </summary>
    public static class RagSourceBuilder
    {
        /// <summary>
        /// Максимальная длина snippet в UI (символов).
        /// Симметрично <see cref="ChatSourceDto.Snippet"/>.
        /// </summary>
        public const int SnippetMaxLength = 200;

        /// <summary>
        /// Преобразует список найденных чанков в список источников.
        /// </summary>
        /// <param name="chunks">
        /// Чанки из <c>IRetrievalService.SearchAsync</c>.
        /// Может быть null / пустым — возвращается пустой список.
        /// </param>
        /// <returns>Список <see cref="ChatSourceDto"/> (порядок сохранён)</returns>
        public static IReadOnlyList<ChatSourceDto> Build(IReadOnlyList<RetrievedChunkDto> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                return Array.Empty<ChatSourceDto>();

            var result = new List<ChatSourceDto>(chunks.Count);
            foreach (var chunk in chunks)
            {
                result.Add(new ChatSourceDto
                {
                    Type = "rag",
                    Label = BuildLabel(chunk.DocumentPath),
                    Url = null,
                    DocumentPath = chunk.DocumentPath,
                    ChunkIndex = chunk.ChunkIndex,
                    Score = chunk.Score,
                    Snippet = TruncateSnippet(chunk.Text)
                });
            }
            return result;
        }

        /// <summary>
        /// Возвращает «человеческое» имя источника для UI:
        /// имя файла из пути (с fallback на полный путь).
        ///
        /// <para>
        /// <b>Кросс-платформенность:</b> <see cref="Path.GetFileName(string)"/>
        /// на Linux распознаёт только <c>/</c> как разделитель, а на Windows —
        /// и <c>/</c>, и <c>\</c>. Поэтому абсолютный Windows-путь
        /// (<c>C:\Projects\...\RULES.md</c>) на Linux вернёт всю строку целиком.
        /// </para>
        ///
        /// <para>
        /// Решение: нормализуем оба разделителя в <c>/</c> перед вызовом
        /// <see cref="Path.GetFileName(string)"/>. На Windows это тоже
        /// безопасно (Windows понимает оба разделителя). Тот же паттерн,
        /// что в <c>PathHelper</c> (KI-040).
        /// </para>
        /// </summary>
        /// <param name="documentPath">Относительный путь или URL</param>
        /// <returns>Label для UI</returns>
        public static string BuildLabel(string documentPath)
        {
            if (string.IsNullOrWhiteSpace(documentPath))
                return "(unknown)";

            try
            {
                // Унификация разделителей: \ → /. Иначе на Linux
                // Path.GetFileName не распознает Windows-путь.
                var normalized = documentPath.Replace('\\', '/');
                var name = Path.GetFileName(normalized);
                return string.IsNullOrWhiteSpace(name) ? documentPath : name;
            }
            catch
            {
                // Невалидный путь (напр., URL с запрещёнными символами) — как есть.
                return documentPath;
            }
        }

        /// <summary>
        /// Обрезает текст до <see cref="SnippetMaxLength"/> символов,
        /// добавляя многоточие (если текст был обрезан).
        /// </summary>
        /// <param name="text">Полный текст чанка (может быть null)</param>
        /// <param name="maxLength">Максимум символов</param>
        /// <returns>Обрезанный текст или пустая строка</returns>
        public static string TruncateSnippet(string text, int maxLength = SnippetMaxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength).TrimEnd() + "…";
        }
    }
}