# DESIGN v1.8 — Mail Agent

**Версия:** 1.0
**Дата:** 2026-09-29
**Автор:** IIChatTools Team
**Статус:** **Draft** (согласование до реализации)
**Связанные KI:** KI-107 (Mail Agent — новый), KI-108 (per-user mail accounts — v1.8.x, новый)
**Целевой релиз:** v1.8.0 (глобальные credentials, App Password) → v1.8.x (per-user)

---

## § 1. Контекст

### § 1.1. Текущее состояние (после v1.7.1)

- **50 инструментов** в `ToolRegistry`: 40 raw + 6 агентов + 3 RAG + `database_agent`.
- **Chat видит 11 инструментов**: 7 агентов + 3 RAG + `database_agent`.
- **Multi-Agent** (v1.4.0): специализированные агенты по группам инструментов.
- **LLM не имеет доступа к почте** — нет ни одного инструмента для IMAP/SMTP.

### § 1.2. Industry best practices

| Практика | Источник | Применение в проекте |
|---|---|---|
| **MailKit** — де-факто стандарт для .NET | Jeffrey Stedfast (jstedfast/MailKit), Apache 2.0 | IMAP4 + SMTP + SSL/TLS + OAuth2 |
| **App Password** — простой способ без OAuth2 | Google, Yandex, Mail.ru | v1.8.0 — аутентификация |
| **Privacy-first** — не логировать содержимое | GDPR, RULES § 5.x | Только метаданные в AuditLogs |
| **Вложения в workspace** — sandbox | RULES § 1.9 | `mail-attachments/{userId}/{guid}.ext` |
| **Rate limiting на отправку** — защита от спама | OWASP ASVS 2.2.1 | `Mail:RateLimit:SendsPerHour` |

### § 1.3. Цели v1.8.0

| # | Цель | Метрика |
|---|---|---|
| 1 | LLM может **отправлять** письма | `send_email(to, subject, body, attachments?)` |
| 2 | LLM может **читать / искать** письма | `list_emails` / `read_email` / `search_emails` |
| 3 | LLM может **удалять / перемещать / помечать** | `delete_email` / `move_email` / `mark_as_read` |
| 4 | **Approval** на mutating (send/delete/move) | `RequiresApprovalByDefault = true` |
| 5 | **Privacy-first**: без PII в логах | AuditLogs — только метаданные |
| 6 | **Rate limiting** на отправку | `≤ 20 писем/час` (по умолчанию) |
| 7 | **Готовность к per-user** | Контракт `IMailAccountProvider` уже в v1.8.0 |

### § 1.4. Что НЕ входит в v1.8.0

- **Per-user credentials** — каждый юзер со своим ящиком. v1.8.x (KI-108).
- **OAuth2** (Gmail, Outlook) — App Password в v1.8.0, OAuth2 в v1.9+.
- **POP3** — только IMAP4 (POP3 устарел, не поддерживает папки/flags).
- **HTML-редактор** — body принимается как Markdown → HTML (простая конвертация).
- **Папки / метки** (кроме базовых: INBOX, Sent, Trash, Drafts).
- **Календарь / контакты** — только почта.

---

## § 2. Проблема

### § 2.1. LLM не имеет доступа к почте

**Пример:** пользователь спрашивает «Прочитай последнее письмо от Иванова и подготовь ответ».

**Сейчас:** LLM не может этого сделать. Нет инструментов для IMAP/SMTP.
`execute_command` + `python -c "import imaplib..."` — теоретически, но:
- нет валидации,
- нет approval на отправку,
- нет rate limiting,
- нет audit trail,
- небезопасно (credentials в command line).

**Хочется:** LLM вызывает `mail_agent(task="Прочитай последнее письмо от ivanov@... и подготовь ответ")`
→ агент внутри использует `list_emails`, `read_email`, `send_email` (с approval).

### § 2.2. Почему MailKit, а не System.Net.Mail

| Аспект | `System.Net.Mail.SmtpClient` | `MailKit` |
|---|---|---|
| SMTP | ✅ | ✅ |
| IMAP4 | ❌ | ✅ |
| POP3 | ❌ | ✅ |
| SSL/TLS | ✅ (ограниченно) | ✅ (полный контроль) |
| OAuth2 | ❌ | ✅ |
| Async | ❌ | ✅ |
| Работа с вложениями | базовая | полноценная |
| Microsoft-статус | **obsolete** (не рекомендуется) | рекомендован MS |

**Решение:** `MailKit` 4.x (Apache 2.0) + `MimeKit` (транзитивно).

### § 2.3. Ограничения существующих инструментов

| Инструмент | Что делает | Чего не хватает |
|---|---|---|
| `web_search` | Ищет в интернете | Нет доступа к почте |
| `fetch_web_content` | Загружает страницу | Не IMAP/SMTP |
| `read_file` / `save_file` | Работа с ФС | Не почта |
| `execute_command` | Shell | Нет валидации, нет approval, нет audit |

Mail Agent заполняет пробел: **«безопасный доступ LLM к почте через IMAP4/SMTP»**.

---

## § 3. Решение

