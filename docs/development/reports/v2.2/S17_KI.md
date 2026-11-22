# S17 — Documented: внешние сервисы и модели

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-094, KI-095, KI-112, KI-116, KI-117 (Documented)
**HEAD до спринта:** `2c09d65` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `docs/KNOWN_ISSUES.md` | ~ | 5 заметок о верификации Documented-записей |
| `docs/TODO_KI.md` | ~ | 5 чекбоксов отмечены как Documented |
| `CHANGELOG.md` | + | `[Unreleased] → Changed`: спринт S17 |
| `docs/development/reports/v2.2/S17_KI.md` | + | Настоящий отчёт |

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

Не требуется — внешние сервисы (Wikipedia, Docker image, LM Studio model
capabilities) недоступны в dev-окружении.

---

## 5. Что не так

1. KI-116 (gemma tool calling) и KI-117 (8192 context) — зависят от внешних
   моделей; при смене модели в LM Studio ограничения могут исчезнуть.
2. KI-094 — intermittent timeout через корпоративный прокси; в коде уже
   есть 15s timeout + retry (RULES § 4.28).

---

## 6. Новые KI

Новых KI нет. Статусы Documented сохранены.

---

## 7. Обоснование

1. Documented — терминальный статус; менять его было бы некорректно.
2. Заметка о верификации фиксирует пересмотр ограничения в v2.2.x.
