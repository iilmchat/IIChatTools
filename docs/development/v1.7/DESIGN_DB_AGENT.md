DESIGN v1.7 — Database Agent
Версия: 1.0 (draft)
Дата: 2026-09-28
Статус: Draft (согласование)
Связанные KI: KI-097 (DB Agent), KI-098 (admin UI whitelist), KI-099 (внешние подключения — Фаза 2)
Целевой релиз: v1.7.0 (Фаза 1 — internal DB), v1.8.0 (Фаза 2 — внешние подключения)

§ 1. Контекст
§ 1.1. Текущее состояние (после v1.6.1)
7 агентов в Chat: file_system_agent, code_agent, web_agent, git_agent, github_agent, planner_agent, consult_secondary_agent.

46 инструментов (40 raw + 6 агентов).

Данные приложения хранятся в БД: Chats, ChatMessages, DocumentChunks, ChatAttachments, AuditLogs, AppSettings, AspNetUsers, ...

LLM не имеет структурированного доступа к этим данным.

Есть code_agent, внутри которого execute_command может выполнить sqlite3 — но это небезопасно (см. § 2).

§ 1.2. Industry best practices
Проанализированы ведущие практики (Microsoft, AWS, Oracle, Neo4j, Atlan). Ключевой вывод: не давать LLM произвольный SQL.

Паттерны:

Domain-Oriented Tooling. Вместо execute_sql — узкоспециализированные декларативные функции (get_user_by_id, get_sales_report). Точность LLM: 66.6% → 93.9% (для малых моделей — 58.3% → 92.9%).

Semantic Layer. Отдельный слой (граф знаний) с бизнес-семантикой. Поднимает точность на сложных вопросах 16% → 54%.

Defense in Depth. Несколько независимых уровней безопасности: read-only роль в БД, валидация SQL, whitelist таблиц, timeout, audit.

§ 1.3. Цели v1.7.0 (Фаза 1)
#	Цель	Метрика
1	LLM может делать read-only запросы к собственной БД приложения	Tool execute_query возвращает результаты
2	Whitelist таблиц настраивается через админку	/admin → SQL Agent с UI для редактирования
3	Многоуровневая безопасность	read-only роль в БД + валидация SQL + approval + audit
4	Готовность к внешним БД	Контракт connection_name в tool; в Фазе 1 — только internal
5	Аудит запросов	Все execute_query пишутся в AuditLogs
§ 1.4. Что НЕ входит в v1.7.0
Внешние БД (Postgres / MySQL / Oracle) — Фаза 2 (v1.8.0).

Domain-Oriented Tools (get_recent_chats, get_user_stats) — по факту обкатки (Фаза 2+).

Write-операции (INSERT / UPDATE / DELETE) — не входит никогда (см. § 6.1).

Semantic Layer (knowledge graph) — не входит, план на v2.0.

Визуальный редактор результатов SQL (таблица) — Фаза 2+, в MVP — обычный tool_result.

§ 2. Проблема
§ 2.1. LLM не имеет доступа к данным
Пример: пользователь спрашивает «Сколько чатов я создал за последнюю неделю?»

Сейчас: LLM не может ответить. Или предлагает read_file — но это не работает для БД.

Хочется: LLM вызывает execute_query(connection="internal", sql="SELECT COUNT(*) FROM Chats WHERE CreatedAt >= date('now', '-7 days')") → получает ответ.

§ 2.2. Прямой доступ через execute_command — антипаттерн
Проблема	Последствие
SQL-инъекции	LLM может случайно/намеренно выполнить DROP TABLE
Отсутствие валидации	Нет whitelist таблиц → доступ к AspNetUsers (PII)
Нет timeout	Запрос SELECT * FROM ChatMessages CROSS JOIN ChatMessages ... → зависание БД
Нет approval	Пользователь не видит, что именно выполняется
Нет аудита	Нет следа о том, какие данные были прочитаны
Нет структуры	LLM не знает схему → генерирует запросы «наугад»
§ 2.3. Ограничения существующих инструментов
Инструмент	Что делает	Чего не хватает
execute_command	Выполняет shell-команду (в т.ч. sqlite3)	Нет валидации, нет whitelist, не профильный
read_file	Читает файл	Не работает для БД
search_knowledge_base	Ищет по RAG-чанкам	Не БД
DB Agent заполняет пробел: «безопасный структурированный доступ к реляционным данным».

§ 3. Решение
§ 3.1. Универсальный агент с подключениями
Не два агента (internal_db_agent + external_db_agent), а один с параметром connection_name.

Причины:

Опыт v1.6.1: 4B-модель плохо различает похожие tools (web_agent vs wikipedia_search). Два DB-агента — та же ошибка.

Единый код безопасности (валидация SQL, whitelist, approval).

Фаза 2 (внешние БД) не требует переписывания — только добавление новых записей в конфиг.

В Фазе 1 зарегистрировано одно подключение — internal (собственная БД приложения).

§ 3.2. Гибридный подход
Фаза 1: универсальный execute_query — даёт максимум гибкости. Обкатка на реальных данных.

Фаза 2+: доменные инструменты (по факту обкатки). Каждый доменный инструмент = отдельный метод с предопределённым SQL. Пользователь часто спрашивает «сколько чатов за неделю» → добавляем get_chat_count_for_user(userId, days).

Почему не сразу: доменные инструменты требуют знания бизнес-вопросов. Мы их пока не знаем. Обкатка execute_query даст данные.

