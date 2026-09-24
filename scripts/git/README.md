# Git-скрипты

**Каталог пуст** — все git-операции выполняются вручную через стандартные команды.

Актуальный процесс релиза — в [`docs/development/RELEASES.md`](../../docs/development/RELEASES.md).

---

## Почему здесь нет скриптов

Исторически тут лежали автоматизаторы эпохи v1.0 → v1.1:
- `finalize-net10-release.ps1` — финализация релиза v1.1.0
- `release.ps1` — generic (CHANGELOG + версия + тег)
- `commit-net10-migration.ps1` — коммит миграции .NET 10
- `create-net10-pr.ps1` — создание PR через `gh`
- `Вариант B — Zip-архив.ps1` — zip-бэкап

**Все устарели:**
- Захардкожены пути `G:\AI\IIChatTools` и версии v1.0.x / v1.1.0.
- `release.ps1` конфликтует с ручным чек-листом `RELEASES.md` (here-string + `gh release create`).
- Полезная логика (тег, коммит, PR) уже описана в RELEASES.md § 4-6 в виде PowerShell-сниппетов.

См. [`archive/v1.0.x/`](archive/v1.0.x/) — 5 файлов.

---

**© 2026 RuChating (iilmchat) · IIChatTools v1.4.1**