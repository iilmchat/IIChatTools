using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IIChatTools.VisionOverlay.Ipc;

namespace IIChatTools.VisionOverlay
{
    /// <summary>
    /// Точка входа WPF-приложения overlay.
    /// Парсит аргументы командной строки, стартует NamedPipe-сервер,
    /// показывает <see cref="MainWindow"/> и связывает IPC-команды с UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6, § 6.4.
    /// </para>
    /// <para>
    /// <b>Аргументы командной строки:</b>
    /// <list type="bullet">
    ///   <item><description><c>--task-id=vt_8f2a</c> — обязательный. Имя pipe = <c>iichattools-vision-overlay-{taskId}</c>.</description></item>
    ///   <item><description><c>--max-steps=15</c> — опциональный. Лимит шагов для индикатора «N из M».</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Жизненный цикл:</b> окно показывается → стартует pipe-сервер в фоне →
    /// при получении команды <c>close</c> или закрытии окна пользователем → shutdown.
    /// </para>
    /// </remarks>
    public partial class App : Application
    {
        private OverlayArgs _args;
        private OverlayPipeServer _pipeServer;
        private MainWindow _mainWindow;

        /// <summary>
        /// Точка входа WPF. Парсит аргументы, запускает окно и pipe-сервер.
        /// </summary>
        /// <param name="e">Аргументы запуска WPF.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Парсинг аргументов.
            _args = OverlayArgs.Parse(e.Args);
            if (_args == null || string.IsNullOrWhiteSpace(_args.TaskId))
            {
                MessageBox.Show(
                    "IIChatTools.VisionOverlay: не задан --task-id.\n" +
                    "Этот процесс запускается автоматически из IIChatTools.API. " +
                    "Ручной запуск без --task-id невозможен.",
                    "VisionOverlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // 2. Создаём окно (вручную — StartupUri не используем).
            _mainWindow = new MainWindow(_args.TaskId, _args.MaxSteps);
            _mainWindow.StopRequested += OnStopRequested;
            _mainWindow.Closed += OnMainWindowClosed;
            MainWindow = _mainWindow;
            _mainWindow.Show();

            // 3. Стартуем pipe-сервер (асинхронно, не блокируем UI).
            _pipeServer = new OverlayPipeServer(_args.TaskId, _mainWindow);
            _ = _pipeServer.RunAsync();
        }

        /// <summary>
        /// Пользователь нажал STOP (или ESC) в overlay.
        /// Отправляет команду <c>stop</c> в pipe (если клиент ещё подключён)
        /// и закрывает окно.
        /// </summary>
        private void OnStopRequested()
        {
            try { _pipeServer?.TrySendStop(); }
            catch { /* клиент мог уже отключиться — не критично */ }

            try { _mainWindow?.Close(); }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Окно закрылось (пользователем, из OnStopRequested, или через IPC close).
        /// Корректно гасим pipe-сервер.
        /// </summary>
        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            try { _pipeServer?.Dispose(); }
            catch { /* ignore */ }

            // ShutdownMode=OnMainWindowClose — WPF сам вызовет Shutdown.
        }
    }

    /// <summary>
    /// Разобранные аргументы командной строки overlay.
    /// </summary>
    internal sealed class OverlayArgs
    {
        /// <summary>ID задачи (обязателен). Используется в имени pipe.</summary>
        public string TaskId { get; private set; }

        /// <summary>Лимит шагов (для индикатора «N из M»). 0 — неизвестно.</summary>
        public int MaxSteps { get; private set; }

        /// <summary>
        /// Разбирает аргументы вида <c>--task-id=...</c> и <c>--max-steps=...</c>.
        /// Возвращает <c>null</c>, если task-id отсутствует.
        /// </summary>
        public static OverlayArgs Parse(string[] args)
        {
            if (args == null) return null;

            var result = new OverlayArgs();

            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var idx = raw.IndexOf('=');
                if (idx <= 0) continue;

                var key = raw.Substring(0, idx).Trim().ToLowerInvariant();
                var value = raw.Substring(idx + 1).Trim();

                switch (key)
                {
                    case "--task-id":
                        result.TaskId = value;
                        break;
                    case "--max-steps":
                        if (int.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out var ms))
                        {
                            result.MaxSteps = ms;
                        }
                        break;
                }
            }

            return string.IsNullOrWhiteSpace(result.TaskId) ? null : result;
        }
    }
}