§ 3.3. Многоуровневая безопасность (Defense in Depth)
Пять независимых уровней — обход одного не открывает доступ к данным.

#	Уровень	Что защищает	Обходится через промпт?
1	Read-only роль в БД	Любые write-операции	❌ Нет — это самый надёжный барьер
2	Валидация SQL	SQL-инъекции, запрет DROP/DELETE	⚠️ Теоретически (сложный SQL)
3	Whitelist таблиц	Доступ к PII (AspNetUsers)	❌ Нет — запросы на не-whitelist таблицы отклоняются
4	Statement timeout (15s)	Зависание БД	❌ Нет
5	Approval пользователя	Все неизвестные запросы	❌ Нет — пользователь видит SQL перед выполнением
Плюс:

Auto-LIMIT: если в SQL нет LIMIT, добавляется LIMIT MaxRows (default 100).

Audit: все вызовы execute_query пишутся в AuditLogs.

§ 3.4. Админ-настройка whitelist (по Q2)
Whitelist таблиц, MaxRows, StatementTimeout — редактируются через /admin → SQL Agent.

Изменения сохраняются в AppSettings (как SubAgents.* в v1.4.0) и восстанавливаются при старте приложения (Program.LoadSqlAgentOverridesAsync).

UI:

Новая вкладка SQL Agent в /admin (9-я по счёту).

Список подключений (internal + в будущем — внешние).

Для каждого подключения: toggle Enabled, AllowedTables (multiline textarea или чек-лист), DeniedTables, MaxRows, StatementTimeoutSeconds.

Кнопка «Проверить подключение» — тестовый SELECT 1.

§ 4. Архитектура
§ 4.1. Слои (IIChatTools.Services)
text
IIChatTools.Services/
  ├── DTO/SqlAgent/
  │   ├── DatabaseConnectionInfoDto.cs        — { name, displayName, provider, enabled }
  │   ├── SqlTableInfoDto.cs                  — { name, rowCount, description? }
  │   ├── SqlColumnInfoDto.cs                 — { name, type, nullable, sampleValue }
  │   ├── SqlQueryRequest.cs                  — { connection, sql, maxRows? }
  │   ├── SqlQueryResultDto.cs                — { columns[], rows[], rowCount, truncated, durationMs }
  │   ├── ValidationResult.cs                 — { isValid, error, sanitizedSql, referencedTables, limitAdded }
  │   ├── SqlAgentOptions.cs                  — bind из appsettings:SqlAgent
  │   └── SqlAgentConnectionOptions.cs        — подсекция Conn[*]
  │
  ├── Interfaces/
  │   ├── ISqlAgentService.cs                 — оркестратор (list / describe / execute)
  │   ├── ISqlQueryValidator.cs               — валидация SQL (whitelist, запреты, auto-LIMIT)
  │   └── ISqlConnectionProvider.cs           — фабрика DbConnection по имени подключения
  │
  ├── Implementation/SqlAgent/
  │   ├── SqlAgentService.cs                  — Scoped
  │   ├── SqlQueryValidator.cs                — Singleton (stateless)
  │   └── SqlConnectionProvider.cs            — Singleton (кэш connection strings)
  │
  └── Implementation/Tools/SqlAgent/
    └── DatabaseAgentTool.cs                — прямая реализация ITool
Особенность: DatabaseAgentTool не использует SubAgentService (как остальные агенты). Внутри — прямой вызов ISqlAgentService. Это проще и безопаснее — агент не «думает», а детерминированно выполняет операции. Никакого LLM-цикла внутри.

Альтернатива (отклонена): использовать SubAgentService с одним tool execute_query. Минус — добавляет LLM-loop туда, где он не нужен → повышает расход токенов и времени.

§ 4.2. DI-регистрация (Startup.cs)
text
services.Configure<SqlAgentOptions>(Configuration.GetSection("SqlAgent"));
services.AddSingleton<ISqlConnectionProvider, SqlConnectionProvider>();
services.AddSingleton<ISqlQueryValidator, SqlQueryValidator>();
services.AddScoped<ISqlAgentService, SqlAgentService>();
services.AddScoped<ITool, DatabaseAgentTool>();
Порядок: DatabaseAgentTool добавляется в RegisterSqlAgentTools(services) (новый private метод в Startup.cs). Регистрация не в RegisterSubAgentTools — это отдельная группа.

§ 4.3. Tools Database Agent
Tool	Назначение	RequiresApproval
list_databases	Список подключений (без connection string)	❌ (read-only, метаданные)
list_tables	Whitelist таблиц для подключения	❌ (read-only, метаданные)
describe_table	Колонки + типы + пример значения	❌ (read-only, метаданные)
execute_query	Выполнить read-only запрос	✅ (всегда — даже в whitelist)
Решение по approval (Q3): только execute_query требует approval. Метаданные (list_*, describe_*) — без. Обоснование: метаданные не раскрывают содержимое (только имена таблиц/колонок), а approval на каждый list_tables создаёт лишние клики.

При этом сам агент database_agent (RequiresApprovalByDefault в ToolDescriptor) — false. Approval срабатывает внутри, на конкретном execute_query. Это даёт UX: пользователь кликает 1 раз, видит сам SQL, подтверждает.

