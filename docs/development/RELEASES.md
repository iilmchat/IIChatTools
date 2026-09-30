# Процесс релизов IIChatTools

**Назначение:** единый чек-лист релиза. Дополняет `RULES.md § 2.7` (правила документации)
и `RULES.md § 6.4` (формат commit message).

**Типы релизов** (Semantic Versioning):
- **Major** (`X.0.0`) — новая крупная фича/фаза (v1.4.0 — Multi-Agent, v1.5.0 — RAG).
- **Minor** (`1.X.0`) — новая функциональность, обратносовместимая.
- **Patch** (`1.3.X`) — багфиксы, документация.

---

## § 1. Подготовка (за 1 день до релиза)

- [ ] Все запланированные KI закрыты (Fixed) или перенесены в следующий релиз.
- [ ] Все запланированные фазы завершены (для major — фазы 0-9).
- [ ] `dotnet build IIChatTools.sln` — **0 warnings, 0 errors**.
- [ ] `dotnet test IIChatTools.sln` — **все зелёные**.
- [ ] CI на `main` — зелёный (последний коммит).
- [ ] Docker Publish на `main` — зелёный.

---

## § 1a. Известные ограничения релиза v1.8.1

> **ОБЯЗАТЕЛЬНО прочитать перед деплоем.** Ниже — задокументированные
> долги, которые переносятся в v1.8.x. Они **не блокируют** выпуск v1.8.0,
> но должны быть учтены при развёртывании.

### Новое в v1.8.1

- **External-LLM Agent** (KI-109) — агент `external_llm_agent` + 3 инструмента
  (`ask_external_llm` с `compare_with`, `list_external_providers`,
  `check_internet_connection`). 5 OpenAI-совместимых провайдеров
  (DeepSeek / OpenAI / Groq / Together AI / Ollama). Budget guardrails
  ($5/день, 500k токенов), circuit breaker (3 fail → 5 мин skip),
  privacy-first (`include_context: false`, без PII в логах). Chat видит
  **13 инструментов** (было 12). API-ключи — User Secrets.

### Ранее в v1.8.0

- **Mail Agent** (KI-107) — почтовый агент `mail_agent` + 7 инструментов
  (`send_email`, `list_emails`, `read_email`, `search_emails`, `delete_email`,
  `move_email`, `mark_as_read`). IMAP/SMTP через MailKit 4.8.0. Rate limiting
  (20 писем/час, 30 чтений/мин), privacy-first (без PII в логах), вложения
  в `mail-attachments/{uid}/` ≤ 10 MB. Глобальные creds (App Password в
  User Secrets).

### Ограничения v1.8.1 (не блокеры)

- **External-LLM Agent:** non-streaming (без SSE с внешних API), без function
  calling на внешних API. Anthropic / Gemini — v1.9+ (KI-110). Per-user API keys
  — v1.8.x.
- **Один глобальный почтовый ящик** для всех пользователей. Per-user —
  v1.8.x (KI-108).
- **OAuth2 (Gmail / Outlook / Exchange)** — v1.9+.
- **Сохранение вложений при `read_email`** — отложено (требует переделки
  `IMailClient`). Метаданные вложений отдаются, но файлы в workspace не
  сохраняются.
- **Прикрепление вложений к `send_email`** — отложено. `ResolveForSendAsync`
  готов, но привязка к `MimeMessage` — v1.8.x.
- **POP3** — не поддерживается (только IMAP4).
- **SqlServer-миграции** — по-прежнему Deferred (KI-091). Dev — Sqlite.
- **PROMPT_V2.md** — разметка требует переработки (пост-релиз, отдельная задача).

### SqlServer-миграции НЕ применяются (KI-091 — Deferred)

**Симптом:** `dotnet ef database update` на свежей SqlServer-БД падает
с `Operand type clash`. План пересборки — в KI-091, перенесён на **v1.7.x**.

**Правила:**

- **Dev (рекомендуется):** `Database:Provider = "Sqlite"` (по умолчанию).
  Схема создаётся через `EnsureCreatedAsync`.
- **Prod / SqlServer:** не разворачивать в v1.7.0. Дождаться пересборки
  миграций в v1.7.x.

### Sqlite relative path — Fixed (KI-100)

`SqlConnectionProvider` теперь резолвит **относительный** `Data Source`
относительно `IWebHostEnvironment.ContentRootPath` (а не CWD). Работает
при запуске из любой директории, как служба, в Docker.

### Per-action approval через `ITool.RequiresApprovalForCall` (KI-101 — Fixed)

