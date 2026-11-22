# S18 — Documented: LLM, аудио, overlay

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-120, KI-144, KI-180, KI-186, KI-189 (Documented)
**HEAD до спринта:** `335dfc7` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `docs/KNOWN_ISSUES.md` | ~ | 5 заметок о верификации Documented-записей |
| `docs/TODO_KI.md` | ~ | 5 чекбоксов отмечены как Documented |
| `CHANGELOG.md` | + | `[Unreleased] → Changed`: спринт S18 |
| `docs/development/reports/v2.2/S18_KI.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Новых тестов нет. Полный прогон в § 3.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1326 passed / 1334 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |

---

## 4. Smoke

Не требуется — ограничения зависят от внешнего LLM/аудиоустройств/overlay.

---

## 5. Что не так

1. KI-120 (галлюцинация числа инструментов) и KI-186 (Vision на UI
   IIChatTools) — зависят от модели qwen3-4b; при замене модели поведение
   может измениться.
2. KI-144 — virtual audio device — внешнее окружение.

---

## 6. Новые KI

Новых KI нет. Статусы Documented сохранены.

---

## 7. Обоснование

1. Documented — терминальный статус; Intermediate-заметка фиксирует пересмотр.
2. Ни одно из ограничений не имеет воспроизводимого smoke в dev-окружении —
   менять статус было бы некорректно.