> ⚠️ **Обновление (2026-09-29, Фаза 5 реализована):** на момент реализации Фазы 5
> архитектура `ChatStreamService` **не поддерживает per-action approval** —
> флаг `RequiresApprovalByDefault` является свойством **всего tool**
> (`ToolDescriptor`), а не отдельного вызова. Механизма «решить по args»
> в `ChatStreamService` нет.
>
> **Практическое решение Фазы 5:** `DatabaseAgentTool.RequiresApprovalByDefault = true`
> (все 4 действия требуют approval). Безопасно, работает без доработки
> `ChatStreamService`. Для `list_tables` — лишний клик (UX-мелочь).
>
> **Per-action approval** (как описано выше) — задача **KI-101**, запланирована
> на v1.7.x. Требует доработки `ChatStreamService` (варианты — в KI-101).

§ 5. Конфигурация
§ 5.1. appsettings.json — секция SqlAgent:*
jsonc
"SqlAgent": {
  "Enabled": true,
  "DefaultConnection": "internal",
  "AdminUiEnabled": true,

  "Connections": {
    "internal": {
      "DisplayName": "IIChatTools DB (собственная)",
      "Provider": "Sqlite",
      "ConnectionStringKey": "SqlAgent:Internal:ConnectionString",

      "AllowedTables": [
        "Chats",
        "ChatMessages",
        "DocumentChunks",
        "ChatAttachments",
        "AuditLogs",
        "AppSettings"
      ],
      "DeniedTables": [
        "AspNetUsers",
        "AspNetUserTokens",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserRoles",
        "AspNetRoles",
        "AspNetRoleClaims",
        "PendingActions",
        "AgentStates",
        "MemoryEntries",
        "UserSettings"
      ],

      "MaxRows": 100,
      "StatementTimeoutSeconds": 15,
      "RequiresApproval": true,
      "Description": "Данные приложения IIChatTools: чаты, сообщения, аудит. Read-only."
    }
  },

  "QueryValidation": {
    "DeniedKeywords": [
      "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", "CREATE",
      "REPLACE", "MERGE", "GRANT", "REVOKE", "EXEC", "EXECUTE",
      "ATTACH", "DETACH", "PRAGMA", "VACUUM", "ANALYZE", "REINDEX",
      "COMMIT", "ROLLBACK", "SAVEPOINT", "BEGIN", "END"
    ],
    "DeniedFunctions": [
      "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer"
    ],
    "MaxSqlLength": 4000,
    "AutoLimitIfMissing": true
  }
}
Пояснения:

Ключ	Назначение
Enabled	Глобальный toggle. false → DatabaseAgentTool не регистрируется в DI
DefaultConnection	Имя подключения по умолчанию (используется, если LLM не указала connection)
AdminUiEnabled	Показывать вкладку «SQL Agent» в /admin
Connections	Словарь подключений (internal — в MVP; external_* — Фаза 2)
Connections[*].DisplayName	Отображается в UI и возвращается в list_databases
Connections[*].Provider	Sqlite / SqlServer / (Фаза 2: Postgres, MySql)
Connections[*].ConnectionStringKey	Ключ в User Secrets, где лежит строка подключения (не сама строка!)
Connections[*].AllowedTables	Whitelist. Сравнение — case-sensitive (в Sqlite таблицы case-sensitive по умолчанию)
Connections[*].DeniedTables	Blacklist. Перебивает whitelist. Защита от случайного добавления PII
Connections[*].MaxRows	Лимит строк. Auto-LIMIT добавляется, если в SQL нет LIMIT
Connections[*].StatementTimeoutSeconds	DbCommand.CommandTimeout
Connections[*].RequiresApproval	Для execute_query. Метаданные (list_*, describe_*) — всегда без approval
QueryValidation.DeniedKeywords	Запрещённые слова (case-insensitive, по границам слов)
QueryValidation.DeniedFunctions	Запрещённые функции (Sqlite: load_extension — RCE)
QueryValidation.MaxSqlLength	Защита от гигантских запросов
QueryValidation.AutoLimitIfMissing	Добавлять LIMIT N, если нет
§ 5.2. appsettings.Development.json — override
jsonc
"SqlAgent": {
  "Enabled": true,
  "DefaultConnection": "internal",
  "Connections": {
    "internal": {
      "DisplayName": "IIChatTools Dev DB",
      "Provider": "Sqlite",
      "ConnectionStringKey": "SqlAgent:Internal:ConnectionString",
      "AllowedTables": [
        "Chats", "ChatMessages", "DocumentChunks",
        "ChatAttachments", "AuditLogs", "AppSettings"
      ],
      "DeniedTables": ["AspNetUsers", "AspNetUserTokens"],
      "MaxRows": 50,
      "StatementTimeoutSeconds": 30,
      "RequiresApproval": true
    }
  }
}
Отличия dev: MaxRows = 50 (быстрее), StatementTimeoutSeconds = 30 (терпимее к медленному dev-диску).

§ 5.3. User Secrets — строки подключения
Строки подключения никогда не хранятся в appsettings*.json. Только в User Secrets (dev) или env-переменных (prod).

Dev (Sqlite):

powershell
cd C:\Projects\AI\IIChatTools\IIChatTools.API
dotnet user-secrets set "SqlAgent:Internal:ConnectionString" "Data Source=Data/iichattools-dev.db;Mode=ReadOnly"
Важно: Mode=ReadOnly — встроенная защита Sqlite. Даже если валидатор пропустит DELETE, БД вернёт ошибку.

Prod (SqlServer, Фаза 2):

