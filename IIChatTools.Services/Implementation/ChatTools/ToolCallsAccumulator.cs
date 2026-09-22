using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IIChatTools.Services.Implementation.ChatTools
{
    /// <summary>
    /// Аккумулятор инкрементальных tool_calls из SSE-стрима LM Studio.
    ///
    /// Формат чанков (OpenAI):
    /// <code>
    /// delta.tool_calls = [
    ///   { index: 0, id: "call_1", function: { name: "list_dir", arguments: "{\"pa" } }
    /// ]
    /// ... несколько чанков ...
    /// delta.tool_calls = [{ index: 0, function: { arguments: "th\": \".\"}" } }]
    /// </code>
    ///
    /// Аккумулятор складывает arguments по <c>index</c>, name/id — берутся из первого
    /// чанка для каждого index. В конце возвращается список завершённых вызовов.
    /// </summary>
    internal sealed class ToolCallsAccumulator
    {
        /// <summary>Состояние одного вызова (по index).</summary>
        private sealed class PendingCall
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
        }

        private readonly Dictionary<int, PendingCall> _calls = new Dictionary<int, PendingCall>();

        /// <summary>
        /// Признак: был ли хотя бы один tool_call в стриме.
        /// </summary>
        public bool HasToolCalls => _calls.Count > 0;

        /// <summary>
        /// Добавляет один delta-чанк tool_call.
        /// </summary>
        /// <param name="deltaToolCall">
        /// JObject из <c>delta.tool_calls[0]</c>.
        /// Может содержать: <c>index</c>, <c>id</c>, <c>function.name</c>, <c>function.arguments</c>.
        /// </param>
        public void Add(JObject deltaToolCall)
        {
            if (deltaToolCall == null) return;

            // index обязателен, но на всякий случай дефолтим в 0
            var index = deltaToolCall["index"]?.Value<int>() ?? 0;

            if (!_calls.TryGetValue(index, out var pending))
            {
                pending = new PendingCall();
                _calls[index] = pending;
            }

            // id и name приходят только в первом чанке для данного index
            if (string.IsNullOrEmpty(pending.Id))
            {
                var id = deltaToolCall["id"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                    pending.Id = id;
            }

            var function = deltaToolCall["function"] as JObject;
            if (function != null)
            {
                if (string.IsNullOrEmpty(pending.Name))
                {
                    var name = function["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        pending.Name = name;
                }

                // arguments приходят инкрементально — конкатенируем
                var argsChunk = function["arguments"]?.ToString();
                if (!string.IsNullOrEmpty(argsChunk))
                    pending.Arguments.Append(argsChunk);
            }
        }

        /// <summary>
        /// Возвращает список завершённых вызовов (сортировка по index).
        /// </summary>
        /// <returns>Список JObject формата OpenAI: <c>{id, type:"function", function:{name, arguments}}</c></returns>
        public List<JObject> BuildCompletedCalls()
        {
            var result = new List<JObject>(_calls.Count);

            var indices = new List<int>(_calls.Keys);
            indices.Sort();

            foreach (var index in indices)
            {
                var pending = _calls[index];

                // Пропускаем «пустые» вызовы (без name)
                if (string.IsNullOrEmpty(pending.Name))
                    continue;

                var args = pending.Arguments.Length > 0
                    ? pending.Arguments.ToString()
                    : "{}";

                result.Add(new JObject
                {
                    ["id"] = pending.Id ?? string.Empty,
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = pending.Name,
                        ["arguments"] = args
                    }
                });
            }

            return result;
        }

        /// <summary>
        /// Сбрасывает состояние (для повторного использования в следующей итерации).
        /// </summary>
        public void Reset()
        {
            _calls.Clear();
        }
    }
}