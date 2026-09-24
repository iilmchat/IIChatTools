# Скрипты миграций

**Каталог пуст** — команды для создания миграций перенесены в документацию.

---

## Актуальные команды

См. [`docs/development/ARCHITECTURE.md`](../../docs/development/ARCHITECTURE.md) § 3.5.

**SqlServer** (используется по умолчанию):
```powershell
dotnet ef migrations add <Name> --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations/SqlServer
```

**Sqlite** (запланировано, KI-070):
```powershell
dotnet ef migrations add <Name> --project IIChatTools.Data --startup-project IIChatTools.API --output-dir Migrations/Sqlite
```

**Применение:**
```powershell
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API
```

---

## Почему здесь нет скриптов

Два legacy-скрипта (эпоха v1.0 → v1.1) перенесены в архив:
- `Миграции для SQLite.ps1` — устаревший подход (одна папка Migrations).
- `Правильно использовать отдельные папки миграций для каждого провайдера.ps1` — справка, перенесена в ARCHITECTURE.md § 3.5.

**Почему устарели:**
- `.ps1` для 2-3 команд — overhead больше, чем пользы.
- Актуальные команды — в ARCHITECTURE.md (в контексте схемы данных).

См. [`archive/v1.0.x/`](archive/v1.0.x/) — 2 файла.

---

© 2026 RuChating (iilmchat) · IIChatTools v1.4.1