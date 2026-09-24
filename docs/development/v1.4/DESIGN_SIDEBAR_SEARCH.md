# DESIGN v1.4.x — Sidebar collapse + Поиск (A + B)

**Версия:** 1.4.x (patch)
**Дата:** 2026-09-25
**Автор:** IIChatTools Team
**Статус:** **Approved** (2026-09-25)
**Связанные KI:** KI-079 (collapse sidebar), KI-078 (поиск: A — внутричатовый, B — модалка по всем чатам)
**Реализовано:** —
**Не входит:** KI-080 (поле ввода на всю ширину) — отдельно.

---

## § 1. Контекст

После релиза v1.4.0 (Multi-Agent) Chat UI имеет полный функционал общения с LLM,
но по сравнению с DeepSeek/ChatGPT у него отсутствуют UX-мелочи:

1. **Нет возможности свернуть sidebar** — на узких экранах или при работе
   с длинными ответами хочется освободить место под диалог (KI-079).
2. **Поиск неполный** (KI-078):
   - **A.** Нет поиска по *активному* чату (Ctrl+F-style, как в браузере).
   - **B.** Нет модалки ⌘K-style для быстрого поиска по *всем* чатам
     (DeepSeek/ChatGPT Search). Текущий inline `#chat-search` в sidebar
     (ChatGPT-style, KI-068) — упрощённая версия, оставляем как есть.

KI-079 — frontend-only. KI-078A — frontend-only. KI-078B — требует
расширения backend (сниппет с превью).

---

## § 2. Проблемы

### § 2.1. KI-079 — Свернуть/развернуть боковую панель

**Симптом:** `.chat-sidebar` фиксированной ширины 280px. На узких экранах
(< 1200px) лента сообщений становится тесной. Нет способа освободить место.

**Решение:** DeepSeek-style. Кнопка «Свернуть боковую панель» внутри sidebar
(рядом с «+ Новый чат»). В collapsed — кнопка «Открыть» появляется в chat-header.
Состояние — в `localStorage`.

### § 2.2. KI-078 — Поиск

**Симптом A (внутричатовый):** диалог из 100+ сообщений. Пользователь помнит
фразу, но не помнит, в каком сообщении. Server-side поиск (KI-068) ищет
по *всем* чатам — не помогает найти фрагмент *внутри* открытого.

**Симптом B (глобальный):** нужна быстрая модалка ⌘K-style с превью совпадений
(как в DeepSeek/ChatGPT) — без переключения фокуса на sidebar.

**Решение:** A + B.
- **A** — плавающая панель Ctrl+F в правом верхнем углу `.chat-main`,
  подсветка `<mark>`, навигация ↑/↓, авто-скролл. Frontend-only.
- **B** — модалка ⌘K (`Ctrl+K`) по центру, input + список чатов с превью,
  клик → открыть чат. Расширяет `/api/chats?search=` полем `snippet`.

---

## § 3. KI-079 — Collapse sidebar (DeepSeek-style)

### § 3.1. UX

| Элемент | Поведение |
|---------|-----------|
| **Кнопка «Свернуть»** | `#btn-collapse-sidebar` — внутри sidebar (справа от «+ Новый чат»). SVG panel-left. |
| **Кнопка «Открыть»** | `#btn-expand-sidebar` — в chat-header (слева от title). SVG panel-left. Видна только в collapsed. |
| **Collapsed** | `.chat-container.chat-sidebar-collapsed .chat-sidebar { width: 0 }`. |
| **Анимация** | CSS `transition: width .2s ease`. |
| **Сохранение** | `localStorage["chat.sidebarCollapsed"]` = `"true"` / `"false"`. |
| **Хоткей** | `Ctrl+B` — toggle (`preventDefault`, `!shiftKey`, `!altKey`). |
| **Mobile (< 768px)** | Collapse **отключён** (media query). Sidebar всегда виден. Кнопки скрыты. |

**Иконка** (Bootstrap Icons `layout-sidebar`, 16×16, `fill="currentColor"`):

```html
<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16"
     viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
  <path d="M0 3a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2zm5-1v12h9a1 1 0 0 0 1-1V3a1 1 0 0 0-1-1zM4 2H2a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h2z"/>
</svg>
```

### § 3.2. Архитектура (`chat.js`)

