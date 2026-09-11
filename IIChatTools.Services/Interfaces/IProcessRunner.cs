using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// Результат выполнения внешнего процесса.
    /// </summary>
    public class ProcessResult
    {
        /// <summary>
        /// Код возврата процесса.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Содержимое стандартного вывода.
        /// </summary>
        public string StdOut { get; set; }

        /// <summary>
        /// Содержимое стандартного потока ошибок.
        /// </summary>
        public string StdErr { get; set; }

        /// <summary>
        /// Признак истечения таймаута.
        /// </summary>
        public bool TimedOut { get; set; }

        /// <summary>
        /// Длительность выполнения в миллисекундах.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Признак превышения лимита размера вывода.
        /// </summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Безопасный запуск внешних процессов с ограничениями и экранированием.
    /// </summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// Запускает процесс и возвращает результат.
        /// </summary>
        /// <param name="fileName">Исполняемый файл</param>
        /// <param name="argumentList">Аргументы (список, без конкатенации строк)</param>
        /// <param name="workingDirectory">Рабочая директория (обязательно внутри workspace)</param>
        /// <param name="timeoutSeconds">Таймаут в секундах</param>
        /// <param name="maxOutputBytes">Максимальный размер вывода в байтах</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Результат выполнения процесса</returns>
        Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> argumentList,
            string workingDirectory,
            int timeoutSeconds,
            long maxOutputBytes,
            CancellationToken cancellationToken);
    }
}