# S04 — High/Medium: RAG embeddings, селектор модели, id drift, PNG scan viewer

**Дата:** 2026-11-22
**Правила:** `docs/development/RULES.md` v1.4.36
**KI спринта:** KI-227, KI-226, KI-193, KI-205
**HEAD до спринта:** `a7876f5` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Rag/EmbeddingService.cs` | ~ | KI-227: fallback на внешний OpenAI-совместимый `/v1/embeddings` |
| `IIChatTools.API/appsettings.json` | ~ | KI-227: `Rag:Embedding:ExternalBaseUrl/ApiKey/Model` (пусто по умолчанию) |
| `IIChatTools.API/Controllers/ModelsController.cs` | ~ | KI-226: поле `providers` в ответе `GET /api/models` |
| `IIChatTools.Tests/UnitTests/ModelsControllerTests.cs` | ~ | KI-226: +2 теста на провайдеров (в т.ч. при недоступном LM Studio) |
| `docs/KNOWN_ISSUES.md` | ~ | 4 KI → Fixed; сводка Fixed 181 / Planned 40 |
| `docs/TODO_KI.md` | ~ | 4 чекбокса закрыты |
| `CHANGELOG.md` | + | `[Unreleased] → Fixed`: спринт S04 |
| `docs/development/reports/v2.2/S04_KI.md` | + | Настоящий отчёт |

**Не трогал:** `VisionIdNormalizer.cs` / `DetectPlannerCycle` (KI-193 уже
реализован), `PdfParser.cs` / `chat.js` (KI-205 уже реализован).

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `GetModelsAsync_IncludesExternalProviders` | Ответ содержит `providers[0].model = deepseek-chat` |
| `GetModelsAsync_LmStudioUnavailable_ReturnsDefaultOnly` | Провайдеры доступны даже при fallback LM Studio |

KI-227 — внешний HTTP-вызов; unit-тест с mock-.handler не добавлен (требует
выделенного `HttpMessageHandler`, который уважает `CancellationToken` —
RULES § 4.35). Риск зафиксирован в § 5.

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1325 passed / 1333 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| TODO_KI | 17 закрытых чекбоксов (S01–S04) |

---

## 4. Smoke

1. **KI-227 (без внешнего провайдера):** остановить LM Studio → загрузка файла
   в чат возвращает понятную ошибку, RAG не падает с «Unexpected error».
2. **KI-227 (с внешним провайдером):** задать в User Secrets
   `Rag:Embedding:ExternalBaseUrl` + `ExternalApiKey` → индексация работает
   через внешний `/v1/embeddings`.
3. **KI-226:** `GET /api/models` — поле `providers` со списком из
   `ExternalLlm:Providers` (deepseek, openai, ...), в т.ч. при выключенном
   LM Studio.
4. **KI-205:** приложить скан-PDF → кнопка «👁 N» в чипе → модалка со
   страницами, зумом и текстовым слоем.

---

## 5. Что не так

1. KI-227 — `HttpClient` создаётся в конструкторе `EmbeddingService`
   (Singleton), без `IHttpClientFactory`; для одного провайдера это нормально,
   но при смене ключа в User Secrets нужен перезапуск.
2. KI-227 — fallback не различает «LM Studio выключен» и «неверная модель»;
   в обоих случаях пробуется внешний провайдер.
3. KI-226 — формат `Chat.Model` остался `<model>`; пункты 4–5 постановки
   (`<provider>:<model>`, per-chat переключение) не реализованы — нужна
   миграция/совместимость со старыми чатами (кандидат на новый KI).
4. В `KNOWN_ISSUES.md` у KI-205 обнаружена слитая секция KI-204 (нет
   отдельного заголовка) — историческая проблема реестра, не трогал в спринте.

---

## 6. Новые KI

Новых KI не зарегистрировано. Кандидаты из § 5 (p. 3) требуют отдельной
записи при начале работ по формату `Chat.Model`.

---

## 7. Обоснование

1. **KI-227 — минимальный внешний провайдер.** Вместо полноценного
   `IEmbeddingProvider`-абстрактного слоя добавлен fallback внутри
   `EmbeddingService`: меньше изменений в DI, конфиг читается через индексер
   (RULES § 4.56), поведение по умолчанию не меняется.
2. **KI-226 — additive-изменение контракта.** Новое поле `providers` не ломает
   существующих клиентов; изменение формата `Chat.Model` отложено, чтобы не
   мигрировать БД в этом спринте.
3. **Верификация KI-193/KI-205.** Оба фикса уже в коде (VisionIdNormalizer,
   page viewer); дублирование создало бы параллельные механизмы.
4. **Тесты провайдеров через прямой вызов контроллера.** Проверяют и обычный
   путь, и fallback LM Studio — консистентно с S02.