### § 3.1. Один агент `mail_agent` (не 6 top-level tools)

По аналогии с `web_agent`, `git_agent` и т.д. — Chat видит **один инструмент `mail_agent`**,
внутри которого **6–7 специализированных инструментов**.

**Почему так:**
- Chat видит **11 инструментов** в v1.7.1. Добавление 6 top-level tools раздуло бы список до 17 — модель начнёт путаться (KI-052).
- Внутри агента — узкий набор + свой system prompt → точнее выбор.
- `mail_agent` наследуется от `AgentToolBase` — получает multi-turn loop, `MaxSteps`, `AllowedTools`.

**Имя агента:** `mail_agent`
**DisplayName:** «Почтовый агент»
**Модель:** `qwen/qwen3-4b-2507` (как у `web_agent` — задача простая).
**MaxSteps:** 10.
**RequiresApproval:** `true` (на уровне агента — 1 модалка на всю задачу).

### § 3.2. Глобальные credentials (v1.8.0) → per-user (v1.8.x)

**v1.8.0:** Один почтовый ящик на всё приложение (например, `noreply@company.local`).
Credentials — в **User Secrets** (`Mail:Imap:Password` + `Mail:Smtp:Password` или `Mail:AppPassword`).

**v1.8.x (KI-108):** Каждый пользователь — свой ящик.
Таблица `UserMailAccount` (userId, imapHost, smtpHost, username, encryptedPassword).
UI в `/profile → Почта`.

**Контракт готов уже в v1.8.0:**
```csharp
/// <summary>
/// Провайдер учётных данных для почтового ящика.
/// v1.8.0: глобальные creds из конфига (Singleton).
/// v1.8.x: per-user creds из UserMailAccount (Scoped).
/// </summary>
public interface IMailAccountProvider
{
    Task<MailAccountCredentials> GetAsync(int userId, CancellationToken ct = default);
}

public class MailAccountCredentials
{
    public string ImapHost { get; set; }
    public int ImapPort { get; set; }
    public bool ImapUseSsl { get; set; }
    public string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public bool SmtpUseSsl { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string FromAddress { get; set; }
    public string FromDisplayName { get; set; }
}
```
**v1.8.0:** `GlobalMailAccountProvider : IMailAccountProvider` — всегда возвращает одни и те же creds из `IOptions<MailOptions>`.
**v1.8.x:** `PerUserMailAccountProvider : IMailAccountProvider` — читает `UserMailAccount` по `userId`.
Замена — 1 строка в DI.

### § 3.3. Гибридный подход: App Password (v1.8.0) + OAuth2 (v1.9+)

**v1.8.0 — App Password:**
- Пользователь включает 2FA в почте (Google / Yandex / Mail.ru).
- Генерирует «App Password» (16 символов для Google, 16 для Yandex, ...).
- Вводит его в User Secrets.
- Приложение использует его как обычный пароль в IMAP/SMTP.

**Плюсы:**
- Работает без OAuth2 (сложная настройка Google Cloud Project / Azure AD App).
- Работает в России (Yandex, Mail.ru — App Password не требует VPN).
- Не требует интерактивного логина в браузере.

**Минусы:**
- App Password = полный доступ к ящику (нет granular scopes).
- Не отзывается автоматически при смене пароля.

**v1.9+ — OAuth2** (для Gmail / Outlook / корпоративных Exchange):
- Отдельный design-документ.
- Требует MSAL / Google.Apis.Auth.
- Хранение refresh tokens в БД.

### § 3.4. Privacy-first (без PII в логах)

**Жёсткие правила (RULES § 5.x):**
- **Не логировать содержимое письма** — ни `Body`, ни `Subject` (могут содержать PII).
- **Не логировать адреса** (From/To/Cc) — только количество получателей.
- **AuditLog** — только метаданные: `{ toolName, action, success, durationMs, uid, hasAttachments }`.
- **Не сохранять письма в `ChatMessage.MetadataJson`** — только для LLM в `tool_result`.
- **В `ILogger`** — только: `{ action, uid, bytesCount, attachmentsCount }`.

**Почему:** если пользователь прочитал письмо с паспортными данными, они не должны попасть в:
- JSONL-логи (`logs/audit/*.jsonl`),
- Serilog-вывод,
- БД `AuditLogs.ParametersJson`.

### § 3.5. Вложения — только workspace

**send_email:**
- Вложения — из workspace пользователя (`{UserWorkspace}/...`).
- Проверка через `PathHelper.TryGetSafeFullPath` (RULES § 1.9).
- Максимум: **≤ 10 MB** на письмо (суммарно).
- Максимум: **≤ 5 вложений**.
- Имена файлов нормализуются (`Path.GetFileName`) — защита от path-traversal.

**read_email:**
- Вложения сохраняются в `{UserWorkspace}/mail-attachments/{uid}/{guid}.ext`.
- Где `uid` — IMAP UID письма (уникален в папке).
- Папка создаётся автоматически.
- **Не** перезаписываются при повторном чтении (по hash содержимого).

**Rate limiting вложений:** не более 50 MB в час на пользователя.