```javascript
const LS_KEY_SIDEBAR = 'chat.sidebarCollapsed';

const state = {
    // ...существующие...
    sidebarCollapsed: false,
};

function applySidebarCollapsed(collapsed) {
    state.sidebarCollapsed = !!collapsed;

    document.querySelector('.chat-container')
        ?.classList.toggle('chat-sidebar-collapsed', collapsed);

    try {
        localStorage.setItem(LS_KEY_SIDEBAR, collapsed ? 'true' : 'false');
    } catch { /* приватный режим */ }

    const btnCollapse = document.getElementById('btn-collapse-sidebar');
    if (btnCollapse) btnCollapse.hidden = collapsed;

    const btnExpand = document.getElementById('btn-expand-sidebar');
    if (btnExpand) btnExpand.hidden = !collapsed;
}

function toggleSidebar() {
    applySidebarCollapsed(!state.sidebarCollapsed);
}
```

**Инициализация** (в `initChatPage` до `bindEvents`):

```javascript
let saved = null;
try { saved = localStorage.getItem(LS_KEY_SIDEBAR); } catch { /* ignore */ }
if (saved === 'true') applySidebarCollapsed(true);
```

**Хоткей** (в `bindEvents`):

```javascript
document.addEventListener('keydown', (e) => {
    if (e.ctrlKey && !e.shiftKey && !e.altKey
        && e.key.toLowerCase() === 'b') {
        e.preventDefault();
        toggleSidebar();
    }
});
```

### § 3.3. `showEmptyState()` / `renderChatHeader()`

Header остаётся видимым всегда (в нём toggle при collapsed).
Скрывается только `#chat-header-info`:

```javascript
function showEmptyState() {
    const headerEl = document.getElementById('chat-header');
    if (headerEl) {
        headerEl.classList.remove('d-none');
        document.getElementById('chat-header-info')?.classList.add('d-none');
    }
    // ...остальное без изменений...
}

function renderChatHeader(chat) {
    const headerEl = document.getElementById('chat-header');
    if (headerEl) headerEl.classList.remove('d-none');
    document.getElementById('chat-header-info')?.classList.remove('d-none');
    // ...остальное без изменений...
}
```

### § 3.4. Файлы

- `IIChatTools.API/Views/Chat/Index.cshtml` — 2 кнопки + обёртка `#chat-header-info`.
- `IIChatTools.API/wwwroot/js/modules/chat.js` — `applySidebarCollapsed()`, `toggleSidebar()`, хоткей.
- `IIChatTools.API/wwwroot/css/chat.css` — `.chat-sidebar-icon-btn`, `.chat-sidebar-collapsed`, media query.
- `SharedResources.resx` + `.ru.resx` — 2 ключа.

### § 3.5. Локализация

| Ключ | RU | EN |
|------|----|----|
| `ChatSidebarCollapse` | Свернуть боковую панель | Collapse sidebar |
| `ChatSidebarExpand` | Открыть боковую панель | Open sidebar |

---

## § 4. KI-078 — Поиск

### § 4.1. Решение: A + B

**A. Внутричатовый поиск** (Ctrl+F) — плавающая панель в углу, подсветка
`<mark>`, навигация ↑/↓, авто-скролл. Frontend-only.

**B. Модалка ⌘K** (Ctrl+K) — по центру, input + список чатов с превью
совпадений, клик → открыть чат. Расширяет backend (snippet).

**Существующий inline `#chat-search` в sidebar** (KI-068, ChatGPT-style) —
**оставляем без изменений**. Это быстрый фильтр для sidebar, отдельная фича.

### § 4.2. Вариант A — UX

| Элемент | Поведение |
|---------|-----------|
| **Открытие** | `Ctrl+F` (перехват, `preventDefault`) ИЛИ кнопка 🔍 в `chat-header`. |
| **UI** | Плавающая панель `.chat-search-bar` в правом верхнем углу `.chat-main`. |
| **Поиск** | По `textContent` всех `.chat-message-content` (user + assistant). Регистронезависимый. |
| **Подсветка** | `<mark class="chat-search-hit">`. Активное — `.chat-search-hit-active` (жёлтый + синяя рамка). |
| **Навигация** | Счётчик `N из M`, ↑/↓ — циклический переход. Авто-скролл (`behavior: 'smooth', block: 'center'`). |
| **Закрытие** | Esc / клик вне / ✕. Удаляет все `<mark>`. |
| **Debounce** | 150ms. |
| **Пустой запрос** | Счётчик `0 / 0`. |
| **Нет совпадений** | Показать `ChatSearchInChatNoResults`. |

