# D08 — Безопасность: персональные почтовые ящики (часть 1)

**Дата:** 2026-11-25
**Правила:** `docs/development/RULES.md` v1.4.36
**Design:** `docs/development/v2.3/DESIGN_IMPROVEMENTS.md` § 3.4 / § 4C
**Задачи:** KI-108 (часть 1)
**HEAD до спринта:** `ed346db` (main)

---

## 1. Файлы (+/-)

| Файл | +/- | Что |
| :--- | :---: | :--- |
| `IIChatTools.Services/Implementation/Mail/PerUserMailAccountProvider.cs` | + | Провайдер ящика по `Mail:Users:{userId}:*`, fallback на Global |
| `IIChatTools.API/Startup.cs` | ~ | Регистрация провайдера через factory (Global → PerUser) |
| `IIChatTools.Tests/UnitTests/Mail/PerUserMailAccountProviderTests.cs` | + | 2 теста (свой ящик / fallback) |
| `docs/KNOWN_ISSUES.md` | ~ | KI-108 → Partially Fixed; сводка Planned 13 / Partially 3 |
| `CHANGELOG.md` | + | `[Unreleased] → Added`: PerUserMailAccountProvider |
| `docs/development/reports/v2.3/D08_MAIL_PER_USER.md` | + | Настоящий отчёт |

---

## 2. Покрытие

| Тест | Что проверяет |
| :--- | :--- |
| `GetAsync_UserConfigured_ReturnsUserCredentials` | Свой IMAP/SMTP, логин/пароль, дефолтные порты/SSL |
| `GetAsync_UserNotConfigured_FallsBackToGlobal` | Нет секции → глобальный ящик |

---

## 3. Результаты

| Проверка | Результат |
| :--- | :--- |
| Build | 0 warnings, 0 errors |
| Tests | 1344 passed / 1352 total, 0 failed, 8 skipped |
| check-zombie-ki.ps1 | 254 / 255 / 254 |
| Design-спринты | 8/18 |

---

## 4. Smoke

1. Задать в User Secrets `Mail:Users:1:Imap:Host/Username/Password` →
   `mail_agent` для пользователя 1 использует этот ящик.
2. Для пользователя без секции — работает общий ящик (как в v1.8).

---

## 5. Что не так

1. **Часть 2 не реализована.** Нет таблицы `UserMailAccount`, шифрования
   через `IDataProtector` и UI в `/profile → Почта` — требуется миграция
   и стабильный key ring между рестартами.
2. Пароли в конфиге (User Secrets) — не в БД; ротация — через секреты.
3. Проверка валидности хоста/порта не выполняется (только fallback-значения).

---

## 6. Новые KI

Новых KI нет. KI-108 → Partially Fixed.

---

## 7. Обоснование

1. **Конфиг до БД.** Per-user изоляция работает уже сейчас через User Secrets,
   без миграции и риска потерять key ring.
2. **Индексер, не GetValue.** RULES § 4.56 — конфиг читается через
   `_configuration[key]`, что стабильно с fake-конфигурациями в тестах.
3. **Fallback на Global.** Обратная совместимость: пользователи без своего
   ящика продолжают работать как раньше.