Метаданные (`list_databases` / `list_tables` / `describe_table`) — **без**
approval. `execute_query` — **с** approval. Реализовано через default interface
method в `ITool`. Все 46 старых инструментов работают без изменений.

### Admin UI локализация — Fixed (KI-102)

Все строки `/admin` переведены через `data-*` (RULES § 4.17). RU/EN
переключается корректно.

### `/status` локализация — KI-103 (Documented, план v1.7.x)

На странице `/status` часть UI остаётся на русском при переключении на EN
(hardcoded RU в `status.js`). **Не блокер.** План — v1.7.x (~1 час).

### Внешние БД (Postgres / MySQL / Oracle) — KI-099 (v1.8.0)

Database Agent работает только с собственной БД (`internal`). Архитектура
готова к внешним подключениям, но провайдеры добавятся в v1.8.0.

### Прочие ограничения (не блокеры)

- **PDF / DOCX / OCR** в RAG — v1.7.x.
- **Re-ranking (cross-encoder)** — v1.7.x.
- **Qdrant** (замена InMemoryVectorStore) — v1.7.x.
- **KI-092** (Bootstrap 5.2 `aria-hidden` warning) — не баг, нулевое влияние.

---

## § 2. Обновление документации (в одном коммите)

**Файлы — обязательны:**

| Файл | Что менять |
|------|------------|
| `Directory.Build.props` | `<Version>X.Y.Z</Version>` + `<Copyright>...v X.Y.Z</Copyright>` + `<!-- © ... vX.Y.Z -->` в шапке |
| `CHANGELOG.md` | `[Unreleased]` → `[X.Y.Z] — YYYY-MM-DD` + новая пустая `[Unreleased]` |
| `README.md` | `# IIChatTools vX.Y.Z`, Copyright, теги `ghcr.io` |
| `docs/development/RULES.md` | § 8 (история правил) — если добавлялись новые правила |
| `docs/KNOWN_ISSUES.md` | Обновить сводку по статусам |
| `docs/development/vX.Y/DESIGN.md` | Финальная версия (для major) |

**Не трогать** (правило 2.7, единственный источник истины):
- ❌ `AppVersion.cs` — читает из `AssemblyInformationalVersionAttribute` (MSBuild формирует из `<Version>`).

---

## § 3. Локальная сборка и проверка

```powershell
cd <repo-root>
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force --verbosity minimal
dotnet build IIChatTools.sln --no-restore
dotnet test IIChatTools.sln --no-build
```

**Проверка версии:**

```powershell
cd IIChatTools.API
dotnet run
# В логе:
# info: IIChatTools.API.Program[0]
#       === IIChatTools vX.Y.Z — запуск инициализации ===
#       © 2026 RuChating (iilmchat) · IIChatTools vX.Y.Z
# Ctrl+C
```

**UI** (F5 в браузере):
- Navbar: `IIChatTools vX.Y.Z`.
- Footer: `© 2026 RuChating (iilmchat) · IIChatTools vX.Y.Z`.
- Главная: `Добро пожаловать в IIChatTools vX.Y.Z`.

**Проверка известных ограничений (§ 1a):**

- [ ] Для v1.5.0: `Database:Provider = "Sqlite"` (dev). SqlServer-БД не разворачивается.
- [ ] Проверка работоспособности RAG: `/admin → База знаний → Обновить индекс проекта` (не через миграции).
- [ ] RAG-индексы в БД: `sqlite3 IIChatTools.API\Data\iichattools-dev.db "SELECT IndexName, COUNT(*) FROM DocumentChunks GROUP BY IndexName;"`

---

## § 4. Коммит

```powershell
git add -A
git status
# Проверить: только ожидаемые файлы (см. § 2)

@'
chore(release): vX.Y.Z — <тип> (<KI-1>, <KI-2>, ...)

- Directory.Build.props: <Version> X.Y.Z-1 -> X.Y.Z
- CHANGELOG.md: [Unreleased] -> [X.Y.Z] — YYYY-MM-DD
- README.md: badge + Copyright + ghcr tag (X.Y.Z)
- RULES.md § 8: версия правил
- KNOWN_ISSUES.md: сводка

Build 0/0. Tests N/N.
'@ | Out-File -FilePath .commit-msg.txt -Encoding utf8NoBOM
git commit -F .commit-msg.txt
Remove-Item .commit-msg.txt

git push origin main
```

**Ждём:** CI на `main` + Docker Publish на `main` (новый sha-тег).

---

## § 5. Тег

```powershell
git tag -a vX.Y.Z -m "vX.Y.Z — <краткое название>"
git push origin vX.Y.Z
```