powershell
dotnet user-secrets set "SqlAgent:Internal:ConnectionString" `
  "Server=localhost;Database=IIChatTools;User Id=iichattools_reader;Password=<...>;ApplicationIntent=ReadOnly;TrustServerCertificate=True"
ApplicationIntent=ReadOnly — SqlServer-нативная защита. Требует настройки AlwaysOn Availability Group (не везде доступно, но best practice).

§ 5.4. Admin override через AppSettings
По аналогии с SubAgents.* (v1.4.0): настройки SqlAgent:* редактируются через /admin и сохраняются в AppSettings как JSON:

Ключ	Значение
SqlAgent.internal.enabled	"true" / "false"
SqlAgent.internal.allowedTables	["Chats", "ChatMessages"] (JSON-массив)
SqlAgent.internal.deniedTables	["AspNetUsers"]
SqlAgent.internal.maxRows	"100"
SqlAgent.internal.statementTimeoutSeconds	"15"
Загрузка при старте (Program.LoadSqlAgentOverridesAsync):

Читаем AppSettings (ключи с префиксом SqlAgent.).

Парсим в SqlAgentConnectionOptions.

SqlAgentOptionsProvider.Update(connection, options) — Singleton, потокобезопасно.

SqlConnectionProvider и SqlQueryValidator читают актуальные значения из провайдера при каждом вызове (не кэшируют между запросами).

Не перезапускается — изменения применяются в runtime. Аналогично SubAgentRegistry.Update.

§ 5.5. Правила валидации конфигурации (при старте)
При старте приложения SqlAgentOptions валидируется:

Проверка	Действие при ошибке
DefaultConnection существует в Connections	InvalidOperationException — приложение не стартует
Provider ∈ {Sqlite, SqlServer}	InvalidOperationException
ConnectionStringKey разрешается (env / secrets / appsettings)	InvalidOperationException
MaxRows ∈ [1, 10000]	Clamp + warning
StatementTimeoutSeconds ∈ [1, 300]	Clamp + warning
AllowedTables не пересекается с DeniedTables	Warning (Denied — перебивает)
AllowedTables не пуст	Warning (агент бесполезен, но не падает)
Принцип: критичные ошибки (нет connection string) → fail fast. Некритичные (MaxRows вне диапазона) → clamp + warning.

§ 6. Безопасность
§ 6.1. Уровень 1 — Read-only роль в БД (самый надёжный)
Sqlite:

Отдельной роли нет.

Mode=ReadOnly в connection string — встроенная защита. Все write-операции (INSERT, DELETE, CREATE TABLE) → SQLite Error 8: attempt to write a readonly database.

Проверено: Sqlite через Mode=ReadOnly отклоняет любые write, включая PRAGMA.

SqlServer (Фаза 2):

sql
-- 1. Создаём login (на уровне сервера)
CREATE LOGIN iichattools_reader WITH PASSWORD = '...';

-- 2. Создаём user в БД
USE IIChatTools;
CREATE USER iichattools_reader FOR LOGIN iichattools_reader;

-- 3. Grant read-only на всю БД
ALTER ROLE db_datareader ADD MEMBER iichattools_reader;
-- db_datareader даёт SELECT на все объекты

-- 4. Запрещаем write на уровне БД
DENY INSERT, UPDATE, DELETE, EXECUTE TO iichattools_reader;
DENY CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO iichattools_reader;
DENY ALTER ANY SCHEMA TO iichattools_reader;
Дополнительно для SqlServer:

ApplicationIntent=ReadOnly в connection string — маршрутизирует на read-only реплику (если настроен AlwaysOn).

Отдельная БД-реплика IIChatTools_ReadOnly — обновляется через log shipping. Максимальная изоляция.

Принцип: даже если валидатор SQL пропустит DELETE, роль в БД отклонит его. Это единственный барьер, который не обходится через промпт-инъекцию.

§ 6.2. Уровень 2 — Валидация SQL
Файл: IIChatTools.Services/Implementation/SqlAgent/SqlQueryValidator.cs

Контракт:

csharp
public interface ISqlQueryValidator
{
    ValidationResult Validate(string sql, SqlAgentConnectionOptions options);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string Error { get; set; }              // null если IsValid
    public string SanitizedSql { get; set; }       // SQL с auto-LIMIT
    public IReadOnlyList<string> ReferencedTables { get; set; }
    public bool LimitAdded { get; set; }
}
Алгоритм (гибрид regex + токенизация, Q4-вариант C):

Шаг 1 — базовые проверки:

text
1. sql != null && sql не пуст
2. sql.Length <= MaxSqlLength (default 4000)
3. sql.TrimStart() начинается с "SELECT" или "WITH" (case-insensitive)
   — допускаем CTE (WITH ... SELECT), они read-only
4. sql не содержит более 1 statement (нет ";" вне строковых литералов)
Шаг 2 — токенизация:

Разбить SQL на токены: ключевые слова, идентификаторы, строковые литералы, комментарии.

Строковые литералы и комментарии исключаются из дальнейших проверок (защита от SELECT 'DROP TABLE' AS x).

Шаг 3 — запрет ключевых слов:

Для каждого токена из DeniedKeywords (INSERT, DELETE, DROP, ...):

Проверяем, что токен не является частью идентификатора (information_schema.tables — это одно слово, не tables).

Проверяем, что токен не в строковом литерале / комментарии.

При совпадении → ValidationResult.IsValid = false.

Шаг 4 — запрет функций:

Аналогично — для DeniedFunctions (load_extension, readfile, ...).

Sqlite: load_extension даёт RCE → критично.

Шаг 5 — извлечение таблиц:

Простой regex для FROM <table>, JOIN <table>, WITH <cte_name> AS:

text
\b(?:FROM|JOIN|WITH)\s+([A-Za-z_][A-Za-z0-9_]*)
Учитываем алиасы: FROM Chats c → table = Chats.

Не обрабатываем CTE-имена (WITH cte AS (...)) — они не таблицы.

Ограничение: сложные подзапросы ((SELECT ... FROM X) внутри FROM) могут не извлечься. На MVP — fail-safe: если таблица не извлечена — оставляем SQL, проверка whitelist работает по тем таблицам, что нашли.

Шаг 6 — проверка whitelist:

Каждая найденная таблица должна быть в AllowedTables и не в DeniedTables.

Если хотя бы одна таблица не в whitelist → IsValid = false с перечислением «запрещённые таблицы: X, Y».

Шаг 7 — auto-LIMIT:

Если AutoLimitIfMissing = true и в SQL нет LIMIT:

Для Sqlite → добавить LIMIT N в конец (если нет ;).

Для SqlServer → обернуть в SELECT TOP N * FROM (...) AS t — Фаза 2.

Помечаем LimitAdded = true для логов.

Пример 1 — валидный запрос:

Вход:

sql
SELECT Id, Title FROM Chats WHERE UserId = 1 ORDER BY UpdatedAt DESC
Выход: IsValid = true, SanitizedSql = SELECT ... LIMIT 100, ReferencedTables = ["Chats"], LimitAdded = true.

Пример 2 — запрос на запрещённую таблицу:

Вход:

sql
SELECT Email FROM AspNetUsers
Выход: IsValid = false, Error = "Таблицы запрещены: AspNetUsers".

Пример 3 — SQL-инъекция в литерале:

Вход:

sql
SELECT 'DROP TABLE Chats' AS x FROM ChatMessages
Выход: IsValid = true (литерал не считается командой), ReferencedTables = ["ChatMessages"].

Пример 4 — multi-statement:

Вход:

sql
SELECT 1; DROP TABLE Chats
Выход: IsValid = false, Error = "Множественные операторы не поддерживаются".

Пример 5 — DELETE (case-insensitive):

Вход:

sql
delete from chats where id=1
Выход: IsValid = false, Error = "Запрещённое ключевое слово: DELETE".

§ 6.3. Уровень 3 — Whitelist таблиц
Работает в валидаторе (см. § 6.2, шаг 6). Дополнительно:

Case-sensitive сравнение для Sqlite (регистр таблиц важен).

Для SqlServer (Фаза 2) — case-insensitive, но whitelist пишется точно как в БД.

Защита от alias-подмены: SELECT * FROM Chats AS AspNetUsers — алиас не считается доступом к AspNetUsers (regex извлекает Chats).

TODO Фаза 2: если алиас совпадает с named-таблицей — warning в логах.

§ 6.4. Уровень 4 — Statement timeout + MaxRows
Timeout:

csharp
using var command = connection.CreateCommand();
command.CommandText = result.SanitizedSql;
command.CommandTimeout = options.StatementTimeoutSeconds;   // 15s default
При превышении — DbException → ToolResult.Fail("Запрос превысил лимит N секунд").

MaxRows:

Auto-LIMIT добавляет LIMIT MaxRows — БД физически не вернёт больше.

При чтении результата — дополнительная защита: если в DataReader пришло больше строк, чем MaxRows * 2 (защита от обхода через UNION), читаем только MaxRows, помечаем truncated = true.

§ 6.5. Уровень 5 — Approval + Audit
Approval (execute_query):

DatabaseAgentTool.RequiresApprovalByDefault = false — сам агент не требует approval целиком (иначе лишний клик на list_tables).

Внутри DatabaseAgentTool.ExecuteAsync при action == "execute_query":

Если options.RequiresApproval == true → возврат ToolResult.Fail("Требуется подтверждение пользователя").

Chat видит requiresApproval = true в ChatToolResultDto → показывает модалку.

В модалке отображается SQL-запрос (пользователь видит, что именно выполняется).

Аудит:

Каждый вызов execute_query пишет запись в AuditLogs:

ToolName = "database_agent.execute_query".

ParametersJson = { connection, sqlHash, sqlPreview, rowCount } — без полного SQL (может содержать PII в WHERE).

ResultJson = { success, rowCount, truncated, durationMs, error }.

UserId, ClientIp, DurationMs, Status.

Логирование через ILogger — только метаданные: connection, duration, row count. SQL — не логируется (RULES § 5.x — без PII).

§ 6.6. Сводная таблица угроз и защит
Угроза	Защита	Обходится?
DROP TABLE через промпт-инъекцию	Валидатор (шаг 3) + read-only роль в БД	❌
DELETE FROM Chats	Валидатор + read-only роль	❌
Чтение AspNetUsers (PII)	Whitelist (шаг 6)	❌
SQL-инъекция через '; DROP ...	Multi-statement запрет (шаг 1)	❌
SQL-инъекция через литерал 'DROP TABLE'	Литералы исключаются (шаг 2)	❌
DoS через CROSS JOIN	Timeout (15s) + Auto-LIMIT	❌
Утечка через readfile()	DeniedFunctions	❌
RCE через load_extension()	DeniedFunctions + readonly mode Sqlite	❌
Многошаговый обход через CTE	Whitelist по всем FROM/JOIN	⚠️ Ограничение MVP (Фаза 2: ScriptDom)
Гигантский запрос	MaxSqlLength (4000)	❌
Утечка connection string через tool	list_databases возвращает только DisplayName	❌
Вывод: покрытие полное для read-only угроз. Единственное ограничение — сложные CTE/подзапросы, требующие полноценного парсера (Фаза 2).

§ 7. План фаз (0–8)
Итого: ~40 ч (≈5 рабочих дней).

Фаза	Что	Оценка	Зависимости
0	DESIGN (этот документ)	—	✅ Done
1	Контракты + DTO	3 ч	Фаза 0
2	SqlConnectionProvider + SqlAgentOptions	4 ч	Фаза 1
3	SqlQueryValidator	6 ч	Фаза 2
4	SqlAgentService + 4 tools	6 ч	Фаза 3
5	DatabaseAgentTool + интеграция в Chat	3 ч	Фаза 4
6	Admin UI (вкладка SQL Agent)	6 ч	Фаза 4
7	Тесты (unit + integration)	6 ч	Фазы 1-6
8	Документация + релиз v1.7.0	6 ч	Фазы 1-7
§ 7.1. Фаза 1 — Контракты + DTO (3 ч)
Шаг	Файлы	Тесты
1.1	DTO/SqlAgent/DatabaseConnectionInfoDto.cs (новый)	—
1.2	DTO/SqlAgent/SqlTableInfoDto.cs (новый)	—
1.3	DTO/SqlAgent/SqlColumnInfoDto.cs (новый)	—
1.4	DTO/SqlAgent/SqlQueryRequest.cs (новый)	—
1.5	DTO/SqlAgent/SqlQueryResultDto.cs (новый)	—
1.6	Interfaces/ISqlAgentService.cs (новый)	—
1.7	Interfaces/ISqlQueryValidator.cs (новый)	—
1.8	Interfaces/ISqlConnectionProvider.cs (новый)	—
| 1.9 | `DTO/SqlAgent/ValidationResult.cs` (новый) | — |
| 1.10 | `DTO/SqlAgent/SqlAgentOptions.cs` (новый) | — |
| 1.11 | `DTO/SqlAgent/SqlAgentConnectionOptions.cs` (новый) | — |
DoD фазы: dotnet build — 0/0. Все DTO/интерфейсы компилируются, но нигде не используются.

§ 7.2. Фаза 2 — SqlConnectionProvider + SqlAgentOptions (4 ч)
Шаг	Файлы	Тесты
2.1	Implementation/SqlAgent/SqlConnectionProvider.cs (новый)	—
2.2	Implementation/SqlAgent/SqlAgentOptionsProvider.cs (новый)	—
2.3	Startup.cs: services.Configure<SqlAgentOptions>(...)	—
2.4	Program.cs: LoadSqlAgentOverridesAsync	—
2.5	appsettings.json + .Development.json: секция SqlAgent	—
2.6	User Secrets: SqlAgent:Internal:ConnectionString (инструкция в README)	—
DoD фазы: можно создать DbConnection для internal подключения через провайдер. Юнит-тест «connection string разрешается из secrets».

§ 7.3. Фаза 3 — SqlQueryValidator (6 ч) — самая ответственная
Шаг	Файлы	Тесты
3.1	SqlQueryValidator.cs: скелет + ValidationResult	1
3.2	Шаг 1: базовые проверки (SELECT/длина/multi-statement)	4
3.3	Шаг 2: токенизация (литералы, комментарии)	3
3.4	Шаг 3: запрет ключевых слов	5
3.5	Шаг 4: запрет функций	3
3.6	Шаг 5: извлечение таблиц (regex)	4
3.7	Шаг 6: whitelist-проверка	4
3.8	Шаг 7: auto-LIMIT	3
DoD фазы: SqlQueryValidatorTests — ~27 тестов. Валидатор отклоняет DELETE, DROP, AspNetUsers, SELECT 1; DROP TABLE, пропускает валидный SELECT.

Риск: regex-извлечение таблиц из сложных подзапросов. Mitigation: fail-safe + логирование. Полный парсер (ScriptDom) — Фаза 2.

§ 7.4. Фаза 4 — SqlAgentService + 4 tools (6 ч)
Шаг	Файлы	Тесты
4.1	SqlAgentService.cs: ListConnectionsAsync	2
4.2	ListTablesAsync(connection)	2
4.3	DescribeTableAsync(connection, table)	3
4.4	ExecuteQueryAsync(connection, sql) — с валидатором и timeout	4
4.5	DatabaseAgentTool.cs (наследник ITool, не AgentToolBase!)	—
4.6	Startup.cs: RegisterSqlAgentTools(services)	—
Реализация ListTablesAsync:

Sqlite: SELECT name FROM sqlite_master WHERE type='table' AND name IN (@whitelist).

Фильтрация по AllowedTables из options.

Дополнительно — SELECT COUNT(*) для каждой таблицы (опционально, rowCount).

Реализация ExecuteQueryAsync:

Валидатор → SanitizedSql.

DbConnection из провайдера.

command.CommandTimeout = options.StatementTimeoutSeconds.

Execute reader → List<Dictionary<string, object>>.

Auto-stop при достижении MaxRows * 2 (защита от UNION-обхода).

Возврат SqlQueryResultDto { columns[], rows[], rowCount, truncated, durationMs }.

DoD фазы: SqlAgentServiceTests — ~11 тестов. Все 4 операции работают на dev-Sqlite.

§ 7.5. Фаза 5 — DatabaseAgentTool + интеграция в Chat (3 ч)
Шаг	Что	Тесты
5.1	DatabaseAgentTool.Name => "database_agent" + Description (RU)	—
5.2	Parameters: action (enum list_databases / list_tables / describe_table / execute_query), connection (string, optional), sql (string, optional), table (string, optional)	—
5.3	RequiresApprovalByDefault = false (approval на execute_query — внутри)	—
5.4	ChatStreamService — database_agent в allowedNames (RULES § 4.44!)	—
5.5	Smoke через curl: list_databases, list_tables, execute_query	—
5.6	Smoke через Chat UI: LLM выбирает database_agent для вопроса «сколько чатов»	—
Особенность: DatabaseAgentTool не наследует AgentToolBase (тот для LLM-loop). Прямой вызов ISqlAgentService. Внутри — switch по action.

Approval flow: для execute_query возврат ToolResult.Fail("Требуется approval. SQL: <запрос>") → Chat SSE tool_approval_required → модалка → approve → повторный вызов с approvalId → выполнение.

DoD фазы: Chat видит 11 tools (было 10). LLM может вызвать database_agent и получить ответ на «сколько чатов у меня в БД».

§ 7.6. Фаза 6 — Admin UI (вкладка SQL Agent) (6 ч)
Шаг	Что	Файлы
6.1	Backend: AdminSqlAgentController (4 endpoints)	IIChatTools.API/Controllers/AdminSqlAgentController.cs
6.2	Backend: IAdminSqlAgentService + AdminSqlAgentService	IIChatTools.Services/Implementation/SqlAgent/
6.3	UI: 9-я вкладка в Admin.cshtml	Views/Home/Admin.cshtml
6.4	UI: admin-sql-agent.js — таблица подключений + модалка редактирования	wwwroot/js/modules/admin-sql-agent.js
6.5	UI: кнопка «Проверить подключение» (SELECT 1)	—
6.6	UI: CSS .sql-agent-* (стилизация чек-листа таблиц)	wwwroot/css/site.css
6.7	.resx (RU + EN) — ~20 ключей	SharedResources.resx, SharedResources.ru.resx
Endpoints:

Метод	URL	Назначение
GET	/api/admin/sql-agent/connections	Список подключений + их настройки
PUT	/api/admin/sql-agent/connections/{name}	Обновить (AllowedTables, DeniedTables, MaxRows, StatementTimeout, Enabled)
POST	/api/admin/sql-agent/connections/{name}/test	Проверить подключение (SELECT 1)
POST	/api/admin/sql-agent/connections/{name}/reset	Сбросить к appsettings.json
UI-модалка:

DisplayName (read-only, из config).

Provider (read-only).

Checkbox Enabled.

Textarea AllowedTables (по строке на таблицу) — с авто-загрузкой списка существующих таблиц из БД.

Textarea DeniedTables.

Number MaxRows (1–10000).

Number StatementTimeoutSeconds (1–300).

Кнопка Проверить подключение — SELECT 1 с timeout.

DoD фазы: /admin → SQL Agent — рабочая вкладка. Изменения сохраняются в AppSettings и применяются в runtime без перезапуска.

§ 7.7. Фаза 7 — Тесты (6 ч)
Шаг	Что	Кол-во
7.1	SqlQueryValidatorTests	~27
7.2	SqlAgentServiceTests (fake DbConnection для Sqlite in-memory)	~11
7.3	SqlConnectionProviderTests	~4
7.4	DatabaseAgentToolTests (fake ISqlAgentService)	~8
7.5	AdminSqlAgentControllerTests	~4
7.6	Integration: полный flow execute_query через /api/tools/execute	~2
Итого: +56 тестов (241 → 297).

DoD фазы: dotnet test — 297/297. Покрытие SqlQueryValidator — >90%.

§ 7.8. Фаза 8 — Документация + релиз v1.7.0 (6 ч)
Шаг	Что
8.1	README.md — раздел «Database Agent» (примеры вопросов, ограничения)
8.2	docs/development/RULES.md — новые правила (если появятся уроки)
8.3	docs/KNOWN_ISSUES.md — KI-097 → Fixed, KI-098 (admin UI) → Fixed
8.4	docs/TESTING.md — smoke-сценарии для DB Agent
8.5	CHANGELOG.md — [1.7.0] — YYYY-MM-DD
8.6	Directory.Build.props — <Version>1.7.0</Version>
8.7	Tag v1.7.0 + GitHub Release + Docker
DoD фазы: релиз v1.7.0 опубликован.

§ 8. Definition of Done (v1.7.0)
§ 8.1. Функциональные требования
□ DatabaseAgentTool зарегистрирован в DI и виден Chat (11 tools вместо 10).
□ list_databases возвращает список подключений (без connection string).
□ list_tables возвращает только whitelist-таблицы.
□ describe_table возвращает колонки + типы + пример значения.
□ execute_query возвращает до MaxRows строк с truncated-флагом.
□ execute_query требует approval (модалка с SQL).
□ Валидатор отклоняет: DELETE, UPDATE, INSERT, DROP, AspNetUsers, multi-statement.
□ Валидатор пропускает: SELECT с CTE, литералы с запретными словами.
□ Auto-LIMIT работает (запрос без LIMIT получает LIMIT MaxRows).
□ Timeout работает (запрос > 15s падает с сообщением).
□ Read-only роль в БД (Sqlite: Mode=ReadOnly).
□ Admin UI /admin → SQL Agent позволяет менять whitelist.
□ Изменения из админки применяются в runtime (без перезапуска).
§ 8.2. Нефункциональные
□ dotnet build — 0 warnings, 0 errors.
□ dotnet test — ~297/297.
□ CI + Docker Publish — зелёные.
□ SqlQueryValidator — покрытие >90%.
□ Connection string — только в User Secrets / env (не в git).
□ Все execute_query пишутся в AuditLogs (без SQL).
□ Логирование — без PII.
§ 8.3. Smoke (7 сценариев)
#	Сценарий	Ожидание
1	curl database_agent(action=list_databases)	[{name: "internal", displayName: "..."}]
2	curl database_agent(action=list_tables, connection=internal)	6 таблиц из whitelist
3	curl database_agent(action=describe_table, connection=internal, table=Chats)	5+ колонок
4	curl database_agent(action=execute_query, ..., sql=SELECT COUNT(*) FROM Chats)	Число чатов
5	curl execute_query с sql=SELECT * FROM AspNetUsers	Fail: «Таблицы запрещены: AspNetUsers»
6	curl execute_query с sql=DELETE FROM Chats	Fail: «Запрещённое ключевое слово: DELETE»
7	Chat UI: «Сколько чатов у меня?»	LLM вызывает database_agent, показывает результат
§ 8.4. Документация
□ README.md — раздел «Database Agent».
□ CHANGELOG.md — [1.7.0].
□ KNOWN_ISSUES.md — KI-097, KI-098 → Fixed.
□ TESTING.md — smoke для DB Agent.
□ RULES.md — обновлён, если есть новые уроки.
§ 9. Ссылки
§ 9.1. KI
KI-097 — Database Agent (этот документ, Фаза 1).

KI-098 — Admin UI для whitelist (часть KI-097).

KI-099 — Внешние подключения (Фаза 2, v1.8.0).

§ 9.2. Правила (RULES.md)
§ 1.14 — локализация (RU + EN).

§ 4.16 — camelCase ключи .resx.

§ 4.17 — JS-локализация через data-*.

§ 4.44 — новый top-level ITool → allowedNames в Chat.

§ 5.x — User Secrets, без PII в логах.

§ 9.3. Внешние источники
Microsoft: Best practices for AI agents and databases — domain-oriented tooling, семантический слой.

AWS: Building trustworthy AI agents with database access — defense in depth.

Neo4j: Graph-enhanced text-to-SQL — semantic layer через граф знаний (точность 16% → 54%).

SQLite: PRAGMA query_only — read-only режим.

OWASP: SQL Injection Prevention — базовые принципы.

Microsoft.SqlServer.TransactSql.ScriptDom — полноценный парсер (Фаза 2).

§ 9.4. Внутренние документы
docs/development/RULES.md — правила разработки (v1.4.18+).

docs/development/ARCHITECTURE.md — архитектура проекта.

docs/development/v1.4/DESIGN.md — Multi-Agent (эталон для SubAgentDescriptor).

docs/KNOWN_ISSUES.md — реестр проблем.

§ 9.5. Приложения
Приложение A — пример SQL-запроса, который LLM может сгенерировать:

sql
SELECT
  c.Id,
  c.Title,
  COUNT(m.Id) AS MessageCount
FROM Chats c
LEFT JOIN ChatMessages m ON m.ChatId = c.Id
WHERE c.UserId = 1
  AND c.UpdatedAt >= date('now', '-7 days')
GROUP BY c.Id, c.Title
ORDER BY c.UpdatedAt DESC
Валидатор:

SELECT ✅

Нет запрещённых ключевых слов ✅

Таблицы: Chats, ChatMessages — обе в whitelist ✅

Auto-LIMIT: добавит LIMIT 100 ✅

Результат: IsValid = true

Приложение B — пример ответа execute_query:

json
{
  "success": true,
  "data": {
    "connection": "internal",
    "columns": ["Id", "Title", "MessageCount"],
    "rows": [
      { "Id": 1, "Title": "Yield Return в RULES", "MessageCount": 8 },
      { "Id": 2, "Title": "Москва в Вики", "MessageCount": 4 }
    ],
    "rowCount": 2,
    "truncated": false,
    "durationMs": 12
  },
  "message": "Возвращено 2 строк за 12 мс."
}
Приложение C — формат UI-модалки approval для execute_query:

Модалка подтверждения (_ApprovalModal, уже реализована в v1.3.0) показывает:

Инструмент: database_agent

Действие: execute_query

Подключение: internal

SQL-запрос:

sql
SELECT Id, Title FROM Chats WHERE UserId = 1 ORDER BY UpdatedAt DESC LIMIT 100
Оценка запроса: 3 таблицы (Chats) в whitelist, auto-LIMIT применён.

Кнопки: Approve / Reject.

Пользователь видит реальный SQL перед подтверждением — это главная защита от нежелательных запросов.

Приложение D — пример конфигурации для внешней БД (Фаза 2):

jsonc
"SqlAgent": {
  "Connections": {
    "internal": { /* ... */ },
    "analytics": {
      "DisplayName": "Аналитика (PostgreSQL)",
      "Provider": "Postgres",
      "ConnectionStringKey": "SqlAgent:Analytics:ConnectionString",
      "AllowedTables": ["sales", "products", "regions"],
      "DeniedTables": ["users_pii"],
      "MaxRows": 500,
      "StatementTimeoutSeconds": 30,
      "RequiresApproval": true
    }
  }
}
Отличие Фазы 2: добавится провайдер Postgres + MySql. SqlConnectionProvider расширится. Все остальные компоненты — без изменений.

Конец DESIGN_DB_AGENT.md v1.7.0.