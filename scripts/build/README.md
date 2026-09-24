# Скрипты сборки

**Каталог пуст** — скрипты эпохи v1.0 → v1.1 перенесены в архив.

---

## Почему здесь нет скриптов

Исторически тут лежали 8 `.bat` / `.ps1` для создания структуры проекта
(`create_structure*.bat`, `create_sprint8.ps1`, `create_full_structure*.bat`).
Все написаны на этапе инициализации репозитория (сентябрь 2026).

**Почему устарели:**
- `.bat` — не используем (только PowerShell / shell).
- Структура проекта уже стабильна, создаётся через `git clone` + `dotnet restore`.
- `Directory.Build.props` + `.sln` — единственный источник структуры.

**Что использовать сейчас:**
- Клонировать репо: `git clone https://github.com/iilmchat/IIChatTools.git`
- Собрать: `dotnet build IIChatTools.sln`
- Тесты: `dotnet test IIChatTools.sln`
- Настройка окружения: см. [`../setup/README.md`](../setup/README.md)

См. [`archive/v1.0.x/`](archive/v1.0.x/) — 8 файлов.

---

© 2026 RuChating (iilmchat) · IIChatTools v1.4.1