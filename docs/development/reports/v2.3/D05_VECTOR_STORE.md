# D05 — Провайдеры: Qdrant как внешний IVectorStore

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.1 / § 4B
**Задачи:** KI-243
**HEAD до спринта:** `2f54c78` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Rag/Providers/QdrantVectorStore.cs` | + | Адаптер IVectorStore → Qdrant REST API |
| `IIChatTools.API/Startup.cs` | ~ | DI-выбор Qdrant / InMemory по конфигу |
| `IIChatTools.API/appsettings.json` | + | `VectorStore:Qdrant:BaseUrl/ApiKey/CollectionPrefix` (пусто по умолчанию) |
| `docs/KNOWN_ISSUES.md` | ~ | KI-243 → Fixed; сводка Fixed 208 / Planned 15 |
| `docs/TODO_KI.md` | ~ | 1 чекбокс закрыт |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: QdrantVectorStore |
| `docs/development/reports/v2.3/D05_VECTOR_STORE.md` | + | Настоящий отчёт |

---

## 2. Покрытие

Отдельных тестов с HTTP-моком нет: `QdrantVectorStore` создаёт HttpClient
внутри конструктора (по образцу D04). Полный прогон существующих тестов
RAG (InMemoryVectorStore) —

```text
1331 passed / 1339 total, 0 failed, 8 skipped
```

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1331 passed / 1339 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 5/18 |

---

## 4. Smoke

1. Без `VectorStore:Qdrant:BaseUrl` — RAG работает как раньше (InMemory).
2. С запущенным Qdrant и заданным BaseUrl — индексация/поиск идут через
   коллекции `iichattools_<index>`; в UI «База знаний» видны те же 4 индекса.

---

## 5. Что не так

1. Контракт `IVectorStore` синхронный — HTTP-вызовы блокируются через
   `GetAwaiter().GetResult()`; под высокой нагрузкой предпочтительнее
   асинхронный контракт (отдельный DESIGN).
2. Нет retries/circuit breaker — будет подключено в D07 вместе с
   `ResiliencePolicy` (D03).
3. Проверка ответа Qdrant минимальна (`status: error`), без разбора деталей.

---

## 6. Новые KI

Новых KI нет. KI-243 → Fixed (v2.3.0).

---

## 7. Обоснование

1. **Адаптер, а не замена.** InMemory остаётся default — externный Qdrant
   включается только конфигом, без миграции данных.
2. **CollectionPrefix.** Изоляция коллекций одного Qdrant между индексами
   и окружениями.
3. **Таймаут 15 с.** Явный timeout на каждый HTTP-вызов (RULES § 4.28).
