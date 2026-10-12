using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Interfaces
{
    /// <summary>
    /// v2.0.0 (KI-126, Фаза 3): общее разделяемое состояние (Blackboard)
    /// для мультиагентной сессии Orchestrator-Worker.
    ///
    /// <para>
    /// Blackboard — это JSON-документ, привязанный к <c>sessionId</c>
    /// (ID сессии <c>AgentDebateSession</c>). Orchestrator пишет туда
    /// метаданные и результаты подзадач, Workers могут читать результаты
    /// других Workers (если <c>parallel = false</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Lifetime:</b> Singleton. In-memory реализация (ConcurrentDictionary)
    /// + Cleanup Timer (TTL 30 мин, KI-043). Persistence — на уровне
    /// <c>OrchestrateTaskTool</c> через <c>AgentDebateRound.ActorOutput</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Схема ключей</b> (DESIGN_ORCHESTRATOR_WORKER § 4.4):
    /// <list type="bullet">
    ///   <item><c>subtask.{id}.status</c> — <c>pending</c> / <c>running</c> / <c>completed</c> / <c>failed</c> / <c>rejected</c>;</item>
    ///   <item><c>subtask.{id}.agent</c> — имя агента-исполнителя;</item>
    ///   <item><c>subtask.{id}.task</c> — текст подзадачи;</item>
    ///   <item><c>subtask.{id}.result</c> — <c>finalAnswer</c> от Worker'а;</item>
    ///   <item><c>subtask.{id}.error</c> — сообщение об ошибке (при <c>failed</c>);</item>
    ///   <item><c>subtask.{id}.durationMs</c> — длительность;</item>
    ///   <item><c>meta.parallel</c> — <c>bool</c>;</item>
    ///   <item><c>meta.startedAt</c> — ISO timestamp.</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IAgentBlackboard
    {
        /// <summary>
        /// Возвращает существующий Blackboard для сессии или создаёт новый.
        /// Идемпотентно — повторный вызов возвращает тот же объект.
        /// </summary>
        /// <param name="sessionId">ID сессии (положительный).</param>
        /// <returns>JObject — mutable shared state.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">Если <paramref name="sessionId"/> ≤ 0.</exception>
        JObject GetOrCreate(int sessionId);

        /// <summary>
        /// Устанавливает значение по ключу. Создаёт Blackboard при необходимости.
        /// </summary>
        /// <param name="sessionId">ID сессии.</param>
        /// <param name="key">Ключ (dot-нотация: <c>subtask.1.status</c>).</param>
        /// <param name="value">Значение (JToken).</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если <paramref name="sessionId"/> ≤ 0.</exception>
        /// <exception cref="System.ArgumentException">Если <paramref name="key"/> пустой.</exception>
        void Set(int sessionId, string key, JToken value);

        /// <summary>
        /// Возвращает значение по ключу (или <c>null</c>, если ключа нет).
        /// </summary>
        /// <param name="sessionId">ID сессии.</param>
        /// <param name="key">Ключ.</param>
        /// <returns>JToken или <c>null</c>.</returns>
        JToken Get(int sessionId, string key);

        /// <summary>
        /// Возвращает глубокую копию Blackboard на текущий момент.
        /// Мутации над копией не влияют на оригинал.
        /// </summary>
        /// <param name="sessionId">ID сессии.</param>
        /// <returns>
        /// JObject — deep clone. <b>Пустой</b> JObject, если сессии нет
        /// (не <c>null</c> — упрощает вызывающему коду).
        /// </returns>
        JObject Snapshot(int sessionId);

        /// <summary>
        /// Удаляет Blackboard сессии. No-op, если сессии нет.
        /// Вызывается в <c>finally</c> OrchestrateTaskTool.
        /// </summary>
        /// <param name="sessionId">ID сессии.</param>
        void Clear(int sessionId);
    }
}