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

### KI-010 — Репозиторий раздулся до 154 МБ из-за бинарных артефактов
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** В историю Git попали `LocalPackages/*.nupkg` (NuGet-пакеты), `bin/`, `obj/` артефакты, `Data/*.db`, `logs/`, `Workspace/` и `nuget*.exe`. Размер репозитория достиг 154 МБ.
- **Решение:** 
  - `.gitignore` расширен: `**/obj/`, `**/bin/`, `LocalPackages/`, `*.nupkg`, `*.dll`, `*.exe`, `*.db`, `Workspace/`, `logs/`, `Data/`.
  - `git filter-repo --invert-paths` очистил историю от крупных файлов (три прохода).
  - `git gc --prune=now --aggressive` финализировал очистку.
  - Создан `.gitattributes` для корректной работы с LF/CRLF.
- **Результат:** размер упал с 154 МБ до 1.51 МБ.
- **Файлы:** `.gitignore`, `.gitattributes`

### KI-011 — `Workspace/` попал в индекс как вложенный git-репозиторий (gitlink)
- **Приоритет:** 🟠 High | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** При `git add .` папка `Workspace/users/1/git-test/` (внутри которой был свой `.git`) попала в индекс как gitlink `mode 160000`, что привело к ошибкам синхронизации.
- **Решение:** 
  - `git rm -r --cached Workspace/users/1/git-test`
  - Добавлено правило `Workspace/` в `.gitignore`
- **Файлы:** `.gitignore`

### KI-012 — Множественные `obj/` в индексе (не удалялись через filter-repo)
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** `filter-repo --path obj/ --invert-paths` не удалил вложенные `IIChatTools.API/obj/`, `IIChatTools.Data/obj/` и т.д. — нужен glob `*/obj/*`.
- **Решение:** `git rm -r --cached <path>/obj` для каждого проекта + правило `**/obj/` в `.gitignore`.
- **Файлы:** `.gitignore`

### KI-013 — Нереорганизованная структура репозитория
- **Приоритет:** 🟡 Medium | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** В корне репозитория накопилось 40+ файлов: скрипты, документация, временные txt, лог-файлы, изображения.
- **Решение:** Файлы сгруппированы по назначению:
  - `docs/` — architecture, development, guides, testing
  - `scripts/` — setup, build, git, migrations, diagnostics
  - `configs/` — JSON/Config-файлы
  - `assets/images/` — картинки, скриншоты
- **Файлы:** `.gitignore`, структура папок
- **Результат:** в корне остались только 7 ключевых файлов + 8 папок.

### KI-014 — Временные файлы отладки в корне
- **Приоритет:** 🟢 Low | **Статус:** Fixed | **Исправлено в:** v1.0.2
- **Обнаружено:** 2026-09-16
- **Описание:** В корне накопились `Test.txt`, `Новый текстовый документ.txt`, `long-value.json`, `screenshot.png`, `screenshot.b64` и др.
- **Решение:** Перемещены в `docs/development/archive/` и `assets/images/`.
- **Файлы:** структура папок
