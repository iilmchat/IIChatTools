# D04 — Провайдеры: эмбеддинги (Port + адаптеры)

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.1 / § 4B
**Задачи:** KI-227 (доработка)
**HEAD до спринта:** `942f34f` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Interfaces/IEmbeddingProvider.cs` | + | Port для эмбеддингов |
| `IIChatTools.Services/Implementation/Rag/Providers/LmStudioEmbeddingProvider.cs` | + | Адаптер LM Studio |
| `IIChatTools.Services/Implementation/Rag/Providers/OpenAiEmbeddingProvider.cs` | + | Адаптер OpenAI-compatible (/v1/embeddings) |
| `IIChatTools.Services/Implementation/Rag/EmbeddingService.cs` | ~ | Работа с `IEnumerable<IEmbeddingProvider>` (primary + fallback) |
| `IIChatTools.API/Startup.cs` | ~ | DI-регистрация провайдеров в порядке приоритета |
| `IIChatTools.Tests/UnitTests/EmbeddingServiceTests.cs` | ~ | Новый тест fallback + обновлённый конструктор |
| `docs/KNOWN_ISSUES.md` | ~ | Заметка D04 в KI-227 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: IEmbeddingProvider |
| `docs/development/reports/v2.3/D04_EMBEDDING_PROVIDERS.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `GetEmbeddingsAsync_PrimaryFails_UsesFallback` | Сбой основного провайдера → вызов резервного, результат и счётчик вызовов |

Существующие 8 тестов EmbeddingService обновлены под новый конструктор и проходят.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1331 passed / 1339 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 4/18 |

---

## 4. Smoke

1. Без `Rag:Embedding:ExternalBaseUrl` — работает только LM Studio
   (поведение как до D04).
2. С заданным ExternalBaseUrl + ApiKey — при недоступном LM Studio
   индексация продолжается через внешний API; в логах warning с именами
   провайдеров.

---

## 5. Что не так

1. `OpenAiEmbeddingProvider` создаёт `HttpClient` в конструкторе (Singleton,
   один провайдер) — смена ключа требует перезапуска.
2. Выбор провайдера — по порядку регистрации в DI, а не по
   `Rag:Embedding:Provider` (будет в D05/D06 с общим `Provider:Kind`).
3. Внешняя отмена не вызывает fallback — это корректно.

---

## 6. Новые KI

Новых KI нет. KI-227 уже Fixed, добавлена заметка о рефакторинге.

---

## 7. Обоснование

1. **Port вместо panic-кода.** Fallback теперь инкапсулирован в интерфейс —
   новые провайдеры добавляются одной регистрацией в DI (RULES § 1.15-аналог).
2. **Порядок вместо флагов.** `IEnumerable<IEmbeddingProvider>` даёт простой
   приоритет: первый = основной, второй = резервный.
3. **Совместимость.** Публичный контракт `IEmbeddingService` не изменился —
   все потребители RAG работают без правок.
