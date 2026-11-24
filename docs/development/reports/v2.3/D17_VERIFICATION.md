# D17 — Верификация: CI, контракты, smoke v2.3

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 4F
**Задачи:** контрактные тесты, CI-проверка реестра
**HEAD до спринта:** `361a626` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `.github/workflows/ci.yml` | ~ | Шаг «Check KI registry consistency» (check-zombie + generator -Check) |
| `IIChatTools.Tests/UnitTests/ProviderContractTests.cs` | + | 2 контрактных теста провайдеров |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: CI + contract tests |
| `docs/development/reports/v2.3/D17_VERIFICATION.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `EmbeddingProvider_OpenAiCompatibleShape_ReturnsVectors` | IEmbeddingProvider: Name + формат EmbeddingResponse |
| `VectorStore_InMemory_ImplementsFullContract` | IVectorStore: Add/Count/Search/Remove/Clear |

Локально проверен CI-шаг: оба скрипта возвращают 0.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1352 passed / 1360 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| generate-ki-summary -Check | OK (exit 0) |
| Design-спринты | 17/18 |

---

## 4. Smoke

1. Push в `main` → CI выполняет шаг проверки KI-реестра до build/test.
2. Искусственно изменить число в сводке → CI падает на `-Check`.
3. `docs/qa/MANUAL_TESTING_GUIDE.md` § 6.1 — 9 сценариев v2.3 для ручной приёмки.

---

## 5. Что не так

1. Нагрузочные тесты не добавлены — требуют реального LM Studio / Qdrant.
2. CI использует `shell: pwsh` на ubuntu-latest — доступен по умолчанию,
   но версия PowerShell может отличаться от локальной.
3. Контрактные тесты покрывают только InMemory/embedded адаптеры; внешние
   (Qdrant/OpenAI) требуют сети — проверяются вручную.

---

## 6. Новые KI

Новых KI нет.

---

## 7. Обоснование

1. **Реестр в CI.** Расхождения KI-254/275 больше не могут попасть в main —
   проверка автоматизирована (docs as code).
2. **Контракты провайдеров.** Тесты фиксируют форму ответов Port'ов D04/D05 —
   новые адаптеры обязаны её соблюдать.
3. **Smoke как документ.** § 6.1 остаётся главным чеклистом приёмки v2.3.
