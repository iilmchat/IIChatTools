using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IIChatTools.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IIChatTools.Services.Implementation
{
    /// <summary>
    /// Реализация безопасного запуска внешних процессов.
    /// Использует ArgumentList (а не Arguments) для предотвращения инъекций.
    /// </summary>
    public class ProcessRunner : IProcessRunner
    {
        private readonly ILogger<ProcessRunner> _logger;

        /// <summary>
        /// Создаёт экземпляр исполнителя.
        /// </summary>
        /// <param name="logger">Логгер</param>
        public ProcessRunner(ILogger<ProcessRunner> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> argumentList,
            string workingDirectory,
            int timeoutSeconds,
            long maxOutputBytes,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Имя исполняемого файла обязательно", nameof(fileName));

            if (timeoutSeconds <= 0) timeoutSeconds = 30;
            if (maxOutputBytes <= 0) maxOutputBytes = 1024 * 1024;

            var sw = Stopwatch.StartNew();
            var result = new ProcessResult();

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            // Безопасное добавление аргументов без конкатенации в строку
            if (argumentList != null)
            {
                foreach (var arg in argumentList)
                    startInfo.ArgumentList.Add(arg ?? string.Empty);
            }

            try
            {
                using var process = new Process { StartInfo = startInfo };
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null && stdoutBuilder.Length < maxOutputBytes)
                    {
                        stdoutBuilder.AppendLine(e.Data);
                        if (stdoutBuilder.Length >= maxOutputBytes)
                            result.Truncated = true;
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null && stderrBuilder.Length < maxOutputBytes)
                    {
                        stderrBuilder.AppendLine(e.Data);
                        if (stderrBuilder.Length >= maxOutputBytes)
                            result.Truncated = true;
                    }
                };

                if (!process.Start())
                    throw new InvalidOperationException($"Не удалось запустить процесс '{fileName}'");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    result.TimedOut = timeoutCts.IsCancellationRequested;

                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogWarning(killEx, "Не удалось завершить процесс {FileName}", fileName);
                    }
                }

                result.ExitCode = process.HasExited ? process.ExitCode : -1;
                result.StdOut = Truncate(stdoutBuilder.ToString(), maxOutputBytes);
                result.StdErr = Truncate(stderrBuilder.ToString(), maxOutputBytes);
                result.DurationMs = sw.ElapsedMilliseconds;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения процесса {FileName}", fileName);
                result.ExitCode = -1;
                result.StdErr = ex.Message;
                result.DurationMs = sw.ElapsedMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// Обрезает строку до указанного размера в байтах.
        /// </summary>
        /// <param name="value">Исходная строка</param>
        /// <param name="maxBytes">Максимум байт</param>
        /// <returns>Обрезанная строка</returns>
        private static string Truncate(string value, long maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            // Обрезаем посимвольно, пока не уложимся в лимит
            var sb = new StringBuilder();
            long total = 0;
            foreach (var ch in value)
            {
                var size = Encoding.UTF8.GetByteCount(ch.ToString());
                if (total + size > maxBytes) break;
                sb.Append(ch);
                total += size;
            }
            return sb.ToString() + "\n... [вывод обрезан]";
        }
    }
}