using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace IIChatTools.VisionOverlay
{
    /// <summary>
    /// Прозрачное always-on-top окно «on-screen indicator» Vision Agent.
    /// Отображает прогресс задачи и кнопку STOP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1.12.0 (KI-131, Ф6.7). См. DESIGN § 4.6.
    /// </para>
    /// <para>
    /// <b>Потокобезопасность:</b> методы <see cref="UpdateProgress"/> и
    /// <see cref="SetFinalStatus"/> вызываются из фонового потока pipe-сервера.
    /// Все обращения к WPF-элементам — через <c>Dispatcher.Invoke</c>.
    /// </para>
    /// </remarks>
    public partial class MainWindow : Window
    {
        /// <summary>Отступ от края рабочей области (px).</summary>
        private const double CornerMargin = 16;

        private readonly string _taskId;
        private int _maxSteps;

        /// <summary>
        /// Событие: пользователь нажал STOP (или ESC).
        /// App-хост обработает: отправит команду в pipe и закроет окно.
        /// </summary>
        public event Action StopRequested;

        /// <summary>
        /// Создаёт окно.
        /// </summary>
        /// <param name="taskId">ID задачи (для отображения).</param>
        /// <param name="maxSteps">Лимит шагов (для индикатора «N из M»). 0 — неизвестно.</param>
        public MainWindow(string taskId, int maxSteps)
        {
            InitializeComponent();

            _taskId = taskId ?? "(unknown)";
            _maxSteps = maxSteps;

            TaskIdText.Text = "задача " + _taskId;

            // Позиция: правый верхний угол рабочей области.
            Loaded += OnLoaded;
        }

        /// <summary>
        /// KI-149 (v1.12.x): устанавливает extended window style
        /// <c>WS_EX_NOACTIVATE</c> + <c>WS_EX_TOOLWINDOW</c>.
        /// <para>
        /// Overlay не должен перехватывать фокус при клике по кнопке STOP —
        /// иначе whitelist процессов в API отклоняет следующее действие
        /// (процесс <c>IIChatTools.VisionOverlay</c> не в списке разрешённых).
        /// </para>
        /// <para>
        /// <c>ShowActivated="False"</c> в XAML + <c>WS_EX_NOACTIVATE</c> здесь
        /// дают двойную защиту: окно не активируется ни при старте, ни при клике.
        /// </para>
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var exStyle = OverlayWin32.GetWindowLongPtr(hwnd, OverlayWin32.GWL_EXSTYLE);
                exStyle |= OverlayWin32.WS_EX_NOACTIVATE | OverlayWin32.WS_EX_TOOLWINDOW;
                OverlayWin32.SetWindowLongPtr(hwnd, OverlayWin32.GWL_EXSTYLE, exStyle);
            }
            catch (Exception ex)
            {
                // Не критично: overlay продолжит работать, но может перехватывать фокус
                // при клике → whitelist-проверка в API отклонит следующее действие.
                Console.WriteLine($"[overlay] WS_EX_NOACTIVATE не установлен: {ex.Message}");
            }
        }

        /// <summary>
        /// Позиционирует окно в правом верхнем углу рабочей области.
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - CornerMargin;
            Top = work.Top + CornerMargin;
        }

        /// <summary>
        /// Обновляет прогресс: «Шаг N из M» + текущее действие.
        /// Вызывается из pipe-сервера — потокобезопасно.
        /// </summary>
        /// <param name="stepIndex">Номер шага (1-based).</param>
        /// <param name="maxSteps">Всего шагов.</param>
        /// <param name="action">Описание действия (короткое).</param>
        public void UpdateProgress(int stepIndex, int maxSteps, string action)
        {
            Dispatcher.Invoke(() =>
            {
                if (maxSteps > 0)
                {
                    _maxSteps = maxSteps;
                    ProgressText.Text = $"Шаг {stepIndex} из {maxSteps}";
                }
                else
                {
                    ProgressText.Text = $"Шаг {stepIndex}";
                }

                ActionText.Text = string.IsNullOrWhiteSpace(action)
                    ? "(выполняется)"
                    : action;

                StatusDot.Fill = (Brush)FindResource("OverlayAccent");
            });
        }

        /// <summary>
        /// Устанавливает финальный статус: «Готово» (зелёный) / «Ошибка» (красный).
        /// Вызывается из pipe-сервера — потокобезопасно.
        /// </summary>
        public void SetFinalStatus(string summary, bool success)
        {
            Dispatcher.Invoke(() =>
            {
                ActionText.Text = string.IsNullOrWhiteSpace(summary)
                    ? (success ? "Готово" : "Ошибка")
                    : summary;

                StatusDot.Fill = (Brush)FindResource(success ? "OverlayOk" : "OverlayStop");
            });
        }

        /// <summary>
        /// Клик по кнопке STOP.
        /// </summary>
        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            StopRequested?.Invoke();
        }

        /// <summary>
        /// Перетаскивание окна за «шапку».
        /// </summary>
        private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch { /* DragMove бросает, если кнопка уже отпущена — не критично */ }
        }

        /// <summary>
        /// ESC = STOP.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                StopRequested?.Invoke();
                return;
            }
            base.OnKeyDown(e);
        }
    }
}