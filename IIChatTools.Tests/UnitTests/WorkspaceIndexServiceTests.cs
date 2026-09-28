using System;
using System.IO;
using System.Threading.Tasks;
using IIChatTools.Data;
using IIChatTools.Services.DTO.Rag;
using IIChatTools.Services.Implementation.Rag;
using IIChatTools.Services.Implementation.Rag.Parsers;
using IIChatTools.Services.Interfaces;
using IIChatTools.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IIChatTools.Tests.UnitTests
{
    /// <summary>
    /// Unit-тесты <see cref="WorkspaceIndexService"/>
    /// (v1.5.0, KI-083, Шаг 7D.1).
    ///
    /// <para>
    /// Сервис — <b>Singleton</b>, фоновая индексация через <c>Task.Run</c> +
    /// <see cref="IServiceScopeFactory"/>. В тестах собираем реальный
    /// <see cref="ServiceCollection"/>, регистрируем fake-ingestion + fake-workspace,
    /// в качестве БД — InMemory с явным <see cref="InMemoryDatabaseRoot"/>
    /// (иначе разные scope не видят данные).
    /// </para>
    ///
    /// <para>
    /// <b>Правило § 4.34</b> — после правки интерфейса проверяем fake-заглушки
    /// (FakeIngestionService расширен <c>ClearForUserCalls</c>).
    /// </para>
    /// </summary>
    public class WorkspaceIndexServiceTests
    {
        // ============================================================
        // Хелперы
        // ============================================================

        /// <summary>
        /// Создаёт временный корень workspace + наполняет его файлами.
        /// Возвращает путь; вызывающий должен удалить в finally.
        /// </summary>
        /// <param name="userId">ID пользователя (для подпапки users/{userId})</param>
        /// <param name="files">
        /// Файлы: (относительный путь внутри users/{userId}, содержимое).
        /// Служебные подпапки (<c>chat-attachments/</c>, <c>logs/</c> и т.п.)
        /// тест создаёт явно.
        /// </param>
        private static string CreateTempWorkspace(
            int userId,
            params (string Path, string Content)[] files)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "iichattools_wsidx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var userDir = Path.Combine(root, "users", userId.ToString());
            Directory.CreateDirectory(userDir);

            foreach (var (rel, content) in files)
            {
                var full = Path.Combine(userDir, rel.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(full, content);
            }

            return root;
        }

        /// <summary>
        /// Собирает DI-контейнер с реальным <see cref="WorkspaceIndexService"/>
        /// + fake-зависимостями и InMemory-БД.
        ///
        /// <para>
        /// <b>Важно (RULES § 4.42):</b> <see cref="InMemoryDatabaseRoot"/>
        /// регистрируется как Singleton — без явного root EF Core создаёт
        /// свой root на каждый scope → данные между scope НЕ видны.
        /// </para>
        /// </summary>
        private static (ServiceProvider Provider,
                        IWorkspaceIndexService Service,
                        FakeIngestionService Ingestion,
                        string TempRoot)
            CreateService(params (string Path, string Content)[] files)
        {
            const int userId = 1;
            var tempRoot = CreateTempWorkspace(userId, files);

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug());

            // InMemoryDatabaseRoot — Singleton (RULES § 4.42).
            // ВАЖНО: и root, и ИМЯ БД вычисляются ДО лямбды.
            // AddDbContext выполняет lambda на каждый scope. Если вычислять
            // имя внутри lambda — каждый scope получит НОВОЕ имя, и scope
            // не будут видеть данные друг друга (даже с общим root).
            var inMemoryRoot = new InMemoryDatabaseRoot();
            var dbName = "wsidx_" + Guid.NewGuid().ToString("N");
            services.AddSingleton(inMemoryRoot);

            services.AddDbContext<AppDbContext>(opt =>
                opt.UseInMemoryDatabase(dbName, inMemoryRoot));

            // Реальные сервисы, где это важно:
            // - IUserSettingsService: тривиальный, работает с БД (End-to-end SetAsync → GetBoolAsync).
            // - IRagDocumentParserRegistry: Singleton, источник списка расширений.
            services.AddScoped<IUserSettingsService, IIChatTools.Services.Implementation.UserSettingsService>();
            services.AddSingleton<IRagDocumentParserRegistry>(
                new RagDocumentParserRegistry(new IRagDocumentParser[] { new PlainTextParser() }));

            // Fake-зависимости:
            // - FakeIngestionService (не делает реальную индексацию).
            // - FakeWorkspaceResolver (возвращает temp-root).
            var fakeIngestion = new FakeIngestionService();
            services.AddScoped<IDocumentIngestionService>(_ => fakeIngestion);
            services.AddScoped<IWorkspaceResolver>(_ => new FakeWorkspaceResolver(tempRoot));

            // Сам сервис — Singleton (как в проде).
            services.AddSingleton<IWorkspaceIndexService, WorkspaceIndexService>();

            var provider = services.BuildServiceProvider();

            // EnsureCreated — в отдельном scope (dispose после вызова).
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            }

            // Сам Service — Singleton, резолвится из root.
            var service = provider.GetRequiredService<IWorkspaceIndexService>();

            return (provider, service, fakeIngestion, tempRoot);
        }

        /// <summary>
        /// Ждёт, пока <c>IsIndexing</c> станет <c>false</c> (макс. ~5 с).
        /// Использует polling через <see cref="Task.Delay(int)"/>.
        /// </summary>
        /// <param name="service">Сервис</param>
        /// <param name="userId">ID пользователя</param>
        /// <param name="timeoutMs">Максимум ожидания (по умолчанию 5000 мс)</param>
        private static async Task<WorkspaceIndexStatusDto> WaitForIndexingCompleteAsync(
            IWorkspaceIndexService service,
            int userId,
            int timeoutMs = 5000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            WorkspaceIndexStatusDto last = null;

            while (Environment.TickCount64 < deadline)
            {
                last = await service.GetStatusAsync(userId);
                if (!last.IsIndexing)
                    return last;

                await Task.Delay(50);
            }

            throw new TimeoutException(
                $"Индексация не завершилась за {timeoutMs} мс. " +
                $"Последний статус: IsIndexing={last?.IsIndexing}, " +
                $"FilesProcessed={last?.FilesProcessed}, TotalFiles={last?.TotalFiles}");
        }

        // ============================================================
        // Тесты
        // ============================================================

        /// <summary>
        /// До включения: enabled=false, chunkCount=0, isIndexing=false.
        /// </summary>
        [Fact]
        public async Task GetStatusAsync_Disabled_ReturnsEnabledFalse()
        {
            var (provider, service, _, tempRoot) = CreateService(("a.txt", "hello"));
            try
            {
                var status = await service.GetStatusAsync(userId: 1);

                Assert.False(status.Enabled);
                Assert.False(status.IsIndexing);
                Assert.Equal(0, status.ChunkCount);
                Assert.Equal(0, status.FilesIndexed);
                Assert.Null(status.LastIndexedAt);
                Assert.Equal(0, status.TotalFiles);
                Assert.Equal(0, status.FilesProcessed);
            }
            finally
            {
                provider.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// EnableAsync: устанавливает флаг enabled=true, запускает фоновую
        /// индексацию; через polling проверяем, что фон завершился и файлы
        /// обработаны (2 валидных файла, служебные подпапки игнорируются).
        /// </summary>
        [Fact]
        public async Task EnableAsync_SetsFlagAndStartsIndexing()
        {
            var (provider, service, ingestion, tempRoot) = CreateService(
                ("hello.txt", "Hello, world!"),
                ("notes.md", "# Заголовок\n\nТекст."),
                ("image.png", "не должен индексироваться"),   // фильтр расширений
                ("chat-attachments/ignored.txt", "служебная подпапка"));

            try
            {
                // Act: включаем.
                var afterEnable = await service.EnableAsync(userId: 1);

                // Assert: флаг enabled=true сразу (синхронная часть).
                // ПРИМЕЧАНИЕ: IsIndexing может быть уже false, если фон
                // успел завершиться (фейк быстрый) — это не ошибка.
                Assert.True(afterEnable.Enabled);

                // Assert: через polling дождались завершения фоновой задачи.
                var final = await WaitForIndexingCompleteAsync(service, userId: 1);

                Assert.False(final.IsIndexing);
                Assert.True(final.Enabled);

                // 2 файла (.txt + .md), .png и служебная подпапка игнорируются.
                Assert.Equal(2, final.TotalFiles);
                Assert.Equal(2, final.FilesProcessed);
                Assert.Equal(6, final.ChunksCreated);      // 2 файла × 3 чанка (FakeIngestion)
                Assert.Null(final.LastError);

                // Ingestion вызван ровно 2 раза.
                Assert.Equal(2, ingestion.IngestCalls.Count);
                Assert.All(ingestion.IngestCalls, r =>
                {
                    Assert.Equal("workspace", r.IndexName);
                    Assert.Equal(1, r.UserId);
                    Assert.Null(r.ChatId);
                });
            }
            finally
            {
                provider.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// DisableAsync: сбрасывает флаг enabled=false и очищает чанки
        /// пользователя через <see cref="IDocumentIngestionService.ClearIndexForUserAsync"/>.
        /// </summary>
        [Fact]
        public async Task DisableAsync_ClearsChunksAndFlag()
        {
            var (provider, service, ingestion, tempRoot) = CreateService(
                ("file.txt", "content"));
            try
            {
                // Arrange: сначала включаем (создаём состояние "enabled=true").
                await service.EnableAsync(userId: 1);
                await WaitForIndexingCompleteAsync(service, userId: 1);

                // Act.
                var afterDisable = await service.DisableAsync(userId: 1);

                // Assert: флаг сброшен, прогресс обнулён.
                Assert.False(afterDisable.Enabled);
                Assert.False(afterDisable.IsIndexing);
                Assert.Equal(0, afterDisable.TotalFiles);
                Assert.Equal(0, afterDisable.FilesProcessed);

                // Assert: ClearIndexForUserAsync был вызван 1 раз с правильными параметрами.
                var clearCall = Assert.Single(ingestion.ClearForUserCalls);
                Assert.Equal("workspace", clearCall.Index);
                Assert.Equal(1, clearCall.UserId);
            }
            finally
            {
                provider.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// ReindexAsync при выключенном индексе → InvalidOperationException.
        /// </summary>
        [Fact]
        public async Task ReindexAsync_WhenDisabled_Throws()
        {
            var (provider, service, _, tempRoot) = CreateService(("file.txt", "content"));
            try
            {
                // Индекс не включали → enabled=false по умолчанию.

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ReindexAsync(userId: 1));

                Assert.Contains("отключён", ex.Message);
            }
            finally
            {
                provider.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        /// <summary>
        /// ReindexAsync при включённом индексе: очищает чанки
        /// (<c>ClearIndexForUserAsync</c>) и запускает фоновую индексацию заново.
        /// </summary>
        [Fact]
        public async Task ReindexAsync_WhenEnabled_ClearsAndStarts()
        {
            var (provider, service, ingestion, tempRoot) = CreateService(
                ("a.txt", "alpha"),
                ("b.md", "beta"));
            try
            {
                // Arrange: включаем и ждём завершения первого прогона.
                await service.EnableAsync(userId: 1);
                await WaitForIndexingCompleteAsync(service, userId: 1);

                // Запоминаем состояние первого прогона.
                var ingestionCallCountBefore = ingestion.IngestCalls.Count;

                // Act: переиндексация.
                var afterReindex = await service.ReindexAsync(userId: 1);

                // Assert: фон снова запущен (enabled=true).
                // ПРИМЕЧАНИЕ: IsIndexing может быть уже false (фейк быстрый) — не проверяем.
                Assert.True(afterReindex.Enabled);

                // Assert: ClearIndexForUserAsync вызван.
                Assert.Single(ingestion.ClearForUserCalls);
                Assert.Equal("workspace", ingestion.ClearForUserCalls[0].Index);
                Assert.Equal(1, ingestion.ClearForUserCalls[0].UserId);

                // Assert: дождались второго прогона.
                var final = await WaitForIndexingCompleteAsync(service, userId: 1);

                Assert.False(final.IsIndexing);
                Assert.Equal(2, final.TotalFiles);
                Assert.Equal(2, final.FilesProcessed);
                Assert.Equal(6, final.ChunksCreated);

                // Assert: ingestion вызван ещё 2 раза (итого: 2 + 2 = 4).
                Assert.Equal(ingestionCallCountBefore + 2, ingestion.IngestCalls.Count);
            }
            finally
            {
                provider.Dispose();
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}