---

## § 4. Архитектура

### § 4.1. Слои (`IIChatTools.Services`)

    IIChatTools.Services/
      ├── DTO/Mail/
      │   ├── MailAccountCredentials.cs       — { imapHost, ..., password }
      │   ├── MailMessageSummaryDto.cs        — { uid, from, subject, date, isRead, hasAttachments }
      │   ├── MailMessageDto.cs               — { uid, from, to[], subject, date, bodyText, bodyHtml, attachments[] }
      │   ├── MailAttachmentDto.cs            — { fileName, contentType, sizeBytes, storagePath? }
      │   ├── SendMailRequest.cs              — { to[], cc[], bcc[], subject, body, attachments[] }
      │   ├── SearchMailRequest.cs            — { mailbox?, from?, subject?, since?, before?, unseenOnly? }
      │   ├── MailOptions.cs                  — bind из appsettings:Mail
      │   └── MailRateLimitOptions.cs         — подсекция Mail:RateLimit
      │
      ├── Interfaces/
      │   ├── IMailClient.cs                  — обёртка над MailKit (Singleton)
      │   ├── IMailAccountProvider.cs         — creds provider (v1.8.0 — Global)
      │   ├── IMailAttachmentService.cs       — сохранение вложений в workspace
      │   └── IMailRateLimiter.cs             — rate limiting на отправку
      │
      ├── Implementation/Mail/
      │   ├── MailKitClient.cs                — Singleton, IDisposable
      │   ├── GlobalMailAccountProvider.cs    — Singleton (v1.8.0)
      │   ├── MailAttachmentService.cs        — Scoped
      │   └── InMemoryMailRateLimiter.cs      — Singleton, ConcurrentDictionary<userId, queue>
      │
      └── Implementation/Tools/Mail/
        ├── SendEmailTool.cs                  — mutating (approval)
        ├── ListEmailsTool.cs                 — read-only
        ├── ReadEmailTool.cs                  — read-only
        ├── SearchEmailsTool.cs               — read-only
        ├── DeleteEmailTool.cs                — mutating (approval)
        ├── MoveEmailTool.cs                  — mutating (approval)
        └── MarkAsReadTool.cs                 — mutating (approval, но маленький — см. § 6.1)

**`MailKitClient`** — Singleton, обёртка:

    public interface IMailClient
    {
        Task<IImapClient> GetImapClientAsync(int userId, CancellationToken ct = default);
        Task SendAsync(SendMailRequest request, int userId, CancellationToken ct = default);
        Task<bool> TestConnectionAsync(int userId, CancellationToken ct = default);
    }

**Пул соединений:**
- MailKit `ImapClient` — не потокобезопасен.
- Решение: `ConcurrentDictionary<int, ImapClient>` (per-user), TTL 5 минут.
- При истечении TTL — `DisposeAsync` + удаление из словаря.
- При разрыве — автоматический reconnect в `GetImapClientAsync`.
- Cleanup — `Timer` каждые 2 минуты (по образцу KI-043).

### § 4.2. DI-регистрация (`Startup.cs`)

    // 1. Опции
    services.Configure<MailOptions>(Configuration.GetSection("Mail"));
    services.Configure<MailRateLimitOptions>(Configuration.GetSection("Mail:RateLimit"));

    // 2. Инфраструктура
    services.AddSingleton<IMailClient, MailKitClient>();
    services.AddSingleton<IMailRateLimiter, InMemoryMailRateLimiter>();

    // 3. Credentials provider (v1.8.0 — Global; v1.8.x — PerUser)
    services.AddSingleton<IMailAccountProvider, GlobalMailAccountProvider>();

    // 4. Сервисы
    services.AddScoped<IMailAttachmentService, MailAttachmentService>();

    // 5. Tools (только если Mail:Enabled = true)
    if (Configuration.GetValue<bool>("Mail:Enabled"))
    {
        RegisterMailTools(services);
    }

**`RegisterMailTools(services)`** — новый private метод (по образцу `RegisterSqlAgentTools`):

    private static void RegisterMailTools(IServiceCollection services)
    {
        services.AddScoped<ITool, SendEmailTool>();
        services.AddScoped<ITool, ListEmailsTool>();
        services.AddScoped<ITool, ReadEmailTool>();
        services.AddScoped<ITool, SearchEmailsTool>();
        services.AddScoped<ITool, DeleteEmailTool>();
        services.AddScoped<ITool, MoveEmailTool>();
        services.AddScoped<ITool, MarkAsReadTool>();
    }

**`mail_agent`** — в `SubAgents` секции `appsettings.json` (см. § 5.1).

### § 4.3. Tools (7 штук внутри агента)

