using System;
using System.Windows;
using System.Windows.Input;
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