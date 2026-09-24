# Скрипты настройки окружения

Утилиты для локальной настройки, offline-развёртывания и ускорения dev-цикла.
**Все скрипты работают от корня репозитория** (путь определяется через `$PSScriptRoot`).

---

## Актуальные

### `enable-online-restore.ps1`

Создаёт временный `NuGet.Config.online` из шаблона `NuGet.Config.example.online`.
Нужен, когда требуется restore с `nuget.org` (например, при наполнении `LocalPackages`).

```powershell
pwsh -ExecutionPolicy Bypass -File scripts\setup\enable-online-restore.ps1
```

| Параметр | Действие |
|---|---|
| `-Force` | Перезаписать существующий `NuGet.Config.online` |

---

### `fill-local-packages.ps1`

Наполняет `LocalPackages/` пакетами из глобального NuGet-кэша (`~/.nuget/packages`)
с сохранением иерархии `<id>/<version>/`. Результат — локальный источник для offline-restore.

**Порядок (три шага):**
```powershell
# 1. Создать NuGet.Config.online
pwsh scripts\setup\enable-online-restore.ps1

# 2. Скачать пакеты в глобальный кэш
dotnet restore IIChatTools.sln --configfile NuGet.Config.online --force

# 3. Скопировать .nupkg в LocalPackages/
pwsh scripts\setup\fill-local-packages.ps1
```

| Параметр | Действие |
|---|---|
| `-Clean` | Снести `LocalPackages/` перед копированием (с резервной копией) |
| `-DryRun` | Показать, что будет скопировано, без копирования |

---

### `configure-defender.ps1`

**Требует прав администратора.** Добавляет исключения Windows Defender для
папки проекта, NuGet-кэша, .NET SDK и процессов `dotnet.exe`, `testhost.exe`,
`vstest.console.exe` и др. Ускоряет первый `dotnet test` после холодной сборки
(125 с → 2 с, см. KI-073).

```powershell
# Открыть PowerShell от имени администратора
cd D:\Projects\IIChatTools
.\scripts\setup\configure-defender.ps1
```

---

## Что НЕ в этом каталоге (см. в корне)

- **`NuGet.Config`** — источник пакетов (по умолчанию `LocalPackages`).
- **`NuGet.Config.example.online`** — шаблон для онлайн-restore.
- **`NuGet.Config.online`** — создаётся `enable-online-restore.ps1` (не коммитится).

---

## Архив

`scripts/setup/archive/v1.0.x/` — 8 legacy-скриптов эпохи v1.0 → v1.1:
- `check-packages.ps1` — проверка наличия пакетов в `LocalPackages`.
- `create_and_fill_localPackages.ps1` — неверный подход (restore в source).
- `downloader2.ps1`, `downloader3.ps1`, `download-missing.ps1`, `download-missing2.ps1`,
  `download-packages.ps1` — работа с `NuGetWebDownloader.exe` / прямой HTTP.
- `create_structure_1_0.ps1` — структура решения v1.0.

**Почему устарели:** современный `fill-local-packages.ps1` использует глобальный
NuGet-кэш (наполняется через `dotnet restore`), без внешних зависимостей.
Скрипты через `NuGetWebDownloader.exe` требовали внешний `.exe` и работали
медленнее (HTTP-запрос на каждый пакет, rate limit).

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.4.1**