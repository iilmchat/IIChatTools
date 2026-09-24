cd G:\AI\IIChatTools

# 1. Инициализация репозитория
git init

# 2. Настройка .gitignore
@"
# Build
bin/
obj/
*.user
*.suo
.vs/

# Data (SQLite)
Data/*.db
Data/*.db-shm
Data/*.db-wal
*.db

# Logs
logs/
*.log

# Workspace (пользовательские данные)
Workspace/

# IDE
.idea/
.vscode/
"@ | Out-File -FilePath .gitignore -Encoding UTF8

# 3. Настройка пользователя
git config user.name "RuChating"
git config user.email "iilmchat@localhost"

# 4. Первый коммит
git add .
git commit -m "IIChatTools v1.0 — initial release

- 40 инструментов для LLM (файлы, код, веб, git, github, браузер, суб-агенты, утилиты)
- Мультипользовательская админка с ролями Admin/User
- Система подтверждений (PendingAction + polling + whitelist)
- Гибридная БД: SqlServer / Sqlite / InMemory + файловый аудит (JSONL)
- Локализация RU/EN с автоматическим тестом синхронизации
- Суб-агенты с авто-отладкой (агент-рецензент)
- Веб-тестер инструментов + страница статуса + аудит
- Аутентификация: cookie + JWT, первый пользователь = Admin
"

# 5. Тег версии
git tag -a v1.0.0 -m "Release v1.0.0"

# 6. Проверка
git log --oneline
git tag