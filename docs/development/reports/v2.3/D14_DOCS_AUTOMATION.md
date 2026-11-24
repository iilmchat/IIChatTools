# D14 — Качество и документы: генератор сводки KI

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.4 / § 4E
**Задачи:** KI-254, KI-275, KI-147
**HEAD до спринта:** `275395d` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `scripts/setup/generate-ki-summary.ps1` | + | Генератор сводки + режим -Check |
| `docs/development/RULES.md` | ~ | § 2.12 — сводка только генератором |
| `docs/development/ARCHITECTURE.md` | ~ | v2.3.0, перечень изменений D01–D13 |
| `docs/KNOWN_ISSUES.md` | ~ | KI-275 → Fixed; KI-147 → заметка; сводка Fixed 211 / Planned 7 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: generate-ki-summary |
| `docs/development/reports/v2.3/D14_DOCS_AUTOMATION.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Проверка генератора:

```text
pwsh -ExecutionPolicy Bypass -File scripts/setup/generate-ki-summary.ps1
Fixed 211 | Planned 7 | Documented 22 | Deferred 7 | Partially Fixed 6 | In Progress 0 | Won't Fix 1 | Всего 254

pwsh ... -Check
Сводка актуальна: 254 KI   (exit 0)
```

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1350 passed / 1358 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| generate-ki-summary -Check | OK (exit 0) |
| Design-спринты | 14/18 |

---

## 4. Smoke

Изменить число в сводке вручную → `-Check` возвращает ненулевой код с
указанием расхождения; восстановление — запуск генератора без `-Check`.

---

## 5. Что не так

1. `-Check` сравнивает только числа, не форматирование (жирный «Всего» и
   пояснение у Fixed допустимы).
2. CI- workflow пока не вызывает скрипт — будет добавлен в D17.
3. check-zombie-ki.ps1 и generate-ki-summary.ps1 дублируют парсер — можно
   вынести общий модуль в отдельный спринт.

---

## 6. Новые KI

Новых KI нет. KI-275 → Fixed, KI-254 уже Fixed, KI-147 → заметка.

---

## 7. Обоснование

1. **Docs as code.** Сводка пересчитывается из source of truth, а не
   редактируется вручную — устраняет корректную причину KI-254/275.
2. **Толерантный парсер.** Emoji, Implemented/Resolved и суффиксы KI-110a/b
   обрабатываются так же, как в check-zombie-ki.ps1.
3. **ARCHITECTURE как living document.** Версия 2.3.0 и список изменений
   D01–D13 фиксируют актуальное состояние без полной переписки 690 строк.
