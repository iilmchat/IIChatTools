# D18 — Релиз v2.3.0

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36, `RELEASES.md` § 1b
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4F
**HEAD до спринта:** `b7be5c4` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `Directory.Build.props` | ~ | Версия 2.2.0 → 2.3.0 (Version + Copyright) |
| `CHANGELOG.md` | ~ | `[2.3.0] — 2026-11-25`, пустой `[Unreleased]` сверху |
| `docs/development/RELEASES.md` | + | § 1b — чек-лист релиза v2.3.0 |
| `docs/TODO_DESIGN_SPRINTS.md` | ~ | Все этапы A–F завершены |
| `README.md` | ~ | Счётчик тестов 1352/1360 |
| `docs/README.md` | ~ | v2.3.0, счётчик тестов |
| `docs/development/reports/v2.3/D18_RELEASE.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Финальный прогон всех тестов и проверок — § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1352 passed / 1360 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| generate-ki-summary -Check | OK (exit 0) |
| Design-спринты | 18/18 ✅ |

---

## 4. Smoke

1. `/api/health` и `/metrics` — версия приложения `2.3.0`
   (из `AssemblyInformationalVersion` через `AppVersion.Current`).
2. `docs/qa/MANUAL_TESTING_GUIDE.md` § 6.1 — 9 сценариев v2.3.
3. CI на `main` — зелёный, включая шаг проверки KI-реестра.

---

## 5. Что не так

1. Часть KI остаётся открытой (Planned/Deferred/Partially) — перенесена в v2.4
   с причинами в KNOWN_ISSUES.md (KI-265, 242, 108, 143, 139, 164–166).
2. Docker Publish workflow не проверялся вручную в этом спринте — только CI.
3. Реальные внешние провайдеры (Qdrant, OpenAI embeddings, PostgreSQL)
   требуют ключей/сервисов и не проверялись в offline-окружении.

---

## 6. Новые KI

Новых KI нет. Релиз v2.3.0 завершён.

---

## 7. Обоснование

1. **SemVer minor.** Изменения обратносовместимы (новые Port'ы, флаги,
   аддитивные поля), поэтому версия 2.3.0, а не 3.0.0.
2. **Версия в props.** RULES § 2.7/§ 4.12 — единый источник версии для
   `AppVersion.Current` и Copyright; дублей в .csproj нет.
3. **Чек-лист релиза.** RELEASES.md § 1b фиксирует, что все 18 спринтов
   выполнены и какие ограничения перенесены в v2.4.