### § 4.3. Вариант A — ключевые функции (`chat.js`)

```javascript
const SEARCH_DEBOUNCE_MS = 150;

// state:
//   chatSearchMarks: []
//   chatSearchIndex: -1
//   chatSearchDebounce: null

openChatSearch()           // показать панель, focus, reset
closeChatSearch()          // скрыть панель, removeAllSearchMarks
applyChatSearch(query)     // обойти DOM, подсветить, обновить счётчик
removeAllSearchMarks()     // снять все <mark>, восстановить текстовые узлы
walkAndHighlight(rootEl, query, marksOut)  // TreeWalker по текстовым узлам
setActiveSearchMark(index) // сделать активным, scrollIntoView, cyclic
updateSearchCounter(c, t)  // «N из M» / «Ничего не найдено»
```

**Принципы:**
- Не кэшируем HTML — каждый поиск снимает старые `<mark>` через `unwrap` + `normalize()`.
- Только `.chat-message-content` — tool-блоки не трогаем.
- `<mark>` внутри `<code>` работает (подсветка поверх highlight.js).

### § 4.4. Вариант A — CSS

```css
.chat-search-bar {
    position: absolute;
    top: 12px;
    right: 12px;
    z-index: 20;
    display: flex;
    align-items: center;
    gap: .35rem;
    padding: .35rem .5rem;
    background: #fff;
    border: 1px solid #d0d7de;
    border-radius: .375rem;
    box-shadow: 0 2px 8px rgba(0, 0, 0, .12);
}
.chat-search-bar[hidden] { display: none !important; }

mark.chat-search-hit {
    background: #fff3a3;
    color: inherit;
    padding: 0 .05em;
    border-radius: .15rem;
}
mark.chat-search-hit.chat-search-hit-active {
    background: #ffd84d;
    box-shadow: 0 0 0 2px #0d6efd;
}
```

### § 4.5. Вариант B — UX (⌘K-модалка)

| Элемент | Поведение |
|---------|-----------|
| **Открытие** | `Ctrl+K` (перехват) ИЛИ кнопка 🔍 в navbar/header. |
| **UI** | Модалка по центру (`position: fixed; top: 15%`), ширина `min(600px, 90vw)`. |
| **Input** | 🔍 + placeholder + ✕. Autofocus. |
| **Список** | Результаты: title, время, сниппет (совпадение выделено `<mark>`). |
| **Навигация** | ↑/↓ — перемещение по результатам. Enter — открыть. |
| **Debounce** | 200ms (server-side запрос). |
| **Закрытие** | Esc / клик вне / ✕. |
| **Пусто** | «Ничего не найдено» или подсказка «Начните вводить». |

### § 4.6. Вариант B — Backend (расширение)

**Расширяем `/api/chats?search=` — добавляем `snippet`:**

```csharp
public class ChatListItemDto
{
    // ...существующие...
    public string MatchedField { get; set; }   // "title" | "content" | null
    public string Snippet { get; set; }        // ~100 символов вокруг совпадения
}
```

**`IChatService.SearchUserChatsAsync` — новый метод:**

```csharp
Task<IReadOnlyList<ChatSearchResult>> SearchUserChatsWithSnippetAsync(
    int userId, string search, CancellationToken ct);
```

