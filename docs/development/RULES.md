# Правила разработки IIChatTools

**Версия:** 1.3.0
**Обновлено:** 2026-09-21
**Назначение:** единый свод правил для команды и ассистента.

При работе над проектом **все** изменения должны соответствовать этим правилам.
Нарушение → комментарий в code review + ссылка на конкретный пункт.

---

## 1. Базовые правила (из стартового промпта v1.0)

| # | Правило |
|---|---------|
| 1.1 | Комментарии и XML-документация — **на русском языке** |
| 1.2 | XML-doc обязателен для всех **public** классов/методов/свойств: `<summary>`, `<param>`, `<returns>`, `<exception>` |
| 1.3 | DI — **только через конструктор** (никакого `ServiceLocator`, `HttpContext.RequestServices` вне middleware) |
| 1.4 | Все I/O-методы — с суффиксом **`Async`** (`ReadFileAsync`, `SendEmailAsync`) |
| 1.5 | Public члены — `PascalCase`, private поля — `_camelCase` |
| 1.6 | Контроллеры: `try-catch` → единый формат `{ success, data, message }` |
| 1.7 | Сервисы: `throw` с осмысленным сообщением, **не гасить стек** (`throw;`, не `throw ex;`) |
| 1.8 | `ILogger` — технические логи; `IAuditService` — действия пользователя |
| 1.9 | Все пути — через `PathHelper.TryGetSafeFullPath` (запрет traversal) |
| 1.10 | CLI — только через `ProcessStartInfo.ArgumentList` (не строковая конкатенация) |
| 1.11 | Версия — через `AppVersion.Current` (читается из `AssemblyInformationalVersion`) |
| 1.12 | Copyright: `© 2026 RuChating (iilmchat) · IIChatTools vX.Y.Z` |
| 1.13 | Все проблемы → `docs/KNOWN_ISSUES.md` с номером `KI-XXX` |
| 1.14 | Новые UI-строки — в **оба `.resx`** (RU + EN). Проверяется `LocalizationSyncTests` |
| 1.15 | Новый инструмент = **1 класс + 1 строка регистрации** в `Startup.cs` (соответствующая группа) |

---

## 2. Документация (обновляется **в том же коммите** с кодом)

| # | Правило |
|---|---------|
| 2.1 | **`CHANGELOG.md`** (Keep a Changelog) — обновляется вместе с кодом, не задним числом |
| 2.2 | Секция **`[Unreleased]`** — рабочая; при релизе → `[X.Y.Z] — YYYY-MM-DD`, сверху — пустая `[Unreleased]` |
| 2.3 | **`README.md`** — обновляется при изменении API endpoints, требований, установки |
| 2.4 | **`docs/KNOWN_ISSUES.md`** — любая найденная проблема (даже «не баг») → запись с KI-XXX |
| 2.5 | **`docs/development/vX.Y/DESIGN.md`** — дизайн-документ для крупных фаз (Chat UI, RAG) |
| 2.6 | Секреты/credentials — **никогда** в git (только User Secrets / env / secret manager) |
| 2.7 | После релиза — обновить `Directory.Build.props` (`<Version>`) + `AppVersion.Current` |
| 2.8 | Ссылки на KI в commit message — обязательны, если фикс связан с реестром |

**Типы записей в CHANGELOG:** `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`.

---

## 3. Рабочий процесс (workflow)

| # | Правило |
|---|---------|
| 3.1 | **Маленькие шаги** — крупная фича разбивается на 3-5 подшагов |
| 3.2 | **1 шаг = 1 коммит** — не смешивать несвязанные изменения |
| 3.3 | **`git add -A`** вместо selective add (во избежание «пропущенных» файлов) |
| 3.4 | **Перед коммитом** — `git status --short` для проверки untracked/staged |
| 3.5 | **После push** — проверить Actions (CI + Docker Publish) |
| 3.6 | **Локально** — `dotnet build` **0 warnings, 0 errors** (обязательно) |
| 3.7 | **Тесты** — все зелёные перед push (`dotnet test`) |
| 3.8 | **Fallback** — при «не работает локально vs CI» — искать untracked/пропущенные файлы |
| 3.9 | **`dotnet restore --configfile NuGet.Config.online`** — после смены версий пакетов |
| 3.10 | **Перед `dotnet nuget locals all --clear`** — убить `dotnet`/`VBCSCompiler`/`MSBuild` |
| 3.11 | **На новой машине** — `git config user.email/name` **до** первого коммита |
| 3.12 | **Перед push** — `git log --all -p \| grep 'password\|secret'` (проверка на утечки) |
| 3.13 | **Удаление мусора** — после задач удалять временные скрипты (`fix-*.ps1`) |

---

## 4. Технические правила C# / .NET 10 (уроки сессии)

