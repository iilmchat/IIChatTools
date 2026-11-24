# D07 — Провайдеры: база данных и outbox (часть 1)

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.1 / § 4B
**Задачи:** KI-242 (резолвер), KI-091 (заметка), KI-265 (часть 2 — отложена)
**HEAD до спринта:** `4fcc421` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Utils/DatabaseProviderResolver.cs` | + | Проверка/нормализация `Database:Provider` |
| `IIChatTools.API/Extensions/DbContextOptionsExtensions.cs` | ~ | Использование резолвера |
| `IIChatTools.Tests/UnitTests/DatabaseProviderResolverTests.cs` | + | 6 тестов |
| `docs/KNOWN_ISSUES.md` | ~ | Заметки в KI-242/091; KI-242 → Partially Fixed; сводка Planned 14 / Partially 2 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: DatabaseProviderResolver |
| `docs/development/reports/v2.3/D07_DATABASE_PROVIDERS.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `Resolve_KnownProvider_ReturnsCanonical` (4 кейса) | Sqlite/SqlServer/InMemory/default |
| `Resolve_PostgreSQL_ThrowsWithGuidance` | Понятная ошибка про Npgsql |
| `Resolve_Unknown_Throws` | Oracle → InvalidOperationException |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1342 passed / 1350 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 7/18 |

---

## 4. Smoke

1. `Database:Provider = sqlite` — приложение стартует как раньше.
2. `Database:Provider = PostgreSQL` — понятная ошибка старта с указанием,
   что нужен Npgsql и миграции (вместо «unknown provider»).
3. Перед `dotnet ef migrations add` — проверить провайдер (RULES § 3.15).

---

## 5. Что не так

1. **PostgreSQL-адаптер не реализован.** В офлайн-репозитории нет пакета
   Npgsql — реализация требует доступ к NuGet и отдельные миграции.
2. **Outbox (KI-265 часть 2) не реализован.** Требует новой таблицы и
   миграций — перенесено в D17 (верификация/релиз) или отдельный Design.
3. KI-091 — historical snapshot drift; резолвер лишь предотвращает
   создание Sqlite-типов в SqlServer-миграциях при неверном провайдере.

---

## 6. Новые KI

Новых KI нет. KI-242 → Partially Fixed.

---

## 7. Обоснование

1. **Валидация до регистрации DbContext.** Резолвер вызывается в
   `AddAppDbContext` — ошибка конфигурации видна сразу при старте.
2. **Честная ошибка для PostgreSQL.** Лучше явно сказать «нет пакета»,
   чем создать неработающий адаптер.
3. **Outbox после миграций.** D07 подготовил проверку провайдера; реальная
   outbox-таблица появится вместе с миграциями в following спринтах.
