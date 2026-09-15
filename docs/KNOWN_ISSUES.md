# Известные проблемы IIChatTools

Файл ведётся с версии 1.0 (сентябрь 2026).
Формат: `KI-XXX` — название, приоритет, статус, файлы.

## Исправленные

### KI-002 — 407 Proxy Authentication Required для веб-инструментов
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `IIChatTools.API/Startup.cs`
- **Решение:** `HttpClientFactoryOptions.HttpMessageHandlerBuilderActions` + `WebProxy.Credentials`.

### KI-003 — 403 Forbidden от Wikipedia API
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `WikipediaSearchTool.cs`
- **Решение:** User-Agent `IIChatTools/1.0 (https://github.com/RuChating/IIChatTools; iilmchat@localhost)`.

### KI-004 — Git-инструменты не поддерживали подкаталоги
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `BaseGitTool.cs` + 7 `Git*Tool.cs`
- **Решение:** Параметр `path` + `GitContextValidationResult` + `RunGitInDirAsync`.
- **API-изменения:** `git_diff.path` → `filePath`; `git_log.path` → `filePath`; `git_add.paths` → `files`; `git_checkout.paths` → `files`.

### KI-006 — gh-инструменты не поддерживали подкаталоги
- **Приоритет:** 🔴 Critical | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Файлы:** `BaseGhTool.cs` + 6 `Gh*Tool.cs`
- **Решение:** Параметр `path` + `GhContextValidationResult` + `RunGhInDirAsync`.

## Документированные (не баги)

### KI-001 — Неинформативное сообщение при отклонении действия
- **Приоритет:** 🟡 Medium | **Статус:** Open | **Запланировано:** v1.0.3
- **Файлы:** `test.js`, `approvals.js`
- **Решение:** Показывать причину отклонения в поле «Результат».

### KI-005 — Неявный `git add -A` при пустом списке файлов
- **Приоритет:** 🟠 High | **Статус:** Documented
- **Файлы:** `GitAddTool.cs`
- **Причина:** Пустой `files` трактуется как `-A`. Документировано в описании инструмента.

### KI-007 — gh issue create требует заранее созданные метки
- **Приоритет:** 🟢 Low | **Статус:** Documented
- **Причина:** Ограничение `gh` CLI. Метки должны быть созданы заранее.

### KI-008 — Хрупкость git-истории при манипуляциях
- **Приоритет:** 🟢 Low | **Статус:** Resolved
- **Причина:** `--allow-unrelated-histories` + `reset --hard` + пересоздание веток → расхождение историй.
- **Решение:** Чистый `git clone` восстанавливает целостность.

### KI-009 — ERR_CONNECTION_RESET на сайтах с SSO
- **Приоритет:** 🟡 Medium | **Статус:** Documented
- **Причина:** Kinopoisk и подобные сайты требуют SSO-редирект, Chromium через прокси сбрасывает соединение.
- **Решение:** Использовать нейтральные сайты. Инъекция cookies — v1.1.