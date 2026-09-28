using System;

namespace IIChatTools.Services.DTO.Chat
{
    /// <summary>
    /// Источник, использованный LLM при генерации ответа (v1.6.0, KI-086).
    ///
    /// <para>
    /// Сериализуется в <c>ChatMessage.MetadataJson</c> как
    /// <c>{ "sources": [ {...}, {...} ] }</c>. UI рендерит блок «Источники»
    /// под ответом ассистента.
    /// </para>
    ///
    /// <para>
    /// Типы источников:
    /// <list type="bullet">
    ///   <item><c>rag</c> — чанк из RAG-индекса (my_rag_docs, project_docs, ...);</item>
    ///   <item><c>web</c> — веб-страница (v1.6.1+, пока не реализовано);</item>
    ///   <item><c>wiki</c> — статья Wikipedia (v1.6.1+, пока не реализовано).</item>
    /// </list>
    /// </para>
    /// </summary>
    public class ChatSourceDto
    {
        /// <summary>
        /// Тип источника: <c>rag</c> | <c>web</c> | <c>wiki</c>.
        /// На v1.6.0 реально используется только <c>rag</c>.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Человекочитаемое имя источника для UI.
        /// Для RAG — имя файла (например, <c>RULES.md</c>).
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Ссылка (для web/wiki). <c>null</c> для RAG.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Относительный путь документа (для RAG).
        /// Например: <c>chat-attachments/5/a1b2c3.pdf</c> или <c>docs/development/RULES.md</c>.
        /// </summary>
        public string DocumentPath { get; set; }

        /// <summary>
        /// Порядковый номер чанка в документе (0-based). <c>null</c> для web/wiki.
        /// </summary>
        public int? ChunkIndex { get; set; }

        /// <summary>
        /// Cosine similarity с запросом (0..1, чем выше — тем релевантнее).
        /// <c>null</c> для web/wiki.
        /// </summary>
        public float? Score { get; set; }

        /// <summary>
        /// Превью содержимого (≤ 200 символов) для отображения в UI.
        /// </summary>
        public string Snippet { get; set; }
    }
}