| Tool | Approval | Назначение | Параметры |
|---|:---:|---|---|
| `send_email` | ✅ | Отправка письма | `to[]`, `cc[]?`, `bcc[]?`, `subject`, `body`, `attachments[]?` |
| `list_emails` | — | Список писем из папки | `mailbox?="INBOX"`, `count=20`, `unseenOnly=false` |
| `read_email` | — | Чтение письма по UID | `uid`, `mailbox?="INBOX"`, `saveAttachments=true` |
| `search_emails` | — | Поиск с фильтрами | `from?`, `subject?`, `since?`, `before?`, `unseenOnly?` |
| `delete_email` | ✅ | Удаление (в Trash) | `uid`, `mailbox?="INBOX"` |
| `move_email` | ✅ | Перемещение в папку | `uid`, `from`, `to` |
| `mark_as_read` | ⚠️ | Пометка прочтения | `uid`, `mailbox?="INBOX"` |

**`mark_as_read` — особый случай:**
- **Не** требует approval сам по себе (действие мелкое).
- **Но** approval уже сработал на уровне `mail_agent` (`RequiresApprovalByDefault = true`).
- Итог: 1 модалка «Почтовый агент хочет выполнить задачу…» → Approve → все 7 actions внутри.

**Формат ответа** (пример `list_emails`):

    {
      "success": true,
      "data": {
        "mailbox": "INBOX",
        "count": 3,
        "messages": [
          {
            "uid": 12345,
            "from": "ivanov@example.com",
            "subject": "Счёт за октябрь",
            "date": "2026-09-28T14:30:00Z",
            "isRead": false,
            "hasAttachments": true
          }
        ]
      }
    }

**Формат ответа `send_email`:**

    {
      "success": true,
      "data": {
        "to": ["ivanov@example.com"],
        "subject": "Re: Счёт за октябрь",
        "attachmentsCount": 1,
        "bytesSent": 245680,
        "sentAt": "2026-09-29T15:42:00Z"
      },
      "message": "Письмо отправлено."
    }

---

## § 5. Конфигурация

### § 5.1. `appsettings.json` — секция `Mail`

    "Mail": {
      "Enabled": false,
      "DefaultMailbox": "INBOX",

      "Imap": {
        "Host": "imap.gmail.com",
        "Port": 993,
        "UseSsl": true,
        "TimeoutSeconds": 30
      },
      "Smtp": {
        "Host": "smtp.gmail.com",
        "Port": 465,
        "UseSsl": true,
        "TimeoutSeconds": 30
      },

      "FromAddress": "agent@example.com",
      "FromDisplayName": "IIChatTools Agent",

      "Attachments": {
        "MaxFileSizeBytes": 10485760,
        "MaxTotalSizeBytes": 10485760,
        "MaxFilesPerMessage": 5,
        "StorageSubfolder": "mail-attachments",
        "MaxHourlyBytesPerUser": 52428800
      },

      "RateLimit": {
        "SendsPerHour": 20,
        "SendsPerMinute": 2,
        "ReadsPerMinute": 30
      },

      "AllowedMailboxes": ["INBOX", "Sent", "Drafts", "Trash", "Archive"],

      "Search": {
        "DefaultLimit": 20,
        "MaxLimit": 100
      }
    }

### § 5.2. `SubAgents` — `mail_agent`

Добавить в `SubAgents` секцию (по образцу v1.4.0):

    "mail_agent": {
      "Enabled": true,
      "DisplayName": "Почтовый агент",
      "Description": "Работа с электронной почтой: чтение, поиск, отправка, управление папками (IMAP/SMTP).",
      "Model": "qwen/qwen3-4b-2507",
      "MaxSteps": 10,
      "RequiresApproval": true,
      "SystemPrompt": "Ты — почтовый агент IIChatTools. Твоя задача — работать с электронной почтой через IMAP/SMTP.\n\nКРИТИЧЕСКИЕ ПРАВИЛА:\n1. НИКОГДА не отправляй письма без явной просьбы пользователя.\n2. Перед отправкой ВСЕГДА подтверждай адресата и тему в финальном ответе.\n3. Не отправляй письма более чем на 10 адресатов за раз.\n4. Если пользователь просит 'ответить' — сначала прочитай исходное письмо через read_email, используй его Subject как 'Re: <Subject>'.\n5. Не раскрывай содержимое писем другим пользователям.\n6. Отвечай на русском языке.",
      "AllowedTools": [
        "send_email",
        "list_emails",
        "read_email",
        "search_emails",
        "delete_email",
        "move_email",
        "mark_as_read"
      ]
    }

### § 5.3. User Secrets — credentials

**Dev (Yandex — самый простой в РФ):**

    cd C:\Projects\AI\IIChatTools\IIChatTools.API

    dotnet user-secrets set "Mail:Imap:Host" "imap.yandex.ru"
    dotnet user-secrets set "Mail:Smtp:Host" "smtp.yandex.ru"
    dotnet user-secrets set "Mail:FromAddress" "you@yandex.ru"
    dotnet user-secrets set "Mail:Imap:Username" "you@yandex.ru"
    dotnet user-secrets set "Mail:Imap:Password" "<app-password>"
    dotnet user-secrets set "Mail:Smtp:Username" "you@yandex.ru"
    dotnet user-secrets set "Mail:Smtp:Password" "<app-password>"
    dotnet user-secrets set "Mail:Enabled" "true"

### § 5.4. Как получить App Password (RU-специфика)

