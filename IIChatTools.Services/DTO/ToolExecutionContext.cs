using System.Threading;

namespace IIChatTools.Services.DTO
{
    /// <summary>
    /// Контекст выполнения инструмента (передаётся на каждый вызов).
    /// </summary>
    public class ToolExecutionContext
    {
        /// <summary>
        /// Идентификатор пользователя, инициировавшего вызов.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Абсолютный путь к рабочему пространству пользователя.
        /// </summary>
        public string WorkspaceRoot { get; set; }

        /// <summary>
        /// IP-адрес клиента (для аудита).
        /// </summary>
        public string ClientIp { get; set; }

        /// <summary>
        /// Токен отмены операции.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }
    }
}