| # | Правило | Ошибка / причина |
|---|---------|-------------------|
| 4.1 | `yield return` **запрещён** в `try-catch` | CS1631 |
| 4.2 | `yield return` в `try-finally` — **разрешён** | — |
| 4.3 | `IAsyncEnumerable<T>` требует `using System.Collections.Generic;` | CS0246 |
| 4.4 | `Response.WriteAsync(...)` — extension из `Microsoft.AspNetCore.Http` | CS1061 |
| 4.5 | `HasName()` в EF Core 10 устарел → `HasDatabaseName()` | CS0618 |
| 4.6 | `reader.EndOfStream` в async → CA2024. Использовать `line == null` | CA2024 |
| 4.7 | `cref` в XML-doc требует `using System;` для системных исключений | CS1574 |
| 4.8 | В .NET 8+ runtime-образах пользователь `app` **уже создан** | Docker — `groupadd: already exists` |
| 4.9 | `Microsoft.AspNetCore.RateLimiting` — недоступен в SDK 10.0.401 (KI-042) | `AddRateLimiter` не резолвится |
| 4.10 | Иерархия `new`-свойств — использовать осознанно, документировать в XML-doc | CS0108 |
| 4.11 | Смешивание версий Microsoft-пакетов в одном решении → NU1605 | Синхронизировать в `Directory.Build.props` |
| 4.12 | `<Version>` в `.csproj` **перебивает** `Directory.Build.props` | Рассинхрон версий UI и логов |
| 4.13 | Путь `obj/project.assets.json` — **создаётся** `restore`, удаляется с `obj/` | NETSDK1004 |
| 4.14 | `--no-restore` использует **существующий** `project.assets.json` | Старые версии пакетов |

---

## 5. Безопасность

| # | Правило |
|---|---------|
| 5.1 | Секреты — **только** в User Secrets / env / secret manager |
| 5.2 | Прокси-credentials — не хардкодить, читать из env (`IICHATTOOLS_PROXY*`) |
| 5.3 | `PathHelper` — единственный способ работы с путями |
| 5.4 | `ArgumentList` — единственный способ вызова CLI |
| 5.5 | **Периодически** — `git log --all -p \| grep 'password\|secret\|nikiforov'` перед push |
| 5.6 | При утечке — смена пароля/токена **немедленно**, потом `filter-repo` |
| 5.7 | Файлы `.env`, `appsettings.Local.json`, `*.pfx` — в `.gitignore` |
| 5.8 | GitHub Secret Scanning + Push Protection — включены |

---

## 6. Git-специфичные правила

| # | Правило |
|---|---------|
| 6.1 | `.gitignore` — обязательное место для `bin/`, `obj/`, `LocalPackages/`, `logs/`, `Workspace/`, `.continue/` |
| 6.2 | `.gitattributes` — `* text=auto eol=lf` (единый LF) |
| 6.3 | LF/CRLF warnings при `git add` — не критичны, но не игнорировать |
| 6.4 | Формат commit message: `<type>(<scope>): <subject>` + пустая строка + body |
| 6.5 | Типы: `feat`, `fix`, `docs`, `test`, `chore`, `refactor`, `security`, `perf` |
| 6.6 | **Force-push** — только `--force-with-lease`, никогда `--force` |
| 6.7 | **`filter-repo`** — только после смены скомпрометированных секретов |
| 6.8 | При merge с origin — **сначала** `git fetch`, потом анализ конфликтов |
| 6.9 | `.gitkeep` — для пустых папок, которые нужны в репо |

---

## 7. Отложенные задачи (актуальный список KI)

См. [`docs/KNOWN_ISSUES.md`](../KNOWN_ISSUES.md) — полный реестр.

**Краткая выжимка Open/Deferred:**

| KI | Приоритет | Суть |
|----|-----------|------|
| KI-043 | 🟡 | Утечка памяти в `RateLimitingMiddleware` |
| KI-044 | 🟢 | `iichattools_audit_entries_total` не инкрементируется |
| KI-046 | 🟢 | `MessageCount` в `ChatListItemDto` всегда 0 |
| KI-047 | 🟡 | Fallback PATCH/DELETE через POST (для старых сетей) |
| KI-049 | 🟢 | `tokensIn`/`tokensOut` = null в SSE |
| KI-050 | 🟠 | JSON на HTML-эндпоинтах при rate limit (плохой UX) |

---

## 8. История изменений правил

| Дата | Версия | Что добавлено |
|------|--------|---------------|
| 2026-09-13 | 1.0.0 | 15 базовых правил |
| 2026-09-16 | 1.0.2 | Правила 2.7, 2.8 (документация + KI в commit) |
| 2026-09-18 | 1.1.0 | Правила Git 6.x (force-with-lease, filter-repo) |
| 2026-09-21 | 1.2.0 | Правила 3.x (workflow) — маленькие шаги, `git add -A` |
| 2026-09-21 | 1.3.0 | Правила 4.x (технические C#/.NET 10) + 7 (KI-выжимка) |

---

## 9. Как использовать

**При работе над задачей:**

1. Открыть этот файл, вспомнить применимые правила.
2. Создать ветку (если не `main`).
3. Мелкими шагами, коммитить каждый шаг.
4. **В том же коммите** — обновить `CHANGELOG.md` (и `README.md`/`KNOWN_ISSUES.md` при необходимости).
5. Проверить `dotnet build` + `dotnet test` локально.
6. Push → проверить CI → merge.

**При code review:**

1. Проверить по пунктам раздела 1-2 (базовое + документация).
2. Технические замечания — по разделу 4.
3. Ссылаться на конкретный пункт: «нарушение 1.4 (нет `Async`-суффикса)».

**При онбординге нового разработчика:**

1. Прочитать этот файл **целиком**.
2. Прочитать `docs/KNOWN_ISSUES.md` (последние 5 записей).
3. Прочитать `CHANGELOG.md` (последний релиз).
4. Клонировать репо, настроить User Secrets, запустить `dotnet test`.