**Yandex (рекомендуется в РФ):**
1. https://id.yandex.ru/security
2. «Пароли приложений» → «Почта» → «Создать пароль».
3. Выбрать: «IMAP-клиент».
4. Скопировать 16-символьный пароль → в `Mail:Imap:Password`.

**Mail.ru:**
1. https://account.mail.ru/user/2-step-auth/passwords
2. Включить 2FA (если ещё не).
3. «Пароли для внешних приложений» → «Создать».
4. Выбрать «IMAP» → скопировать.

**Gmail (требует VPN в РФ):**
1. https://myaccount.google.com/security
2. «Двухэтапная аутентификация» → включить.
3. «Пароли приложений» → «Почта» → «Другое».
4. Скопировать 16-символьный пароль.
5. **⚠️ В РФ:** Google может требовать VPN для доступа к странице. IMAP/SMTP через `imap.gmail.com:993` обычно работает без VPN, но первый логин может требовать подтверждения.

**Корпоративная почта (Exchange / Office 365):** App Password доступен только при включённом MFA в Azure AD. Часто проще OAuth2 (v1.9+).

### § 5.5. Admin override через AppSettings

По аналогии с `SubAgents.*` и `SqlAgent.*`:
- Ключи `Mail.*` — редактируются через `/admin → Почта` (новая вкладка, v1.8.x).
- **Пароль в БД не хранится** — только в User Secrets / env.
- Override возможен для: `Mail:Enabled`, `Mail:RateLimit:*`, `Mail:Attachments:*`, `Mail:AllowedMailboxes`.

### § 5.6. Правила валидации конфигурации

При старте (если `Mail:Enabled = true`):

| Проверка | Действие |
|---|---|
| `Mail:Imap:Host` не пуст | `InvalidOperationException` |
| `Mail:Smtp:Host` не пуст | `InvalidOperationException` |
| `Mail:FromAddress` не пуст | `InvalidOperationException` |
| `Mail:FromAddress` валидный email | Warning |
| `Mail:Imap:Username` / `Password` разрешены | `InvalidOperationException` |
| `Mail:Imap:Port` ∈ [1, 65535] | Clamp + warning |
| `Mail:RateLimit:SendsPerHour` ∈ [1, 1000] | Clamp + warning |
| `Mail:Attachments:MaxFileSizeBytes` ≤ 25 MB | Warning (SMTP-лимит большинства провайдеров — 25 MB) |
| `Mail:AllowedMailboxes` не пуст | Warning (fallback на `["INBOX"]`) |

**Принцип:** критичные ошибки (нет host / нет creds) → fail fast. Некритичные (валидация email, clamp) → warning.

---

## § 6. Безопасность

### § 6.1. Approval (5 уровней защиты)

| Tool | `RequiresApprovalForCall` | Обоснование |
|---|:---:|---|
| `send_email` | ✅ | Отправка от лица пользователя — критично |
| `delete_email` | ✅ | Удаление письма — необратимо (без Trash) |
| `move_email` | ✅ | Может переместить в неправильную папку |
| `mark_as_read` | ❌ | Мелкое действие, но внутри `mail_agent` (approval уже был) |
| `list_emails` | ❌ | Read-only |
| `read_email` | ❌ | Read-only |
| `search_emails` | ❌ | Read-only |

**Двойная защита:**
1. `mail_agent` сам имеет `RequiresApprovalByDefault = true` — **1 модалка** на всю задачу.
2. Внутри `AgentToolBase` — все mutating actions вызываются уже «внутри доверенного контекста».

**Итог:** Пользователь видит 1 модалку: «Почтовый агент хочет выполнить задачу: Прочитай письмо от Иванова и ответь». → Approve → агент работает.

### § 6.2. Privacy (PII)

**Жёсткие правила:**
- **Не логировать** содержимое писем (`Body`, `HtmlBody`).
- **Не логировать** адреса (`From`, `To`, `Cc`, `Bcc`).
- **AuditLog.ParametersJson** — только: `{ action, uid, mailbox, attachmentsCount, recipientsCount }`.
- **AuditLog.ResultJson** — только: `{ success, bytesSent, messageUid, durationMs }`.
- **ILogger** — только: `LogInformation("Mail: {Action} uid={Uid} attachments={Attachments}")`.
- **В `ChatMessage.MetadataJson`** письма не сохраняются (только Sources / citations от RAG).

### § 6.3. Вложения — workspace-only

