using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IIChatTools.Services.Extensions
{
    /// <summary>
    /// Расширения для System.Diagnostics.Process.
    /// Восполняет отсутствие WaitForExitAsync в netstandard2.1.
    /// </summary>
    public static class ProcessExtensions
    {
        /// <summary>
        /// Асинхронно ожидает завершения процесса с поддержкой отмены.
        /// </summary>
        /// <param name="process">Процесс</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Задача, завершающаяся после выхода процесса</returns>
        /// <exception cref="ArgumentNullException">Если process равен null</exception>
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            if (process.HasExited)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnExited(object sender, EventArgs e) => tcs.TrySetResult(null);

            process.EnableRaisingEvents = true;
            process.Exited += OnExited;

            // Если процесс уже успел завершиться между проверкой и подпиской
            if (process.HasExited)
            {
                process.Exited -= OnExited;
                tcs.TrySetResult(null);
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    process.Exited -= OnExited;
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            return tcs.Task;
        }

        /// <summary>
        /// Безопасно завершает процесс.
        /// В netstandard2.1 нет Kill(entireProcessTree), поэтому просто Kill().
        /// </summary>
        /// <param name="process">Процесс</param>
        /// <exception cref="ArgumentNullException">Если process равен null</exception>
        public static void KillSafe(this Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Игнорируем — процесс мог уже завершиться
            }
        }
    }
}