**Ждём:** Docker Publish на тег `vX.Y.Z` — соберёт и опубликует:
- `ghcr.io/iilmchat/iichattools:vX.Y.Z`
- `ghcr.io/iilmchat/iichattools:X.Y.Z`
- `ghcr.io/iilmchat/iichattools:X.Y` (для minor/patch)
- `ghcr.io/iilmchat/iichattools:X` (для major)
- `ghcr.io/iilmchat/iichattools:latest` (для последнего стабильного)

---

## § 6. GitHub Release

**Создать временный файл** `.release-notes-vX.Y.Z.md` (в корне):

```markdown
## Что нового

<1-2 абзаца — суть релиза>

### KI-XXX — <название>
- <bullet>
- <bullet>

## Статистика

- KI: N Fixed, M In Progress, K Deferred.
- Тесты: N/N.
- CI + Docker Publish — зелёные.

## Образ

    docker pull ghcr.io/iilmchat/iichattools:vX.Y.Z

## Документация

- `docs/development/RULES.md` — версия.
- `CHANGELOG.md` — полный список изменений.
```

**Команды:**

```powershell
# .gitignore уже содержит .release-notes-*.md (см. v1.3.0)
gh release create vX.Y.Z `
  --title "vX.Y.Z — <краткое название>" `
  --notes-file .release-notes-vX.Y.Z.md `
  --verify-tag
Remove-Item .release-notes-vX.Y.Z.md
```

**Проверить на GitHub:**
- [ ] Release отображается в `https://github.com/iilmchat/IIChatTools/releases`.
- [ ] Помечен `Latest` (если это самый свежий).
- [ ] Тег привязан к правильному коммиту.
- [ ] Notes рендерятся (заголовки, списки, code blocks).

---

## § 7. Уведомление (опционально)

- [ ] Обновить docker-compose / деплой-скрипты клиентам (если применимо).
- [ ] Написать в команду (Telegram/Slack) — «релиз vX.Y.Z доступен».

---

## § 8. Что пошло не так — как исправлять

### Пропущен GitHub Release
Тег есть, но в `/releases` нет записи.

**Fix (retroactive):**

```powershell
gh release create vX.Y.Z --title "..." --notes-file ... --verify-tag
```

### Пропущен тег
Релиз не помечен.

**Fix:**

```powershell
git tag -a vX.Y.Z -m "..." <commit-sha>
git push origin vX.Y.Z
```

### Неправильная версия в UI/логах
- Проверить: `<Version>` в `Directory.Build.props`.
- Убедиться, что `AppVersion.cs` **не содержит хардкода** (правило 2.7).
- Пересобрать: `dotnet build` (AssemblyInformationalVersion формируется MSBuild).

### Docker-образ не собрался на тег
Проверить:
- Workflow `docker-publish.yml` — триггер `push: tags: ['v*.*.*']`.
- Тег соответствует шаблону `v*.*.*` (`v1.3.1` — ок, `1.3.1` без `v` — нет).

### Расхождение `latest` и последнего релиза
`docker-publish.yml` должен ставить `latest` только для последнего стабильного тега.
Если `latest` откатился на старый — перезапустить workflow для нужного тега.

---

## § 9. Автоматизация (TODO — v1.4.x)

Сейчас GitHub Release создаётся **вручную** (`gh release create`). Риск: забыть.

**Решение:** добавить `.github/workflows/release.yml`:

```yaml
name: Create GitHub Release
on:
  push:
    tags: ['v*.*.*']
jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Extract version from tag
        id: version
        run: echo "tag=${GITHUB_REF#refs/tags/}" >> $GITHUB_OUTPUT
      - uses: softprops/action-gh-release@v2
        with:
          name: "${{ steps.version.outputs.tag }}"
          generate_release_notes: true
          draft: false
          prerelease: false
```

После добавления — `gh release create` станет **не нужен**, релиз создастся автоматически при push тега.

**Запланировано:** v1.4.x (после KI-053).

---

## § 10. Ссылки

- `RULES.md § 2.7` — обновление `<Version>` и `AppVersion`.
- `RULES.md § 2.10` — разметка Markdown-файлов (4 бэктика снаружи).
- `RULES.md § 6.4` — формат commit message.
- `RULES.md § 3.15` — проверка `Database:Provider` перед миграциями.
- `CHANGELOG.md` — Keep a Changelog.
- `docs/KNOWN_ISSUES.md` — реестр проблем (**KI-091** — SqlServer-миграции — обязательный пункт перед деплоем).
- [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/)
- [Semantic Versioning](https://semver.org/lang/ru/)