**При отправке:**
- Файлы только из `{UserWorkspace}/...`.
- `PathHelper.TryGetSafeFullPath` (защита от `..\..\`).
- Имя нормализуется (`Path.GetFileName`) — защита от `C:\Windows\...` в имени.
- Лимит: ≤ 10 MB на файл, ≤ 10 MB суммарно, ≤ 5 файлов.

**При чтении:**
- Файлы сохраняются в `{UserWorkspace}/mail-attachments/{uid}/{guid}.ext`.
- **`{uid}`** — IMAP UID письма (уникален в папке).
- Hash содержимого — для дедупликации (не перезаписываем при повторном чтении).
- Максимум: ≤ 25 MB на письмо.

### § 6.4. Rate limiting

| Лимит | Значение | Защита от |
|---|:---:|---|
| `SendsPerHour` | 20 | Спам / утечка через массовую отправку |
| `SendsPerMinute` | 2 | Флуд (LLM может зациклиться) |
| `ReadsPerMinute` | 30 | DoS на IMAP-сервер |
| `MaxHourlyBytesPerUser` | 50 MB | Отправка больших вложений пачкой |

**Реализация:** `InMemoryMailRateLimiter` (Singleton) — `ConcurrentDictionary<int, Queue<DateTime>>`.
Cleanup — `Timer` каждые 5 минут (по образцу KI-043).

**При превышении:** `ToolResult.Fail("Превышен лимит отправки: 20 писем/час. Повторите через N минут.")`.

### § 6.5. Аудит (без содержимого)

Каждый вызов mail-tool пишет запись в `AuditLogs`:

**`send_email`:**

    {
      "toolName": "mail.send_email",
      "userId": 1,
      "status": "Success",
      "durationMs": 420,
      "parametersJson": { "recipientsCount": 2, "attachmentsCount": 1, "bytesSent": 245680 },
      "resultJson": { "success": true, "messageId": "<...@mail.yandex.ru>" }
    }

**`read_email`:**

    {
      "toolName": "mail.read_email",
      "userId": 1,
      "status": "Success",
      "durationMs": 180,
      "parametersJson": { "uid": 12345, "mailbox": "INBOX", "saveAttachments": true },
      "resultJson": { "success": true, "bodyLength": 2048, "attachmentsCount": 1 }
    }

**Что НЕ попадает в аудит:** Subject, From, To, Body, имена вложений (могут содержать PII).

### § 6.6. Сводная таблица угроз и защит

| Угроза | Защита | Обходится? |
|---|---|---|
| Отправка спама от лица пользователя | Approval + RateLimit (20/час) | ❌ |
| Утечка credentials | User Secrets / env (не в git) | ❌ |
| Path-traversal через вложение | `PathHelper.TryGetSafeFullPath` | ❌ |
| DoS на IMAP через частые reads | RateLimit (30/мин) | ❌ |
| Отправка огромного письма | Лимит 10 MB на письмо | ❌ |
| Логирование PII | Только метаданные в audit | ❌ |
| Рекурсивная отправка (LLM зациклился) | `MaxSteps` (10) + RateLimit | ❌ |
| Подмена адресата | LLM не имеет прямого доступа, только через tool | ❌ |
| Чтение чужой почты | В v1.8.0 — глобальный ящик (1 на всё). В v1.8.x — фильтр по userId | ⚠️ v1.8.0 (по дизайну) |
| Перехват письма | SSL/TLS (порт 993/465) | ❌ |

**Открытое ограничение v1.8.0:** глобальные creds → все пользователи работают с **одним ящиком**.
Это **осознанное решение** для MVP. Per-user — v1.8.x (KI-108).
---

## § 7. План фаз (0–6)

**Оценка:** ~10–12 ч (≈2 рабочих дня).

| Фаза | Что | Оценка | Зависимости |
|:---:|---|:---:|---|
| **0** | DESIGN (этот документ) | — | ✅ **Done (2026-09-29)** |
| **1** | NuGet (MailKit) + DTO + IMailClient (скелет) | 2 ч | Фаза 0 |
| **2** | MailKitClient + GlobalMailAccountProvider + TestConnection | 2.5 ч | Фаза 1 |
| **3** | 7 tools (send/list/read/search/delete/move/mark) | 3 ч | Фаза 2 |
| **4** | MailAttachmentService + RateLimiter | 2 ч | Фаза 2 |
| **5** | mail_agent (SubAgentDescriptor) + Chat integration | 1 ч | Фаза 3 |
| **6** | Тесты + документация + релиз v1.8.0 | 2.5 ч | Фазы 1-5 |

### § 7.1. Фаза 1 — NuGet + DTO + скелет (2 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 1.1 | `Directory.Build.props`: `<MailKitVersion>4.8.0</MailKitVersion>` | — |
| 1.2 | `IIChatTools.Services.csproj`: `<PackageReference Include="MailKit" ... />` | — |
| 1.3 | DTO/Mail/ (8 файлов) | — |
| 1.4 | Interfaces/ (4 интерфейса) | — |

**DoD:** `dotnet build` 0/0. Все DTO/интерфейсы компилируются.

### § 7.2. Фаза 2 — MailKitClient + Provider (2.5 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 2.1 | `MailKitClient.cs` — IMAP-пул + SMTP-отправка | `MailKitClientSmokeTests` (3) |
| 2.2 | `GlobalMailAccountProvider.cs` | — |
| 2.3 | `Startup.cs`: DI-регистрация | — |
| 2.4 | `appsettings.json` + `.Development.json`: секция `Mail` | — |
| 2.5 | User Secrets инструкция (README) | — |

**DoD:** `IMailClient.TestConnectionAsync(userId: 1)` возвращает `true` для реального ящика (Yandex).

### § 7.3. Фаза 3 — 7 tools (3 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 3.1 | `SendEmailTool.cs` | 4 теста (валидация + успех + approval) |
| 3.2 | `ListEmailsTool.cs` | 3 теста |
| 3.3 | `ReadEmailTool.cs` | 4 теста (с вложениями / без) |
| 3.4 | `SearchEmailsTool.cs` | 3 теста |
| 3.5 | `DeleteEmailTool.cs` + `MoveEmailTool.cs` | 4 теста |
| 3.6 | `MarkAsReadTool.cs` | 2 теста |
| 3.7 | `RegisterMailTools(services)` в Startup.cs | — |

**DoD:** 20+ тестов на 7 tools. Все зелёные.

### § 7.4. Фаза 4 — Attachments + RateLimiter (2 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 4.1 | `MailAttachmentService.cs` | 6 тестов (лимиты, path-traversal, дедупликация) |
| 4.2 | `InMemoryMailRateLimiter.cs` | 4 теста (per-hour, per-minute, cleanup) |
| 4.3 | DI-регистрация | — |

**DoD:** `SendEmailTool` с вложением > 10 MB → `ToolResult.Fail`. 21-е письмо/час → `Fail`.

### § 7.5. Фаза 5 — mail_agent (1 ч)

| Шаг | Файлы | Тесты |
|---|---|---|
| 5.1 | `SubAgents:mail_agent` в `appsettings.json` | — |
| 5.2 | `ChatStreamService`: `mail_agent` в `allowedNames` (RULES § 4.44!) | — |
| 5.3 | Smoke: `curl /api/tools/execute` с `mail_agent` | — |
| 5.4 | Smoke через Chat UI | — |

**DoD:** Chat видит **12 инструментов** (было 11). LLM вызывает `mail_agent` для задачи по почте.

### § 7.6. Фаза 6 — Тесты + документация + релиз (2.5 ч)

| Шаг | Что |
|---|---|
| 6.1 | Все unit-тесты зелёные (**+~25**) |
| 6.2 | README: раздел «Mail Agent» |
| 6.3 | CHANGELOG `[1.8.0]` |
| 6.4 | KNOWN_ISSUES: KI-107 → Fixed; KI-108 → Planned (v1.8.x) |
| 6.5 | RULES § 7 (KI-выжимка) + § 8 |
| 6.6 | DESIGN.md → статус **Implemented** |
| 6.7 | Tag `v1.8.0` + GitHub Release |

---

## § 8. Definition of Done (v1.8.0)

### § 8.1. Функциональные требования

- [ ] MailKit 4.8.0 подключён (Apache 2.0).
- [ ] `IMailClient` + `MailKitClient` (Singleton, IDisposable) — IMAP-пул + SMTP.
- [ ] `IMailAccountProvider` + `GlobalMailAccountProvider` (v1.8.0).
- [ ] 7 tools реализованы: send/list/read/search/delete/move/mark.
- [ ] `mail_agent` в `SubAgents` + в `allowedNames` Chat.
- [ ] Chat видит **12 инструментов** (было 11).
- [ ] Approval: 1 модалка на `mail_agent`, mutating actions — внутри.
- [ ] `MailAttachmentService` — вложения в `mail-attachments/{uid}/`.
- [ ] `InMemoryMailRateLimiter` — 20 писем/час, 30 reads/мин.
- [ ] Privacy: без PII в логах / audit / ChatMessage.MetadataJson.
- [ ] Config validation при старте (fail fast на критичных).

### § 8.2. Нефункциональные

- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — **~400/400** (+~25 новых).
- [ ] CI + Docker Publish — зелёные.
- [ ] RULES § 7 + § 8 обновлены.
- [ ] CHANGELOG `[1.8.0] — YYYY-MM-DD`.
- [ ] README раздел «Mail Agent».
- [ ] DESIGN.md статус **Implemented**.

### § 8.3. Smoke (7 сценариев)

| # | Сценарий | Ожидание |
|---|---|---|
| 1 | `/admin → Status` — увидеть Mail в зависимостях (если Enabled) | ✅ или ⚠️ |
| 2 | `curl /api/tools/execute mail_agent(list_emails)` | JSON со списком |
| 3 | Chat: «Прочитай последнее письмо» | LLM → `mail_agent` → approval → чтение |
| 4 | Chat: «Отправь письмо X на Y» | LLM → `mail_agent` → approval → отправка |
| 5 | Чтение письма с вложением | Файл в `mail-attachments/{uid}/` |
| 6 | 21-е письмо за час | `Fail: Превышен лимит` |
| 7 | Вложение > 10 MB | `Fail: Файл слишком большой` |

### § 8.4. Документация

- [ ] README.md — раздел «Mail Agent».
- [ ] CHANGELOG.md — `[1.8.0]`.
- [ ] KNOWN_ISSUES.md — KI-107 → Fixed; KI-108 → Planned.
- [ ] TESTING.md — smoke-сценарии для Mail Agent.
- [ ] RULES.md — обновлён (если есть новые уроки).

---

## § 9. Ссылки

### § 9.1. KI

- **KI-107** — Mail Agent (этот документ, v1.8.0).
- **KI-108** — Per-user mail accounts (v1.8.x).
- **KI-052** — Multi-Agent (эталон для SubAgentDescriptor).
- **KI-097** — Database Agent (эталон для SQL-tools).

### § 9.2. Правила (RULES.md)

- § 1.9 — `PathHelper` для работы с путями.
- § 1.14 — локализация (RU + EN).
- § 1.15 — новый инструмент = 1 класс + 1 строка регистрации.
- § 4.17 — JS-локализация через `data-*`.
- § 4.44 — новый top-level ITool → `allowedNames`.
- § 5.x — User Secrets, без PII в логах.

### § 9.3. Внешние источники

- [MailKit](https://github.com/jstedfast/MailKit) — Apache 2.0, де-факто стандарт .NET IMAP/SMTP.
- [MimeKit](https://github.com/jstedfast/MimeKit) — Apache 2.0, парсинг MIME.
- [Yandex App Passwords](https://yandex.ru/support/id/authorization/app-passwords.html).
- [Mail.ru App Passwords](https://help.mail.ru/mail/security/app-password).
- [Google App Passwords](https://support.google.com/accounts/answer/185833).
- [OWASP ASVS 2.2.1](https://owasp.org/www-project-application-security-verification-standard/).

### § 9.4. Внутренние документы

- `docs/development/v1.4/DESIGN.md` — Multi-Agent (эталон SubAgentDescriptor).
- `docs/development/v1.7/DESIGN_DB_AGENT.md` — Database Agent (эталон для tools + admin UI).
- `docs/development/RULES.md` — правила (v1.4.20).
- `docs/KNOWN_ISSUES.md` — реестр проблем.

---

## § 10. Приложения

### Приложение A — пример `send_email` через LLM

**Задача пользователя:** «Отправь письмо на ivanov@example.com с темой "Счёт за октябрь" и приложи файл invoice.pdf из workspace».

**Что делает LLM:**
1. Вызывает `mail_agent(task="Отправить письмо ivanov@example.com, тема 'Счёт за октябрь', приложить invoice.pdf")`.
2. Approval: «Почтовый агент хочет выполнить задачу…» → Approve.
3. Внутри агента:
   - LLM вызывает `send_email(to=["ivanov@example.com"], subject="Счёт за октябрь", body="…", attachments=["invoice.pdf"])`.
   - `SendEmailTool` проверяет: файл есть в workspace, размер ≤ 10 MB.
   - `IMailRateLimiter.CheckAndIncrement(userId)` — OK.
   - `MailKitClient.SendAsync(...)` — отправляет.
4. Возврат: `{ success: true, message: "Письмо отправлено." }`.
5. Chat формулирует финальный ответ: «Письмо отправлено на ivanov@example.com с темой "Счёт за октябрь" и вложением invoice.pdf».

### Приложение B — пример `search_emails` через LLM

**Задача:** «Найди все письма от Иванова за последнюю неделю».

**Что делает LLM:**
1. Вызывает `mail_agent(task="Найти письма от Иванова за последнюю неделю")`.
2. Approval → Approve.
3. Внутри агента:
   - LLM вызывает `search_emails(from="ivanov", since="2026-09-22")`.
   - `SearchEmailsTool` конвертирует в IMAP SEARCH: `FROM "ivanov" SINCE 22-Sep-2026`.
   - Возвращает список UID с метаданными.
4. Chat формулирует: «Найдено 3 письма от Иванова за неделю: 1) …, 2) …, 3) …».

### Приложение C — структура `mail-attachments/{uid}/`

    Workspace/users/1/mail-attachments/
      ├── 12345/                          ← IMAP UID письма
      │   ├── a1b2c3d4-…-guid.pdf
      │   └── e5f6g7h8-…-guid.xlsx
      └── 12346/
        └── i9j0k1l2-…-guid.docx

- Папка `{uid}` создаётся автоматически при `read_email(saveAttachments=true)`.
- Имена файлов: `{guid}.ext` (защита от коллизий в оригинальных именах).
- Дедупликация: если файл с тем же hash уже есть — не перезаписываем.

### Приложение D — пример конфигурации для нескольких провайдеров (v1.8.x, per-user)

    "Mail": {
      "Enabled": true,
      "DefaultMailbox": "INBOX",
      "AccountProvider": "PerUser",       // v1.8.x: Global | PerUser

      "Providers": {
        "yandex": {
          "ImapHost": "imap.yandex.ru",
          "ImapPort": 993,
          "SmtpHost": "smtp.yandex.ru",
          "SmtpPort": 465,
          "UseSsl": true
        },
        "gmail": {
          "ImapHost": "imap.gmail.com",
          "ImapPort": 993,
          "SmtpHost": "smtp.gmail.com",
          "SmtpPort": 465,
          "UseSsl": true
        }
      }
    }

**Отличие v1.8.x:** пользователь выбирает провайдера в `/profile → Почта`, вводит свои creds.
`PerUserMailAccountProvider` возвращает creds по `userId` из таблицы `UserMailAccount`.
