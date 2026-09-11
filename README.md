# IIChatTools v1.0

**Платформа инструментального моста между локальной LLM (LM Studio) и средой разработчика.**

© 2026 RuChating (iilmchat) · IIChatTools v1.0

---

## Что это

IIChatTools — серверное приложение на **.NET Core 3.1**, предоставляющее LLM
широкий набор безопасных инструментов: работа с файловой системой, выполнение
кода, веб-поиск, браузерная автоматизация, Git/GitHub, делегирование суб-агентам.

Проект вдохновлён [Beledarian's LM Studio Tools](https://lmstudio.ai/beledarian/beledarians-lm-studio-tools)
и реализует аналогичную функциональность в экосистеме .NET + MSSQL.

## Ключевые возможности

- **39 инструментов** для LLM (файловая система, код, веб, Git/GitHub, браузер, утилиты).
- **Суб-агенты** — делегирование многошаговых задач с авто-отладкой.
- **Веб-админка** с полным CRUD (пользователи, настройки, белый список, аудит).
- **Мультипользовательность** с ролями Admin/User, изоляцией workspace.
- **Система подтверждений** — критичные действия требуют утверждения пользователем (polling).
- **Локализация** RU/EN (интерфейс + сообщения).
- **Аудит** всех действий в MSSQL.
- **Безопасность**: `PathHelper`, `ArgumentList`, whitelist команд, лимиты размеров, TTL-сессии.

---

## Требования

| Компонент | Версия |
| :--- | :--- |
| .NET Core SDK | 3.1.201+ |
| MSSQL Server | 2016+ (Express/Developer/Standard) |
| LM Studio | 0.3.0+ (с OpenAI-совместимым API) |
| Node.js (опционально) | 18+ |
| Python 3 (опционально) | 3.x |
| Git CLI (опционально) | любая актуальная |
| GitHub CLI `gh` (опционально) | любая актуальная |
| Chromium для PuppeteerSharp | скачивается автоматически при первом запуске (~200 МБ) |

---

## Установка

```bash
git clone <repo-url> IIChatTools
cd IIChatTools
dotnet restore
```

### Настройка

Отредактируйте `IIChatTools.API/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=IIChatTools;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "УКАЖИТЕ_СЕКРЕТНЫЙ_КЛЮЧ_МИНИМУМ_32_СИМВОЛА"
  },
  "Workspace": {
    "RootPath": "C:\\IIChatToolsWorkspace"
  },
  "LmStudio": {
    "BaseUrl": "http://localhost:8034",
    "Model": "local-model"
  }
}
```

### Миграции и запуск

```bash
dotnet ef database update --project IIChatTools.Data --startup-project IIChatTools.API
dotnet run --project IIChatTools.API
```

При первом запуске автоматически:
- применяются миграции;
- создаются роли `Admin` и `User`;
- синхронизируются настройки из `appsettings.json` в БД.

**Первый зарегистрированный пользователь** получает роль **Admin**. Все последующие — **User**.

---

## API (основные endpoints)

| Метод | URL | Назначение |
| :--- | :--- | :--- |
| `POST` | `/auth/register` | Регистрация |
| `POST` | `/auth/login` | Вход (cookie) |
| `POST` | `/auth/token` | Получение JWT |
| `GET` | `/api/tools` | Список инструментов |
| `POST` | `/api/tools/execute` | Вызов инструмента |
| `GET` | `/api/tools/execution/{id}` | Статус вызова (polling) |
| `GET` | `/api/approvals/pending` | Ожидающие подтверждения |
| `POST` | `/api/approvals/{id}/approve` | Подтвердить |
| `POST` | `/api/approvals/{id}/reject` | Отклонить |
| `GET` | `/api/status/snapshot` | Снимок состояния |
| `*` | `/api/admin/*` | CRUD админки (только Admin) |

---

## Безопасность

- **Пути** всегда проверяются через `PathHelper.TryGetSafeFullPath`.
- **Команды CLI** — через `ProcessStartInfo.ArgumentList` (без конкатенации строк) и белый список.
- **Размеры данных** — конфигурируемые лимиты (файл, вывод команды, тело запроса).
- **Таймауты** — для всех внешних операций.
- **Сессии браузера** — TTL, изоляция по пользователю, лимит одновременных сессий.
- **Суб-агенты** — запрет рекурсии, лимит шагов, персистентность.
- **Подтверждения** — все критичные действия требуют явного согласия пользователя.

---

## Сборка и тесты

```bash
dotnet build IIChatTools.sln -c Release
dotnet test IIChatTools.Tests
```

---

## Развёртывание

1. Установите .NET Core 3.1 Runtime на целевой сервер.
2. Создайте БД MSSQL, выполните `dotnet ef database update`.
3. Настройте `appsettings.json` (строка подключения, JWT-ключ, workspace).
4. Опубликуйте: `dotnet publish IIChatTools.API -c Release -o ./publish`.
5. Запустите `dotnet IIChatTools.API.dll` (Kestrel) или настройте IIS.

---

## Лицензия

© 2026 RuChating (iilmchat). Все права защищены.