Возвращает: `ChatId`, `Title`, `UpdatedAt`, `MessageCount`, `MatchedField`,
`Snippet` (совпадение в `[[...]]` или с offset'ами).

**Frontend** — рендерит `<mark>` по offset'ам.

### § 4.7. Вариант A + B — Локализация

**A** (внутричатовый):

| Ключ | RU | EN |
|------|----|----|
| `ChatSearchInChatOpen` | Поиск в чате | Search in chat |
| `ChatSearchInChatPlaceholder` | Поиск в чате… | Search in chat… |
| `ChatSearchInChatNoResults` | Ничего не найдено | No results |
| `ChatSearchInChatCounter` | {0} из {1} | {0} of {1} |
| `ChatSearchInChatPrev` | Предыдущее | Previous |
| `ChatSearchInChatNext` | Следующее | Next |
| `ChatSearchInChatClose` | Закрыть | Close |

**B** (⌘K-модалка):

| Ключ | RU | EN |
|------|----|----|
| `ChatSearchGlobalOpen` | Поиск по чатам | Search chats |
| `ChatSearchGlobalPlaceholder` | Поиск по содержимому чата… | Search in chat content… |
| `ChatSearchGlobalHint` | Введите запрос или выберите чат | Type to search |
| `ChatSearchGlobalNoResults` | Ничего не найдено | No results |
| `ChatSearchGlobalClose` | Закрыть | Close |

---

## § 5. Общие изменения

### § 5.1. Хоткеи

| Клавиша | Действие | Примечание |
|---------|----------|------------|
| `Ctrl+B` | Toggle sidebar | `preventDefault` — закладки Chrome |
| `Ctrl+F` | Поиск в активном чате (A) | `preventDefault` — перебивает нативный |
| `Ctrl+K` | Модалка поиска по всем чатам (B) | DeepSeek-style ⌘K |

Проверка: `e.ctrlKey && !e.shiftKey && !e.altKey` — не перехватывать
`Ctrl+Shift+B` / `Ctrl+Shift+F` / `Ctrl+Shift+K`.

### § 5.2. Совместимость

- Хоткеи — только на `/chat` (модуль `chat.js` подключён только там).
- `localStorage` может быть запрещён — все вызовы в `try/catch`.

---

## § 6. План работ

| # | Фаза | Что | Оценка | Статус |
|:-:|------|-----|:------:|:------:|
| 1 | Design | `DESIGN_SIDEBAR_SEARCH.md` | — | ✅ |
| 2 | KI-079 | Collapse sidebar (frontend + 2 `.resx` + docs) | ~1 ч | 🔜 |
| 3 | KI-078A | Внутричатовый поиск Ctrl+F (frontend + 7 `.resx` + docs) | ~2 ч | ⏸ |
| 4 | KI-078B | Модалка ⌘K (frontend + backend snippet + 5 `.resx` + docs) | ~4 ч | ⏸ |
| 5 | Smoke | Ручная проверка + mobile | ~30 мин | ⏸ |

**Итого:** ~7.5 ч.

---

## § 7. Definition of Done

### § 7.1. KI-079
- [ ] Кнопка «Свернуть» внутри sidebar, «Открыть» в header.
- [ ] Collapse → `width: 0`, без артефакта.
- [ ] `localStorage` + восстановление.
- [ ] `Ctrl+B`, `preventDefault`.
- [ ] Mobile (< 768px): collapse отключён.
- [ ] Локализация RU + EN (2 ключа).
- [ ] `chat-header` не пропадает без чата.
- [ ] CI зелёный.

### § 7.2. KI-078A
- [ ] `Ctrl+F` + 🔍 открывают панель.
- [ ] Подсветка `<mark>` всех совпадений.
- [ ] Активное — синяя рамка, авто-скролл.
- [ ] Счётчик `N из M`, ↑/↓ циклично.
- [ ] Esc / клик вне / ✕ — закрыть + снять marks.
- [ ] Локализация RU + EN (7 ключей).
- [ ] CI зелёный.

### § 7.3. KI-078B
- [ ] `Ctrl+K` + 🔍 открывают модалку.
- [ ] Input + debounce 200ms → `/api/chats?search=`.
- [ ] Список с title, временем, сниппетом (совпадение выделено `<mark>`).
- [ ] ↑/↓ — навигация, Enter — открыть чат.
- [ ] Esc / клик вне / ✕ — закрыть.
- [ ] Backend: `ChatListItemDto.MatchedField` + `.Snippet`.
- [ ] Локализация RU + EN (5 ключей).
- [ ] CI зелёный.

### § 7.4. Общие
- [ ] `dotnet build` — 0 warnings, 0 errors.
- [ ] `dotnet test` — N/N (frontend-only для 079/078A; +тесты для 078B).
- [ ] `CHANGELOG.md` — `[Unreleased] → Added`.
- [ ] `KNOWN_ISSUES.md` — KI-079, KI-078 → Fixed.
- [ ] `README.md` — Chat UI: collapse + search (A + B).
- [ ] Smoke через DevTools.

---

## § 8. Ссылки

- KI-079 — collapse sidebar.
- KI-078 — поиск (A — внутричатовый, B — модалка).
- KI-080 — поле ввода на всю ширину (отдельно).
- KI-068 — server-side поиск по чатам (Fixed v1.3.1) — база для B.
- RULES § 4.16 — ключи `.resx` — case-insensitive.
- RULES § 4.17 — локализация JS через `data-*`.
- RULES § 2.10 — MD-файлы в 4 бэктиках (снаружи 4, внутри 3).
- `docs/development/v1.4/DESIGN.md` — эталон формата.