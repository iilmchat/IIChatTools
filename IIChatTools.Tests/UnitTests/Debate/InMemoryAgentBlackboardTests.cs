using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IIChatTools.Services.Implementation.Debate;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IIChatTools.Tests.UnitTests.Debate
{
    /// <summary>
    /// Unit-тесты <see cref="InMemoryAgentBlackboard"/>
    /// (v2.0.0, KI-126, Фаза 3, Шаг 3J).
    ///
    /// <para>
    /// Покрываем: GetOrCreate / Set / Get / Snapshot / Clear + потокобезопасность
    /// + валидацию. TTL-cleanup (Timer 5 мин) — не тестируется (долгий);
    /// проверяется через <c>Dispose()</c> (остановка Timer).
    /// </para>
    /// </summary>
    public class InMemoryAgentBlackboardTests
    {
        private static InMemoryAgentBlackboard Create() =>
            new InMemoryAgentBlackboard(NullLogger<InMemoryAgentBlackboard>.Instance);

        // ============ GetOrCreate ============

        [Fact]
        public void GetOrCreate_NewSession_ReturnsEmptyObject()
        {
            using var bb = Create();

            var obj = bb.GetOrCreate(1);

            Assert.NotNull(obj);
            Assert.Empty(obj);
        }

        [Fact]
        public void GetOrCreate_ExistingSession_ReturnsSameInstance()
        {
            using var bb = Create();

            var first = bb.GetOrCreate(42);
            first["key"] = "value";

            var second = bb.GetOrCreate(42);

            Assert.Same(first, second);
            Assert.Equal("value", second["key"]?.ToString());
        }

        [Fact]
        public void GetOrCreate_ZeroSessionId_Throws()
        {
            using var bb = Create();

            Assert.Throws<ArgumentOutOfRangeException>(() => bb.GetOrCreate(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => bb.GetOrCreate(-5));
        }

        // ============ Set / Get ============

        [Fact]
        public void Set_Get_RoundTrip()
        {
            using var bb = Create();

            bb.Set(1, "subtask.1.status", "completed");
            bb.Set(1, "subtask.1.agent", "web_agent");
            bb.Set(1, "subtask.1.durationMs", 12345);

            Assert.Equal("completed", bb.Get(1, "subtask.1.status")?.ToString());
            Assert.Equal("web_agent", bb.Get(1, "subtask.1.agent")?.ToString());
            Assert.Equal(12345, bb.Get(1, "subtask.1.durationMs")?.Value<int>());
        }

        [Fact]
        public void Get_NonExistentKey_ReturnsNull()
        {
            using var bb = Create();

            bb.Set(1, "existing", "value");

            Assert.Null(bb.Get(1, "missing"));
        }

        [Fact]
        public void Get_NonExistentSession_ReturnsNull()
        {
            using var bb = Create();

            Assert.Null(bb.Get(999, "any"));
        }

        [Fact]
        public void Get_EmptyKey_ReturnsNull()
        {
            using var bb = Create();

            bb.Set(1, "x", "y");

            Assert.Null(bb.Get(1, ""));
            Assert.Null(bb.Get(1, "   "));
        }

        [Fact]
        public void Set_EmptyKey_Throws()
        {
            using var bb = Create();

            Assert.Throws<ArgumentException>(() => bb.Set(1, "", "value"));
            Assert.Throws<ArgumentException>(() => bb.Set(1, "   ", "value"));
        }

        [Fact]
        public void Set_OverwritesExistingKey()
        {
            using var bb = Create();

            bb.Set(1, "status", "pending");
            bb.Set(1, "status", "completed");

            Assert.Equal("completed", bb.Get(1, "status")?.ToString());
        }

        // ============ Snapshot ============

        [Fact]
        public void Snapshot_ReturnsDeepClone()
        {
            using var bb = Create();

            bb.Set(1, "meta.parallel", true);
            var nested = new JObject { ["x"] = 10 };
            bb.Set(1, "nested", nested);

            var snapshot = bb.Snapshot(1);

            Assert.Equal(true, snapshot["meta.parallel"]?.Value<bool>());
            Assert.Equal(10, snapshot["nested"]?["x"]?.Value<int>());

            // Мутация snapshot не влияет на оригинал.
            snapshot["newKey"] = "polluted";
            snapshot["nested"]["x"] = 999;

            Assert.Null(bb.Get(1, "newKey"));
            Assert.Equal(10, bb.Get(1, "nested")?["x"]?.Value<int>());
        }

        [Fact]
        public void Snapshot_NonExistentSession_ReturnsEmptyObject()
        {
            using var bb = Create();

            var snapshot = bb.Snapshot(999);

            Assert.NotNull(snapshot);
            Assert.Empty(snapshot);
        }

        // ============ Clear ============

        [Fact]
        public void Clear_RemovesSession()
        {
            using var bb = Create();

            bb.Set(1, "key", "value");
            Assert.NotNull(bb.Get(1, "key"));

            bb.Clear(1);

            Assert.Null(bb.Get(1, "key"));
            // Snapshot после Clear возвращает пустой объект (не падает).
            Assert.Empty(bb.Snapshot(1));
        }

        [Fact]
        public void Clear_NonExistentSession_NoOp()
        {
            using var bb = Create();

            // Не должно бросить.
            bb.Clear(999);
        }

        [Fact]
        public void Clear_ZeroSessionId_NoOp()
        {
            using var bb = Create();

            // Не должно бросить (тихо игнорируем).
            bb.Clear(0);
            bb.Clear(-1);
        }

        // ============ Потокобезопасность ============

        [Fact]
        public async Task ConcurrentSet_NoRace()
        {
            using var bb = Create();

            const int count = 100;
            var tasks = new List<Task>();

            for (var i = 0; i < count; i++)
            {
                var idx = i;
                tasks.Add(Task.Run(() =>
                {
                    bb.Set(1, $"subtask.{idx}.status", "completed");
                }));
            }

            await Task.WhenAll(tasks);

            var snapshot = bb.Snapshot(1);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal("completed",
                    snapshot[$"subtask.{i}.status"]?.ToString());
            }
        }

        // ============ Dispose ============

        [Fact]
        public void Dispose_ClearsBoards_AndStopsTimer()
        {
            var bb = Create();

            bb.Set(1, "key", "value");
            bb.Dispose();

            // Повторный Dispose — no-op (защита от double-dispose).
            bb.Dispose();

            // После Dispose Get может вернуть значение (словарь уже очищен —
            // но нет строгого контракта). Проверяем только, что Dispose не падает.
            // По факту: Clear() в Dispose удаляет всё, Snapshot вернёт пустой.
            Assert.Empty(bb.Snapshot(1));
        }
    }
}