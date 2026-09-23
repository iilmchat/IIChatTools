using Prometheus;

namespace IIChatTools.Services.Metrics
{
    /// <summary>
    /// Реестр кастомных метрик Prometheus для IIChatTools.
    /// HELP-описания — на английском (стандарт Prometheus/Grafana).
    /// Статические поля создаются один раз при старте приложения.
    /// </summary>
    public static class AppMetrics
    {
        /// <summary>
        /// Счётчик вызовов инструментов. Labels: <c>tool_name</c>, <c>status</c>
        /// (Success / Error / Rejected / Expired).
        /// </summary>
        public static readonly Counter ToolExecutionsTotal = Prometheus.Metrics.CreateCounter(
            "iichattools_tool_executions_total",
            "Total number of tool executions.",
            new CounterConfiguration { LabelNames = new[] { "tool_name", "status" } });

        /// <summary>
        /// Гистограмма времени выполнения инструментов (в секундах).
        /// </summary>
        public static readonly Histogram ToolExecutionDurationSeconds = Prometheus.Metrics.CreateHistogram(
            "iichattools_tool_execution_duration_seconds",
            "Tool execution duration in seconds.",
            new HistogramConfiguration
            {
                LabelNames = new[] { "tool_name" },
                Buckets = new[] { 0.01, 0.05, 0.1, 0.5, 1.0, 2.0, 5.0, 10.0, 30.0, 60.0 }
            });

        /// <summary>
        /// Gauge — текущее число ожидающих подтверждений.
        /// Обновляется фоновым сервисом <c>MetricsRefreshBackgroundService</c>.
        /// </summary>
        public static readonly Gauge PendingApprovals = Prometheus.Metrics.CreateGauge(
            "iichattools_pending_approvals",
            "Number of pending approval actions.");

        /// <summary>
        /// Gauge — число активных пользователей.
        /// </summary>
        public static readonly Gauge ActiveUsers = Prometheus.Metrics.CreateGauge(
            "iichattools_active_users",
            "Number of active users.");

        /// <summary>
        /// Счётчик записей аудита. Labels: <c>status</c>.
        /// </summary>
        public static readonly Counter AuditEntriesTotal = Prometheus.Metrics.CreateCounter(
            "iichattools_audit_entries_total",
            "Total number of audit entries.",
            new CounterConfiguration { LabelNames = new[] { "status" } });

        /// <summary>
        /// Счётчик запросов к LM Studio. Labels: <c>status</c> (Success / Error / Timeout).
        /// </summary>
        public static readonly Counter LmStudioRequestsTotal = Prometheus.Metrics.CreateCounter(
            "iichattools_lmstudio_requests_total",
            "Total number of LM Studio API requests.",
            new CounterConfiguration { LabelNames = new[] { "status" } });
            
        /// <summary>
        /// Счётчик удалённых записей/файлов аудита (retention policy).
        /// Labels: <c>target</c> = "database" | "file".
        /// </summary>
        public static readonly Counter AuditCleanupTotal = Prometheus.Metrics.CreateCounter(
            "iichattools_audit_cleanup_total",
            "Total number of audit records/files deleted by retention policy.",
            new CounterConfiguration { LabelNames = new[] { "target" } });

        /// <summary>
        /// Счётчик удалённых чатов по retention policy.
        /// Labels: <c>reason</c> = "retention" | "manual" (для будущего).
        /// </summary>
        public static readonly Counter ChatCleanupTotal = Prometheus.Metrics.CreateCounter(
            "iichattools_chat_cleanup_total",
            "Total number of chats deleted by retention policy.",
            new CounterConfiguration { LabelNames = new[] { "reason" } });
    }
}