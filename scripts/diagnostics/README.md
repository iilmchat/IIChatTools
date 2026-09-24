# Диагностические скрипты

**Каталог пуст** — все утилиты эпохи v1.0 → v1.1 удалены или перенесены в архив.

---

## Почему здесь нет скриптов

| Файл (legacy) | Что делал | Замена |
|---|---|---|
| `test-tool-calls.ps1` | Ручные тесты tool calling | xUnit: `ChatStreamServiceTests` |
| `test-tool-calls-multi.ps1` | Multi-turn tool calling | xUnit: то же |
| `export-sources.ps1` | Экспорт исходников в один файл | `git bundle` / `Compress-Archive` |
| `get_files_today.bat` | Список изменённых файлов | `git status` / `git diff` |
| `get_files_today_with_check.bat` | То же + проверка | — |
| `сгенерируйте строку из 201 символа.ps1` | Одноразовый тест | — |

**Что использовать сейчас:**
- **Тесты** — `dotnet test IIChatTools.sln` (81 unit/integration).
- **Health checks** — `/health/live`, `/health/ready`, `/health`.
- **Метрики** — `/metrics` (Prometheus).
- **LM Studio** — `LmStudioTestController` (`/api/lmstudio/ping`, `/api/lmstudio/tool-test`).

См. [`archive/v1.0.x/`](archive/v1.0.x/) — 3 файла.

---

© 2026 RuChating (iilmchat) · IIChatTools v1.4.1