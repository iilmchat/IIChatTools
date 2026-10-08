# Синхронизация KNOWN_ISSUES.md с GitHub Issues (KI-214)

**Версия:** 1.0
**Дата:** 2026-10-08
**Статус:** Implemented (v1.14)
**Связанные KI:** KI-214.

---

## § 1. Зачем

`docs/KNOWN_ISSUES.md` — единый реестр 100+ KI в плоском Markdown. Плюсы:
единый source of truth, git-blame, diff-able. Минусы: нет фильтрации,
сортировки, поиска по labels.

GitHub Issues дают нативный UI, но полный рефакторинг на Issues потеряет
`git-blame` и Markdown-историю.

**Подход B (витрина):**
- `KNOWN_ISSUES.md` — **единственный source of truth** (скрипт только читает).
- `scripts/sync-known-issues.ps1` — **парсит** MD и создаёт/обновляет
  GitHub Issues.
- Двусторонней синхронизации **нет** (правки в Issues игнорируются).

---

## § 2. Требования

- `gh` CLI ≥ 2.40 ([install](https://cli.github.com/)).
- Авторизация с правами `repo` + `issues:write`:

  ```powershell
  gh auth status
  # Ожидание: "Logged in to github.com account <user>"
  # Token scopes: 'repo', 'workflow', ...
  ```

- Права `ADMIN` или `WRITE` на целевом репо:

  ```powershell
  gh repo view iilmchat/IIChatTools --json viewerPermission
  # Ожидание: { "viewerPermission": "ADMIN" }
  ```

---

## § 3. Как запускать

### 3.1. Dry-run (проверка без gh)

```powershell
cd K:\AI\Work\IIChatTools
pwsh scripts/sync-known-issues.ps1 -DryRun
```

Показывает, что было бы создано/обновлено. **Никаких вызовов gh не делает.**

### 3.2. Точечный прогон (только KI-137, KI-215)

```powershell
pwsh scripts/sync-known-issues.ps1 -Filter "KI-137,KI-215"
```

### 3.3. Полный прогон (все 102 KI)

```powershell
pwsh scripts/sync-known-issues.ps1
```

Первый полный прогон: ~102 Issues за ~2-4 минуты (throttling gh).

### 3.4. Только создание (не обновлять существующие)

```powershell
pwsh scripts/sync-known-issues.ps1 -SkipExisting
```

---

## § 4. Что делает скрипт

1. **Парсит** `KNOWN_ISSUES.md` → секции `### KI-XXX — Title`.
2. **Извлекает** для каждой секции:
   - `Id` (`KI-XXX`).
   - `Title`.
   - `Priority` (по первой эмодзи: 🔴/🟠/🟡/🟢).
   - `Status` (regex `**Статус:** XXX`).
   - `Body` (полное содержимое секции).
3. **Дедуплицирует** по `KI-id` — первое вхождение побеждает. Дубли
   (если есть) логируются с warning.
4. **Создаёт labels** (idempotent):
   - `ki` — все Issue.
   - `priority-{critical|high|medium|low|unknown}`.
   - `status-{fixed|resolved|implemented|documented|deferred|wontfix|planned|in-progress|partially-fixed|open|unknown}`.
5. **Для каждой секции:**
   - Нет Issue с title `KI-XXX: ...` → `gh issue create`.
   - Есть → `gh issue edit` (body + labels delta).
   - Состояние: **closed** для
     `Fixed/Resolved/Implemented/Documented/Deferred/Won't Fix`;
     **open** для `Planned/Open/In Progress/Partially Fixed/Unknown`.
6. **Отчёт:** created / updated / skipped / errors.

---

## § 5. Ограничения

- **Двусторонней синхронизации нет.** Правки в Issues (заголовок, тело,
  комментарии) будут перезаписаны или проигнорированы при следующем прогоне.
- **Комментарии к Issues не отключаются** (API GitHub не позволяет
  отключить их при создании — нужно вручную через UI).
- **Все 102 KI** получают Issues, включая `Fixed` и `Documented`. Fixed —
  закрытые (state = CLOSED).
- **CI-триггер** (`.github/workflows/sync-issues.yml`) — **отложен**. При
  необходимости — отдельным коммитом.

---

## § 6. Когда запускать

- **Вручную** — по мере накопления изменений в `KNOWN_ISSUES.md`.
- **Перед релизом** — чтобы витрина соответствовала source of truth.
- **После массовых правок** KI (например, закрытия серии) — чтобы Issues
  закрылись автоматически.

---

## § 7. Troubleshooting

### `gh issue create` → "label not found"

Скрипт создаёт labels сам при первом прогоне. Если по какой-то причине
label не создался (например, нет прав `issues:write`) — создайте вручную:

```powershell
gh label create ki --repo iilmchat/IIChatTools --color ededed
```

### `gh` печатает кракозябры

Проверьте кодировку консоли:

```powershell
chcp 65001
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
```

Скрипт уже ставит эти настройки в начале.

### Дубликаты Issues

Если в `KNOWN_ISSUES.md` есть **дублирующиеся секции** одного KI — скрипт
дедуплицирует (берёт первое вхождение). В Issues создаётся один Issue.

Если Issue уже создан ранее с другим title (например, кто-то переименовал) —
матч по префиксу `KI-XXX` не сработает, скрипт создаст новый. Решение:
вручную переименовать старый или закрыть как «duplicate».