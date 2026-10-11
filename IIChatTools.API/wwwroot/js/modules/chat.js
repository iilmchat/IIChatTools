/**
 * Модуль страницы /chat: sidebar + лента сообщений + SSE-стриминг.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.3
 *
 * Шаг 2.0.3 — SSE: отправка сообщений, стриминг ответа, обработка tool_call/tool_result.
 * Интеграция approval-модалки — Шаг 2.0.4.
 * KI-069 — inline-edit названия чата в sidebar (двойной клик или ✏️).
 * KI-068 — серверный поиск по названию + содержимому сообщений (debounce 300ms).
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { requestChatApproval } from './approvals.js';
import { initSpeechRecognition } from './speech.js';   // v1.13.0 (KI-140)

// ============ Состояние ============

/**
 * KI-079: ключ localStorage для состояния sidebar.
 */
const LS_KEY_SIDEBAR = 'chat.sidebarCollapsed';
// v1.11.0 (KI-126, Шаг 1G.1): ключ localStorage для вида отображения дебатов.
const LS_KEY_DEBATE_VIEW = 'chat.debateView';

// v1.13.x (KI-206): ключ localStorage для состояния thumbs sidebar.
const LS_KEY_PAGES_THUMBS = 'chat.pagesThumbsCollapsed';

/** KI-206: шаг зума для кнопок +/−. */
const PAGES_ZOOM_STEP = 0.25;
/** KI-206: границы зума. */
const PAGES_ZOOM_MIN = 0.25;
const PAGES_ZOOM_MAX = 4.0;

const state = {
    chats: [],
    models: [],
    defaultModel: null,
    activeChatId: null,
    activeChat: null,
    activeChatMessageCount: 0,   // Фаза 2.2.1b: кол-во сообщений активного чата
    searchQuery: '',             // Фаза 2.2.3: поиск по названию чата
    isStreaming: false,          // блокировка отправки во время стрима
    autoScroll: true,            // включён, пока пользователь у нижнего края
    abortController: null,       // Фаза 2.1.3: AbortController для Stop
    editingChatId: null,         // KI-069: id чата в режиме inline-edit (или null)
    sidebarCollapsed: false,     // KI-079: sidebar свёрнут (DeepSeek-style)
    // KI-078A: внутричатовый поиск
    chatSearchMarks: [],         // массив <mark> в порядке появления
    chatSearchIndex: -1,         // индекс активного совпадения (-1 = нет)
    chatSearchDebounce: null,    // таймер debounce ввода
    // KI-078B: ⌘K-модалка поиска по всем чатам
    globalSearchResults: [],     // массив ChatListItemDto с snippet
    globalSearchIndex: 0,        // индекс активного результата
    globalSearchDebounce: null,  // таймер debounce ввода
    globalSearchRequestId: 0,    // защита от гонок fetch

    // KI-083 (Шаг 6D): вложения чата (RAG)
    attachments: [],             // ChatAttachmentDto[]
    ragChunkCount: 0,            // суммарное количество чанков в my_rag_docs для активного чата

    // v1.13.x (KI-204): polling OCR-прогресса.
    // { stopped: bool, timerId: number|null } или null, если не активен.
    ocrPolling: null,

    // v1.13.x (KI-205): постраничный просмотр PNG сканов.
    // v1.13.x (KI-206): + zoom (число), thumbsCollapsed (bool),
    //                    imageNaturalSize {w, h}, thumbsObserver.
    // v1.13.x (KI-207): + textLayer (DOM), textLayerDto, searchHits, searchIndex.
    pagesViewer: null,

    // v1.11.0 (KI-126, Шаг 1G.1): вид отображения дебатов ('dialog' | 'collapsed')
    debateView: 'collapsed',

    // v1.11.0 (KI-126, Шаг 1G.2): кэш данных дебатов по sessionId.
    // Нужен для перерендера блока при смене вида (dialog ⇄ collapsed).
    // Формат: { [sessionId]: { started, rounds: [], completed } }
    debates: {},

    // v1.11.0 (KI-126, Шаг 1G.3): черновики feedback между раундами.
    // Формат: { [sessionId]: { status, expiresAt, text } }
    //   status: 'pending' | 'sending' | 'sent' | 'skipped' | 'timeout'
    feedbackDrafts: {},

    // v1.11.0 (KI-129): маппинг toolCallId → sessionId — для восстановления
    // блоков Actor-Critic при F5. Заполняется в prefillDebatesFromChatDetail
    // при selectChat. Ключ — tool_call.id из ToolCallsJson assistant-сообщения.
    debateSessionByToolCallId: {},

    // v1.13.13 (KI-230): активный Continue-стрим. null — не идёт.
    // { bubble, contentEl, appendEl, assistantMessageId, appendedRaw }
    appendTarget: null,
};

// ============ Инициализация ============

/**
 * Инициализирует страницу чата.
 */
export async function initChatPage() {
    // KI-079: восстановить состояние sidebar из localStorage (до bindEvents).
    let savedCollapsed = null;
    try {
        savedCollapsed = localStorage.getItem(LS_KEY_SIDEBAR);
    } catch {
        // Приватный режим / запрет localStorage — игнорируем.
    }
    if (savedCollapsed === 'true') applySidebarCollapsed(true);

    // v1.11.0 (KI-126, Шаг 1G.1): восстановить вид отображения дебатов.
    let savedDebateView = null;
    try {
        savedDebateView = localStorage.getItem(LS_KEY_DEBATE_VIEW);
    } catch {
        // Приватный режим / запрет localStorage — игнорируем.
    }
    state.debateView = savedDebateView === 'dialog' ? 'dialog' : 'collapsed';

    bindEvents();
    applyDebateViewUI();
    initLightboxHandlers();   // KI-229

    // v1.13.0 (KI-140): инициализация голосового ввода (Speech Recognition).
    // Кнопка активна только при Speech:Enabled = true (data-speech-enabled
    // на #chat-messages, проставляется из Razor).
    const messagesEl = document.getElementById('chat-messages');
    initSpeechRecognition(
        document.getElementById('btn-speech'),
        document.getElementById('chat-input'),
        messagesEl,
        messagesEl?.dataset.speechEnabled || 'false');

    // v1.11.0 (KI-126, Шаг 1G.3): глобальный интервал для countdown
    // в feedback-блоках (1 сек). Ничего не делает, если активных нет.
    setInterval(updateAllFeedbackCountdowns, 1000);

    await loadModels();
    await loadChats();

    const urlChatId = readUrlChatId();
    if (urlChatId && state.chats.some(c => c.id === urlChatId)) {
        await selectChat(urlChatId);
    } else if (state.chats.length > 0) {
        await selectChat(state.chats[0].id);
    } else {
        showEmptyState();
    }

    console.log('[chat] KI-069 готов: inline-edit названия в sidebar');
}

// ============ Привязка событий ============

function bindEvents() {
    document.getElementById('btn-new-chat')
        ?.addEventListener('click', createChat);

    document.getElementById('btn-delete-chat')
        ?.addEventListener('click', () => {
            if (state.activeChatId) deleteChat(state.activeChatId);
        });

    const input = document.getElementById('chat-input');
    const btnSend = document.getElementById('btn-send');

    if (input) {
        // Автоувеличение высоты
        input.addEventListener('input', () => autoResizeTextarea(input));
        // Enter — отправка, Shift+Enter — новая строка
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        });
        // KI-231: Paste Image из clipboard (Ctrl+V) → загрузка в RAG.
        input.addEventListener('paste', onChatInputPaste);
    }

    // KI-231: Drag-and-Drop файлов на поле ввода.
    initDragAndDrop();

    if (btnSend) {
        btnSend.addEventListener('click', sendMessage);
    }

    // Фаза 2.1.3: кнопка Stop
    const btnStop = document.getElementById('btn-stop');
    if (btnStop) {
        btnStop.addEventListener('click', stopStreaming);
    }

    // Фаза 2.2.4: селектор модели
    const modelSelect = document.getElementById('chat-model-select');
    if (modelSelect) {
        modelSelect.addEventListener('change', onChatModelChanged);
    }

    // v1.11.0 (KI-126, Шаг 1G.1): переключатель вида отображения дебатов.
    document.getElementById('btn-debate-view')
        ?.addEventListener('click', toggleDebateView);

    // KI-079: кнопка toggle (одна, работает в обоих состояниях)
    document.getElementById('btn-collapse-sidebar')
        ?.addEventListener('click', toggleSidebar);

    // KI-078B: поиск в collapsed sidebar → открыть ⌘K-модалку.
    document.getElementById('btn-search-sidebar')
        ?.addEventListener('click', openGlobalSearch);

    // KI-078A: кнопка 🔍 в chat-header — открыть панель поиска по активному чату.
    document.getElementById('btn-search-in-chat')
        ?.addEventListener('click', openChatSearch);

    // KI-078A: панель поиска — input, кнопки ↑/↓/✕.
    const inChatSearchBar = document.getElementById('chat-search-bar');
    if (inChatSearchBar) {
        const searchInput = document.getElementById('chat-search-input');

        searchInput?.addEventListener('input', (e) => {
            const q = e.target.value;
            if (state.chatSearchDebounce) {
                clearTimeout(state.chatSearchDebounce);
            }
            state.chatSearchDebounce = setTimeout(() => {
                state.chatSearchDebounce = null;
                applyChatSearch(q);
            }, CHAT_SEARCH_DEBOUNCE_MS);
        });

        searchInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                setActiveChatSearchMark(
                    state.chatSearchIndex + (e.shiftKey ? -1 : 1)
                );
            }
        });

        inChatSearchBar.querySelector('[data-action="prev"]')
            ?.addEventListener('click', () =>
                setActiveChatSearchMark(state.chatSearchIndex - 1));
        inChatSearchBar.querySelector('[data-action="next"]')
            ?.addEventListener('click', () =>
                setActiveChatSearchMark(state.chatSearchIndex + 1));
        inChatSearchBar.querySelector('[data-action="close"]')
            ?.addEventListener('click', closeChatSearch);
    }

    // KI-078B: ⌘K-модалка — input, закрытие, клик вне, клик по результату.
    const globalSearchModal = document.getElementById('chat-global-search');
    if (globalSearchModal) {
        const gsInput = document.getElementById('chat-global-search-input');

        gsInput?.addEventListener('input', (e) => onGlobalSearchInput(e.target.value));

        gsInput?.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                moveGlobalSearchActive(+1);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                moveGlobalSearchActive(-1);
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const items = state.globalSearchResults;
                if (items.length > 0 && state.globalSearchIndex >= 0) {
                    const target = items[state.globalSearchIndex];
                    if (target && target.id) {
                        openChatFromGlobalSearch(target.id);
                    }
                }
            }
        });

        // Клик по backdrop / ✕ — закрыть.
        globalSearchModal.querySelectorAll('[data-action="close"]')
            .forEach(el => el.addEventListener('click', closeGlobalSearch));

        // Клик по результату — открыть чат.
        const gsResults = document.getElementById('chat-global-search-results');
        gsResults?.addEventListener('click', (e) => {
            const item = e.target.closest('.chat-global-search-item');
            if (!item) return;
            const chatId = parseInt(item.dataset.chatId, 10);
            if (Number.isFinite(chatId)) {
                openChatFromGlobalSearch(chatId);
            }
        });

        // Hover по результату — синхронизировать активный индекс.
        gsResults?.addEventListener('mouseover', (e) => {
            const item = e.target.closest('.chat-global-search-item');
            if (!item) return;
            const idx = parseInt(item.dataset.index, 10);
            if (Number.isFinite(idx)) {
                state.globalSearchIndex = idx;
                gsResults.querySelectorAll('.chat-global-search-item').forEach((el, i) => {
                    el.classList.toggle('chat-global-search-item-active', i === idx);
                });
            }
        });

        // KI-078B fix: кнопка «Очистить поле» — сброс input без закрытия модалки.
        document.getElementById('chat-global-search-clear')
            ?.addEventListener('click', () => {
                const input = document.getElementById('chat-global-search-input');
                if (input) {
                    input.value = '';
                    input.focus();
                }
                onGlobalSearchInput('');
            });
    }

    // v1.13.x (KI-205 + KI-206): модалка постраничного просмотра PNG.
    const pagesModal = document.getElementById('chat-pages-modal');
    if (pagesModal) {
        // Backdrop + ✕ — закрыть.
        pagesModal.querySelectorAll('[data-action="close"]').forEach(el => {
            el.addEventListener('click', closePagesViewer);
        });

        // KI-206: thumbs toggle.
        document.getElementById('chat-pages-thumbs-toggle')
            ?.addEventListener('click', togglePagesThumbs);

        // KI-207-fix4: кнопка открытия поиска в toolbar модалки.
        document.getElementById('chat-pages-search-open')
            ?.addEventListener('click', openPagesSearch);

        // KI-208: экспорт страниц.
        document.getElementById('chat-pages-export-zip')
            ?.addEventListener('click', exportPagesZip);
        document.getElementById('chat-pages-export-text')
            ?.addEventListener('click', exportPagesText);
        document.getElementById('chat-pages-export-pdf')
            ?.addEventListener('click', exportPagesPdf);

        // KI-206: зум (кнопки + / − / 100%).
        pagesModal.querySelectorAll('[data-zoom]').forEach(btn => {
            btn.addEventListener('click', () => {
                const kind = btn.dataset.zoom;
                if (kind === 'in') setPagesZoom(getPagesZoom() + PAGES_ZOOM_STEP);
                else if (kind === 'out') setPagesZoom(getPagesZoom() - PAGES_ZOOM_STEP);
                else if (kind === 'reset') setPagesZoom(1.0);
            });
        });

        // KI-206: fit-width / fit-page.
        pagesModal.querySelectorAll('[data-fit]').forEach(btn => {
            btn.addEventListener('click', () => {
                const kind = btn.dataset.fit;
                if (kind === 'width') fitPagesViewer('width');
                else if (kind === 'page') fitPagesViewer('page');
            });
        });

        // KI-206: input «Стр. N / M» + Enter.
        const gotoInput = document.getElementById('chat-pages-goto-input');
        if (gotoInput) {
            gotoInput.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    const n = parseInt(gotoInput.value, 10);
                    if (Number.isFinite(n)) gotoPagesViewer(n);
                    gotoInput.blur();
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    e.stopPropagation();
                    gotoInput.blur();
                }
            });
        }

        // KI-206: скролл колесом мыши (prev / next page, debounce 300 мс).
        const viewport = pagesModal.querySelector('.chat-pages-viewport');
        if (viewport) {
            let wheelAccum = 0;
            let wheelLocked = false;
            let wheelResetTimer = null;

            viewport.addEventListener('wheel', (e) => {
                // Ctrl+wheel — нативный зум браузера, не мешаем.
                if (e.ctrlKey) return;

                // Если контент страницы скроллится вертикально (zoom > fit),
                // сначала даём скроллить картинку, потом листаем.
                const canScrollInside = viewport.scrollHeight > viewport.clientHeight;
                if (canScrollInside) {
                    const atTop = viewport.scrollTop <= 0;
                    const atBottom = viewport.scrollTop + viewport.clientHeight >= viewport.scrollHeight - 1;
                    if (e.deltaY < 0 && !atTop) return;       // листаем содержимое вверх
                    if (e.deltaY > 0 && !atBottom) return;    // листаем содержимое вниз
                }

                if (wheelLocked) {
                    e.preventDefault();
                    return;
                }

                wheelAccum += e.deltaY;
                e.preventDefault();

                if (Math.abs(wheelAccum) >= 60) {
                    const dir = wheelAccum > 0 ? +1 : -1;
                    navigatePagesViewer(dir);

                    wheelLocked = true;
                    if (wheelResetTimer) clearTimeout(wheelResetTimer);
                    wheelResetTimer = setTimeout(() => {
                        wheelLocked = false;
                        wheelAccum = 0;
                    }, 300);
                    wheelAccum = 0;
                }
            }, { passive: false });
        }

        // v1.13.x (KI-207): поисковая панель в модалке страниц.
        const searchBar = document.getElementById('chat-pages-search-bar');
        if (searchBar) {
            const searchInput = document.getElementById('chat-pages-search-input');

            searchInput?.addEventListener('input', (e) => {
                applyPagesSearch(e.target.value);
            });

            searchInput?.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    e.stopPropagation();   // KI-207-fix5: не даём уйти на modal
                    navigatePagesSearch(e.shiftKey ? -1 : +1);
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    e.stopPropagation();
                    e.stopImmediatePropagation();
                    closePagesSearch();
                }
            });

            // KI-207-fix5: data-search-action — отдельный атрибут, чтобы не
            // ловить общий [data-action="close"] модалки (иначе ✕ в поиске
            // закрывал всю модалку — обе функции на одной кнопке).
            searchBar.querySelector('[data-search-action="prev"]')
                ?.addEventListener('click', (e) => {
                    e.stopPropagation();
                    navigatePagesSearch(-1);
                });
            searchBar.querySelector('[data-search-action="next"]')
                ?.addEventListener('click', (e) => {
                    e.stopPropagation();
                    navigatePagesSearch(+1);
                });
            searchBar.querySelector('[data-search-action="close"]')
                ?.addEventListener('click', (e) => {
                    e.stopPropagation();
                    e.stopImmediatePropagation();
                    closePagesSearch();
                });
        }
    }

    // KI-083 (Шаг 6D): вложения чата (RAG)
    document.getElementById('btn-attach-file')
        ?.addEventListener('click', () => {
            document.getElementById('chat-file-input')?.click();
        });

    document.getElementById('chat-file-input')
        ?.addEventListener('change', (e) => {
            const files = Array.from(e.target.files || []);
            if (files.length > 0) uploadFiles(files);
            // Сброс value — иначе повторный выбор того же файла не сработает.
            e.target.value = '';
        });

    // Делегированный обработчик: удалить chip / очистить RAG / открыть страницы.
    document.getElementById('chat-attachments-bar')
        ?.addEventListener('click', (e) => {
            const removeBtn = e.target.closest('[data-attachment-remove]');
            if (removeBtn) {
                e.preventDefault();
                e.stopPropagation();
                const id = parseInt(removeBtn.dataset.attachmentRemove, 10);
                if (Number.isFinite(id)) deleteAttachment(id);
                return;
            }
            const clearBtn = e.target.closest('[data-action="clear-rag"]');
            if (clearBtn) {
                e.preventDefault();
                e.stopPropagation();
                clearAttachments();
                return;
            }
            // v1.13.x (KI-205): открыть постраничный просмотр PNG.
            const pagesBtn = e.target.closest('[data-pages-open]');
            if (pagesBtn) {
                e.preventDefault();
                e.stopPropagation();
                const id = parseInt(pagesBtn.dataset.pagesOpen, 10);
                if (Number.isFinite(id)) openPagesViewer(id);
                return;
            }
        });

    // KI-068: поиск по чатам — server-side (title + content), debounce 300ms
    const searchInput = document.getElementById('chat-search');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            // KI-069: сохранить активный inline-edit перед перерисовкой sidebar
            _flushActiveInlineEdit();
            state.searchQuery = e.target.value || '';
            _scheduleSearch();
        });
    }

    // === Скроллинг (Фаза 2.0.5b) ===

    // Слушатель скролла на ленте сообщений — отслеживает позицию пользователя
    const messagesEl = document.getElementById('chat-messages');
    if (messagesEl) {
        messagesEl.addEventListener('scroll', onMessagesScroll);
        // Делегированный обработчик кнопок действий с сообщением (Copy)
        messagesEl.addEventListener('click', onMessageActionClick);
        // KI-229: делегированный click по <img data-lightbox-src> и
        // кнопке [data-open-screenshot] — открыть lightbox.
        messagesEl.addEventListener('click', onLightboxTriggerClick);
        // KI-235: делегированный click по suggested-prompt — подстановка в input.
        messagesEl.addEventListener('click', onSuggestedPromptClick);

        // v1.11.0 (KI-126, Шаг 1G.3): сохранение ввода feedback-поля —
        // при перерендере контейнера (смена вида) текст не теряется.
        messagesEl.addEventListener('input', (e) => {
            if (!e.target.matches('.chat-debate-feedback textarea')) return;
            const container = e.target.closest('[data-debate-session-id]');
            if (!container) return;
            const sid = parseInt(container.dataset.debateSessionId, 10);
            if (!Number.isFinite(sid)) return;
            const draft = state.feedbackDrafts[sid];
            if (draft) draft.text = e.target.value;
        });
    }

    // Динамически создаём кнопку «↓ Вниз» (не трогаем Razor)
    const mainEl = document.querySelector('.chat-main');
    if (mainEl && !document.getElementById('chat-scroll-down')) {
        const btn = document.createElement('button');
        btn.id = 'chat-scroll-down';
        btn.type = 'button';
        btn.className = 'chat-scroll-down';
        btn.title = 'Вниз';
        btn.setAttribute('aria-label', 'Прокрутить вниз');
        btn.innerHTML = '↓';
        btn.hidden = true;
        btn.addEventListener('click', () => scrollToBottom(true));
        mainEl.appendChild(btn);
    }

    // KI-079: глобальный хоткей Ctrl+B (toggle sidebar).
    // KI-078A: Ctrl+F (поиск в активном чате), Escape (закрыть панель).
    // KI-078B: Ctrl+K (⌘K-модалка поиска по всем чатам).
    // Проверяем !shiftKey/!altKey, чтобы не перехватывать Ctrl+Shift+B/F/K.
    // Работает только на /chat (модуль подключён только там).
    // KI-207-fix5: e.code (физический код) вместо e.key (зависит от раскладки).
    // На РУССКОЙ раскладке клавиша F даёт e.key === 'а' (кириллица) →
    // наш обработчик не срабатывал, Chrome открывал нативный поиск.
    // Это объясняло «нестабильность»: на EN работало, на RU — нет.
    const isKey = (e, code, ...legacyKeys) => {
        if (e.code) return e.code === code;
        // Fallback для старых браузеров без KeyboardEvent.code.
        return legacyKeys.some(k => e.key && e.key.toLowerCase() === k);
    };

    window.addEventListener('keydown', (e) => {
        const pmNav = document.getElementById('chat-pages-modal');
        if (pmNav && !pmNav.hidden) {
            // Ctrl+F — открыть поиск по text layer.
            if (e.ctrlKey && !e.shiftKey && !e.altKey && isKey(e, 'KeyF', 'f')) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                openPagesSearch();
                return;
            }

            // Ctrl+= / Ctrl+- / Ctrl+0 — зум.
            if (e.ctrlKey || e.metaKey) {
                if (isKey(e, 'Equal', '=', '+')
                    || isKey(e, 'NumpadAdd')) {
                    e.preventDefault();
                    setPagesZoom(getPagesZoom() + PAGES_ZOOM_STEP);
                    return;
                }
                if (isKey(e, 'Minus', '-', '_')
                    || isKey(e, 'NumpadSubtract')) {
                    e.preventDefault();
                    setPagesZoom(getPagesZoom() - PAGES_ZOOM_STEP);
                    return;
                }
                if (isKey(e, 'Digit0', '0')
                    || isKey(e, 'Numpad0')) {
                    e.preventDefault();
                    setPagesZoom(1.0);
                    return;
                }
            }

            // Escape — закрыть поиск (если открыт) / потом модалку.
            if (e.key === 'Escape') {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                const psb = document.getElementById('chat-pages-search-bar');
                if (psb && !psb.hidden) {
                    closePagesSearch();
                } else {
                    closePagesViewer();
                }
                return;
            }

            // ←/→/↑/↓ — навигация по страницам (если фокус не в input).
            const ae = document.activeElement;
            const inInput = ae && (ae.tagName === 'INPUT' || ae.tagName === 'TEXTAREA');
            if (!inInput) {
                if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
                    e.preventDefault();
                    navigatePagesViewer(-1);
                    return;
                }
                if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
                    e.preventDefault();
                    navigatePagesViewer(+1);
                    return;
                }
            }

            // Прочие Ctrl-хоткеи — игнорируем.
            if (e.ctrlKey && !e.shiftKey && !e.altKey) {
                if (isKey(e, 'KeyB', 'b') || isKey(e, 'KeyK', 'k')) {
                    e.preventDefault();
                    return;
                }
            }
        }

        // ==================================================================
        // Ниже — глобальные hotkeys страницы /chat (модалка страниц закрыта).
        // ==================================================================
        if (e.ctrlKey && !e.shiftKey && !e.altKey) {
            if (isKey(e, 'KeyB', 'b')) {
                e.preventDefault();
                toggleSidebar();
                return;
            }
            if (isKey(e, 'KeyF', 'f')) {
                e.preventDefault();
                openChatSearch();
                return;
            }
            if (isKey(e, 'KeyK', 'k')) {
                e.preventDefault();
                openGlobalSearch();
                return;
            }
        }

        if (e.key === 'Escape') {
            const gsModal = document.getElementById('chat-global-search');
            if (gsModal && !gsModal.hidden) {
                e.preventDefault();
                closeGlobalSearch();
                return;
            }
            const bar = document.getElementById('chat-search-bar');
            if (bar && !bar.hidden) {
                e.preventDefault();
                closeChatSearch();
                return;
            }
        }
    }, true);

    // KI-078A: клик вне панели поиска — закрыть (кроме клика по самой панели
    // и по кнопке-триггеру 🔍 в header).
    document.addEventListener('click', (e) => {
        const bar = document.getElementById('chat-search-bar');
        if (!bar || bar.hidden) return;
        if (bar.contains(e.target)) return;
        if (e.target.closest('#btn-search-in-chat')) return;
        closeChatSearch();
    });
}

// ============ KI-079: Collapse/expand sidebar (DeepSeek-style) ============

/**
 * KI-079: применяет состояние collapse sidebar к DOM, обновляет localStorage
 * и переключает видимость кнопок «Свернуть» (в sidebar) / «Открыть» (в header).
 * @param {boolean} collapsed true — sidebar свёрнут
 */
function applySidebarCollapsed(collapsed) {
    state.sidebarCollapsed = !!collapsed;

    document.querySelector('.chat-container')
        ?.classList.toggle('chat-sidebar-collapsed', collapsed);

    try {
        localStorage.setItem(LS_KEY_SIDEBAR, collapsed ? 'true' : 'false');
    } catch {
        // Приватный режим — игнорируем.
    }

    // KI-079 fix: кнопка-toggle остаётся в sidebar всегда (mini-rail).
    // Меняем только title/aria-label в зависимости от состояния.
    const btn = document.getElementById('btn-collapse-sidebar');
    if (btn) {
        const label = collapsed
            ? (btn.dataset.labelExpand || 'Открыть боковую панель')
            : (btn.dataset.labelCollapse || 'Свернуть боковую панель');
        btn.title = label;
        btn.setAttribute('aria-label', label);
    }
}

/**
 * KI-079: переключает sidebar между свёрнутым и развёрнутым состоянием.
 */
function toggleSidebar() {
    applySidebarCollapsed(!state.sidebarCollapsed);
}

// ============ v1.11.0 (KI-126, Шаг 1G.1): Вид отображения дебатов ============

/**
 * Переключает вид отображения раундов Actor-Critic.
 * Сохраняет выбор в localStorage и перерисовывает уже отображённые раунды.
 */
function toggleDebateView() {
    state.debateView = state.debateView === 'dialog' ? 'collapsed' : 'dialog';
    try {
        localStorage.setItem(LS_KEY_DEBATE_VIEW, state.debateView);
    } catch {
        // Приватный режим — игнорируем.
    }
    applyDebateViewUI();
    refreshDebateViews();
}

/**
 * Обновляет иконку и title кнопки-селектора в зависимости от текущего вида.
 */
function applyDebateViewUI() {
    const btn = document.getElementById('btn-debate-view');
    if (!btn) return;

    const isDialog = state.debateView === 'dialog';
    const label = isDialog
        ? (btn.dataset.labelCollapsed || 'Collapsed')
        : (btn.dataset.labelDialog || 'Dialog');

    btn.title = label;
    btn.setAttribute('aria-label', label);

    // Смена SVG: в диалоговом режиме — иконка списка, в свернутом — иконка диалога.
    // Используем inline SVG для избежания внешних зависимостей.
    if (isDialog) {
        // Иконка «свернуть» (список)
        btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><path d="M2.5 12a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5z"/></svg>`;
    } else {
        // Иконка «развернуть» (диалог)
        btn.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><path d="M2 2a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H2zm0 1h12a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zm0 5a1 1 0 0 1 1-1h8a1 1 0 0 1 0 2H3a1 1 0 0 1-1-1z"/></svg>`;
    }
}

// refreshDebateViews() — см. секцию «Рендер дебатов (Actor-Critic)» ниже.
// В Шаге 1G.2 она была переписана (полный перерендер через
// renderDebateContainerInner), а старая версия в этой секции — удалена.
// Дублирование вызывало SyntaxError в браузере.

// ============ KI-078A: Внутричатовый поиск (Ctrl+F) ============

/** Задержка debounce при вводе поискового запроса (мс). */
const CHAT_SEARCH_DEBOUNCE_MS = 150;

/**
 * KI-078A: открывает панель поиска, ставит фокус в input, сбрасывает счётчик.
 */
function openChatSearch() {
    const bar = document.getElementById('chat-search-bar');
    if (!bar) return;

    bar.hidden = false;
    const input = document.getElementById('chat-search-input');
    if (input) {
        input.value = '';
        input.focus();
    }
    updateChatSearchCounter(0, 0);
}

/**
 * KI-078A: закрывает панель, снимает все подсветки, сбрасывает state.
 */
function closeChatSearch() {
    const bar = document.getElementById('chat-search-bar');
    if (!bar) return;

    bar.hidden = true;
    removeAllChatSearchMarks();
    state.chatSearchMarks = [];
    state.chatSearchIndex = -1;
    if (state.chatSearchDebounce) {
        clearTimeout(state.chatSearchDebounce);
        state.chatSearchDebounce = null;
    }
    updateChatSearchCounter(0, 0);
}

/**
 * KI-078A: применяет поиск — подсвечивает совпадения во всех .chat-message-content.
 * @param {string} query
 */
function applyChatSearch(query) {
    removeAllChatSearchMarks();
    state.chatSearchMarks = [];
    state.chatSearchIndex = -1;

    const trimmed = (query || '').trim();
    if (trimmed.length === 0) {
        updateChatSearchCounter(0, 0);
        return;
    }

    const contents = document.querySelectorAll(
        '#chat-messages .chat-message .chat-message-content'
    );
    const marks = [];
    for (const content of contents) {
        walkAndHighlight(content, trimmed, marks);
    }

    state.chatSearchMarks = marks;

    if (marks.length === 0) {
        updateChatSearchCounter(0, 0);
        return;
    }

    setActiveChatSearchMark(0);
}

/**
 * KI-078A: снимает все <mark class="chat-search-hit">, восстанавливая текстовые узлы.
 * Идемпотентно: повторный вызов на чистом DOM — no-op.
 */
function removeAllChatSearchMarks() {
    document.querySelectorAll('mark.chat-search-hit').forEach(mark => {
        const parent = mark.parentNode;
        if (!parent) return;
        while (mark.firstChild) {
            parent.insertBefore(mark.firstChild, mark);
        }
        parent.removeChild(mark);
        parent.normalize();
    });
}

/**
 * KI-078A: обходит текстовые узлы внутри rootEl и оборачивает совпадения в <mark>.
 * Регистронезависимый поиск через toLowerCase (для Unicode — кириллица работает).
 * @param {HTMLElement} rootEl
 * @param {string} query
 * @param {HTMLElement[]} marksOut
 */
function walkAndHighlight(rootEl, query, marksOut) {
    const lowerQuery = query.toLowerCase();
    const walker = document.createTreeWalker(rootEl, NodeFilter.SHOW_TEXT);
    const textNodes = [];
    let n;
    while ((n = walker.nextNode())) textNodes.push(n);

    for (const textNode of textNodes) {
        const text = textNode.nodeValue;
        if (!text) continue;

        const lowerText = text.toLowerCase();
        let pos = 0;
        let idx;
        const parts = [];

        while ((idx = lowerText.indexOf(lowerQuery, pos)) !== -1) {
            if (idx > pos) {
                parts.push({ text: text.substring(pos, idx), hit: false });
            }
            parts.push({
                text: text.substring(idx, idx + query.length),
                hit: true
            });
            pos = idx + query.length;
        }

        if (parts.length === 0) continue;
        if (pos < text.length) {
            parts.push({ text: text.substring(pos), hit: false });
        }

        const fragment = document.createDocumentFragment();
        for (const p of parts) {
            if (p.hit) {
                const mark = document.createElement('mark');
                mark.className = 'chat-search-hit';
                mark.textContent = p.text;
                fragment.appendChild(mark);
                marksOut.push(mark);
            } else {
                fragment.appendChild(document.createTextNode(p.text));
            }
        }
        textNode.parentNode.replaceChild(fragment, textNode);
    }
}

/**
 * KI-078A: делает mark с указанным индексом активным и скроллит к нему.
 * Индекс циклически нормализуется — выход за границы не бросает.
 * @param {number} index
 */
function setActiveChatSearchMark(index) {
    const marks = state.chatSearchMarks;
    if (!marks || marks.length === 0) return;

    for (const m of marks) {
        m.classList.remove('chat-search-hit-active');
    }

    const n = marks.length;
    index = ((index % n) + n) % n;

    state.chatSearchIndex = index;
    const active = marks[index];
    active.classList.add('chat-search-hit-active');
    active.scrollIntoView({ behavior: 'smooth', block: 'center' });

    updateChatSearchCounter(index + 1, n);
}

/**
 * KI-078A: обновляет счётчик совпадений «N из M» (или «Ничего не найдено»).
 * @param {number} current
 * @param {number} total
 */
function updateChatSearchCounter(current, total) {
    const counter = document.getElementById('chat-search-counter');
    const bar = document.getElementById('chat-search-bar');
    if (!counter) return;

    if (total === 0) {
        const noResults = bar?.dataset.labelNoresults || 'Ничего не найдено';
        counter.textContent = noResults;
        return;
    }

    const template = bar?.dataset.labelCounter || '{0} / {1}';
    counter.textContent = template
        .replace('{0}', String(current))
        .replace('{1}', String(total));
}

// ============ KI-078B: ⌘K-модалка поиска по всем чатам (Ctrl+K) ============

/** Задержка debounce при вводе в ⌘K-модалке (мс). */
const GLOBAL_SEARCH_DEBOUNCE_MS = 200;

/** Ключ localStorage для запоминания последнего запроса (опционально). */
const LS_KEY_GLOBAL_SEARCH = 'chat.globalSearch.lastQuery';

/**
 * KI-078B: открывает ⌘K-модалку, ставит фокус в input, показывает подсказку.
 */
function openGlobalSearch() {
    const modal = document.getElementById('chat-global-search');
    if (!modal) return;

    modal.hidden = false;
    document.body.classList.add('chat-global-search-open');

    const input = document.getElementById('chat-global-search-input');
    if (input) {
        input.value = '';
        input.focus();
    }

    state.globalSearchResults = [];
    state.globalSearchIndex = 0;
    renderGlobalSearchResults([], '');
}

/**
 * KI-078B: закрывает ⌘K-модалку, сбрасывает state.
 */
function closeGlobalSearch() {
    const modal = document.getElementById('chat-global-search');
    if (!modal) return;

    modal.hidden = true;
    document.body.classList.remove('chat-global-search-open');

    if (state.globalSearchDebounce) {
        clearTimeout(state.globalSearchDebounce);
        state.globalSearchDebounce = null;
    }

    state.globalSearchResults = [];
    state.globalSearchIndex = 0;
}

/**
 * KI-078B: обработчик ввода — debounce → fetch /api/chats?search=.
 * @param {string} query
 */
function onGlobalSearchInput(query) {
    if (state.globalSearchDebounce) {
        clearTimeout(state.globalSearchDebounce);
    }

    const trimmed = (query || '').trim();

    // KI-078B fix: показать/скрыть кнопку «Очистить поле».
    const clearBtn = document.getElementById('chat-global-search-clear');
    if (clearBtn) clearBtn.hidden = trimmed.length === 0;

    if (trimmed.length === 0) {
        state.globalSearchResults = [];
        state.globalSearchIndex = 0;
        renderGlobalSearchResults([], '');
        return;
    }

    state.globalSearchDebounce = setTimeout(async () => {
        state.globalSearchDebounce = null;

        const requestId = ++state.globalSearchRequestId;

        try {
            const res = await apiGet(`/api/chats?search=${encodeURIComponent(trimmed)}`);
            if (requestId !== state.globalSearchRequestId) return;   // гонка — отбрасываем

            const items = res.success ? (res.data || []) : [];
            state.globalSearchResults = items;
            state.globalSearchIndex = items.length > 0 ? 0 : -1;
            renderGlobalSearchResults(items, trimmed);
        } catch (ex) {
            console.warn('[chat] Ошибка ⌘K-поиска:', ex);
            if (requestId === state.globalSearchRequestId) {
                state.globalSearchResults = [];
                state.globalSearchIndex = -1;
                renderGlobalSearchResults([], trimmed);
            }
        }
    }, GLOBAL_SEARCH_DEBOUNCE_MS);
}

/**
 * KI-078B: рендер списка результатов в ⌘K-модалке.
 * @param {Array} items ChatListItemDto с snippet
 * @param {string} query
 */
function renderGlobalSearchResults(items, query) {
    const container = document.getElementById('chat-global-search-results');
    const modal = document.getElementById('chat-global-search');
    if (!container || !modal) return;

    if (!items || items.length === 0) {
        if ((query || '').trim().length === 0) {
            const hint = modal.dataset.labelHint || 'Начните вводить запрос';
            container.innerHTML = `<div class="chat-global-search-hint">${escapeHtml(hint)}</div>`;
        } else {
            const noResults = modal.dataset.labelNoResults || 'Ничего не найдено';
            container.innerHTML = `<div class="chat-global-search-empty">${escapeHtml(noResults)}</div>`;
        }
        return;
    }

    container.innerHTML = items.map((item, idx) => {
        const active = idx === state.globalSearchIndex ? ' chat-global-search-item-active' : '';
        const title = escapeHtml(item.title || 'Без названия');
        const meta = escapeHtml(formatRelativeDate(item.updatedAt));
        const snippetHtml = renderGlobalSearchSnippet(item);

        return `
            <div class="chat-global-search-item${active}"
                 data-chat-id="${item.id}"
                 data-index="${idx}"
                 role="option"
                 tabindex="-1">
                <div class="chat-global-search-item-header">
                    <div class="chat-global-search-item-title">${title}</div>
                    <div class="chat-global-search-item-meta">${meta}</div>
                </div>
                ${snippetHtml}
            </div>`;
    }).join('');

    // Авто-скролл к активному
    scrollGlobalSearchActiveIntoView();
}

/**
 * KI-078B: рендер snippet с подсветкой совпадения (<mark>).
 * @param {object} item
 * @returns {string} HTML или ''
 */
function renderGlobalSearchSnippet(item) {
    if (!item.snippet) return '';

    if (item.snippetMatchStart == null || item.snippetMatchLength == null) {
        return `<div class="chat-global-search-item-snippet">${escapeHtml(item.snippet)}</div>`;
    }

    const before = item.snippet.substring(0, item.snippetMatchStart);
    const match = item.snippet.substring(
        item.snippetMatchStart,
        item.snippetMatchStart + item.snippetMatchLength
    );
    const after = item.snippet.substring(item.snippetMatchStart + item.snippetMatchLength);

    return `<div class="chat-global-search-item-snippet">${
        escapeHtml(before)}<mark>${escapeHtml(match)}</mark>${escapeHtml(after)}</div>`;
}

/**
 * KI-078B: меняет активный результат (↑/↓), циклически.
 * @param {number} delta -1 = вверх, +1 = вниз
 */
function moveGlobalSearchActive(delta) {
    const items = state.globalSearchResults;
    if (!items || items.length === 0) return;

    const n = items.length;
    state.globalSearchIndex = ((state.globalSearchIndex + delta) % n + n) % n;

    const container = document.getElementById('chat-global-search-results');
    if (container) {
        container.querySelectorAll('.chat-global-search-item').forEach((el, idx) => {
            el.classList.toggle('chat-global-search-item-active', idx === state.globalSearchIndex);
        });
    }
    scrollGlobalSearchActiveIntoView();
}

/**
 * KI-078B: скроллит активный результат в видимую часть списка.
 */
function scrollGlobalSearchActiveIntoView() {
    const container = document.getElementById('chat-global-search-results');
    if (!container) return;
    const active = container.querySelector('.chat-global-search-item-active');
    if (active) {
        active.scrollIntoView({ block: 'nearest' });
    }
}

/**
 * KI-078B: открывает выбранный чат и закрывает ⌘K-модалку.
 * @param {number} chatId
 */
async function openChatFromGlobalSearch(chatId) {
    closeGlobalSearch();
    await selectChat(chatId);
}

// ============ v1.13.14 (KI-231): Drag-and-Drop + Paste Image ============

/**
 * KI-231: проверяет, что перетаскиваемый объект — файл(ы).
 * <para>
 * `dataTransfer.types` — массив строк. При drag файлов — `['Files']`.
 * При drag текста / ссылок — `['text/plain']` / `['text/uri-list']` и т.п.
 * Без проверки мы бы перехватывали и текстовый drag.
 * </para>
 * @param {DragEvent} e
 * @returns {boolean}
 */
function hasFiles(e) {
    const dt = e.dataTransfer;
    if (!dt) return false;
    return Array.from(dt.types || []).includes('Files');
}

/**
 * KI-231: инициализирует Drag-and-Drop на `.chat-input-box`.
 * <para>
 * Счётчик `dragenter`/`dragleave` — иначе при перетаскивании над
 * вложенными элементами (textarea, кнопки) `dragleave` на родителе
 * срабатывает раньше `drop` → подсветка мигает.
 * </para>
 */
function initDragAndDrop() {
    const dropZone = document.querySelector('.chat-input-box');
    if (!dropZone) return;

    // KI-231: подсказка из #chat-messages → на .chat-input-box (для CSS attr()).
    const messagesEl = document.getElementById('chat-messages');
    const dropHint = messagesEl?.dataset.labelDropHint || 'Drop files here';
    dropZone.setAttribute('data-drop-hint', dropHint);

    let dragCounter = 0;

    dropZone.addEventListener('dragenter', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        dragCounter++;
        if (dragCounter === 1) dropZone.classList.add('drag-over');
    });

    // dragover ОБЯЗАТЕЛЬНО с preventDefault — иначе браузер откроет файл
    // в новой вкладке (default-behavior).
    dropZone.addEventListener('dragover', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
    });

    dropZone.addEventListener('dragleave', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            dropZone.classList.remove('drag-over');
        }
    });

    dropZone.addEventListener('drop', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        dragCounter = 0;
        dropZone.classList.remove('drag-over');

        if (!state.activeChatId) {
            toast('Сначала выберите чат', 'warning');
            return;
        }

        const files = Array.from(e.dataTransfer.files || []);
        if (files.length === 0) return;

        // Переиспользуем uploadFiles (RAG-индексация, тосты, OCR-polling — уже там).
        uploadFiles(files);
    });
}

/**
 * KI-231: обработчик `paste` на textarea.
 * <para>
 * Если в clipboard есть image (`clipboardData.items` с `kind === 'file'`
 * и `type.startsWith('image/')`) — конвертируем в `File` → `uploadFiles`.
 * `preventDefault()` нужен, чтобы браузер не вставил PNG как inline
 * `<img>` прямо в textarea (ломает отправку).
 * </para>
 * <para>Если clipboard содержит текст — пропускаем без изменений.</para>
 */
function onChatInputPaste(e) {
    const items = e.clipboardData?.items;
    if (!items || !state.activeChatId) return;

    for (const item of items) {
        if (item.kind !== 'file') continue;
        if (!item.type || !item.type.startsWith('image/')) continue;

        // Одно изображение на paste (ChatGPT-style).
        e.preventDefault();

        const blob = item.getAsFile();
        if (!blob) continue;

        const filename = generatePasteFilename(blob.type);
        const file = new File([blob], filename, { type: blob.type });
        uploadFiles([file]);
        return;
    }
    // Не image — стандартное поведение (вставка текста).
}

/**
 * KI-231: имя файла для вставленного из буфера изображения.
 * Формат: `{prefix}-YYYYMMDD-HHMMSS.{ext}`, например
 * `screenshot-20261011-143027.png`. Префикс — из
 * `data-label-paste-image-name` на `#chat-messages` (RULES § 4.17).
 * @param {string} mimeType
 * @returns {string}
 */
function generatePasteFilename(mimeType) {
    const extMap = {
        'image/png': 'png',
        'image/jpeg': 'jpg',
        'image/jpg': 'jpg',
        'image/gif': 'gif',
        'image/webp': 'webp',
        'image/bmp': 'bmp'
    };
    const ext = extMap[(mimeType || '').toLowerCase()] || 'png';

    const container = document.getElementById('chat-messages');
    const prefix = container?.dataset.labelPasteImageName || 'screenshot';

    const now = new Date();
    const pad = (n) => String(n).padStart(2, '0');
    const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`
        + `-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;

    return `${prefix}-${stamp}.${ext}`;
}

// ============ Модели и список чатов ============

async function loadModels() {
    const res = await apiGet('/api/models');
    if (!res.success) return;
    state.models = res.data || [];
    state.defaultModel = state.models.find(m => m.isDefault)?.id
        || state.models[0]?.id
        || null;
}

async function loadChats() {
    const listEl = document.getElementById('chat-list');
    // KI-123: индикатор загрузки (spinner + «Идёт загрузка списка чатов…»).
    // Локализация — из data-label-loading на #chat-list (RULES § 4.17).
    renderChatListLoading(listEl);

    // KI-068: если есть активный поисковый запрос — используем server-side search.
    // Пустой запрос → полный список (поведение как до KI-068).
    const q = (state.searchQuery || '').trim();
    const url = q ? `/api/chats?search=${encodeURIComponent(q)}` : '/api/chats';

    const res = await apiGet(url);
    if (!res.success) {
        toast(q ? 'Ошибка поиска' : 'Не удалось загрузить чаты', 'error');
        return;
    }

    state.chats = res.data || [];
    renderChatList();
}

/**
 * KI-068: debounce для поиска — ждём 300ms после последнего нажатия.
 * Если пользователь продолжает печатать — таймер сбрасывается.
 */
let _searchDebounceTimer = null;

function _scheduleSearch() {
    if (_searchDebounceTimer) {
        clearTimeout(_searchDebounceTimer);
    }
    _searchDebounceTimer = setTimeout(() => {
        _searchDebounceTimer = null;
        loadChats();
    }, 300);
}

/**
 * KI-123: показывает в sidebar индикатор загрузки списка чатов —
 * spinner (Bootstrap `.spinner-border`) + текст.
 *
 * Текст локализуется через `data-label-loading` на `#chat-list`
 * (RULES § 4.17 — не хардкодить JS-строки).
 *
 * @param {HTMLElement} listEl — контейнер `#chat-list`
 */
function renderChatListLoading(listEl) {
    if (!listEl) return;

    const label = listEl.dataset.labelLoading || 'Loading chats…';
    listEl.innerHTML = `
        <div class="chat-list-loading">
            <div class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></div>
            <span>${escapeHtml(label)}</span>
        </div>`;
}

function renderChatList() {
    const listEl = document.getElementById('chat-list');
    if (!listEl) return;

    // KI-069: во время inline-edit НЕ перерисовываем sidebar — иначе потеряем
    // <input> с вводом пользователя. Локальные изменения (например, при
    // сохранении имени) применяются к DOM напрямую через _setItemTitleText.
    if (state.editingChatId) return;

    // KI-068: сервер уже отфильтровал список (title + content). Различаем два случая:
    // — пустой поиск → «Нет чатов. Создайте первый.»
    // — активный поиск без результатов → «Ничего не найдено»
    if (state.chats.length === 0) {
        const hasQuery = (state.searchQuery || '').trim().length > 0;
        const noResults = document.getElementById('chat-search')?.dataset.labelNoResults
            || 'Ничего не найдено';
        const empty = hasQuery ? noResults : 'Нет чатов. Создайте первый.';
        listEl.innerHTML = `<div class="text-muted text-center p-3 small">${escapeHtml(empty)}</div>`;
        return;
    }

    listEl.innerHTML = state.chats.map(chat => {
        const isActive = chat.id === state.activeChatId;
        const title = escapeHtml(chat.title || 'Без названия');
        const meta = formatChatMeta(chat);
        return `
            <div class="chat-list-item ${isActive ? 'active' : ''}"
                 data-chat-id="${chat.id}" role="button" tabindex="0">
                <div class="chat-list-item-body">
                    <div class="chat-list-item-title" title="Двойной клик — переименовать">${title}</div>
                    <div class="chat-list-item-meta">${escapeHtml(meta)}</div>
                </div>
                <button type="button"
                        class="chat-list-item-action chat-list-item-edit"
                        data-edit-id="${chat.id}"
                        title="Переименовать"
                        aria-label="Переименовать">✏️</button>
                <button type="button"
                        class="chat-list-item-action chat-list-item-delete"
                        data-delete-id="${chat.id}"
                        title="Удалить"
                        aria-label="Удалить">🗑</button>
            </div>`;
    }).join('');

    listEl.querySelectorAll('[data-chat-id]').forEach(el => {
        el.addEventListener('click', (e) => {
            // KI-069: игнорируем клики по input (пользователь редактирует название).
            if (e.target.closest('.chat-list-item-edit-input')) return;
            // Игнорируем клики по кнопкам действий
            if (e.target.closest('[data-delete-id]') || e.target.closest('[data-edit-id]')) return;

            const chatId = parseInt(el.dataset.chatId, 10);
            // KI-069: если чат уже активен — не дёргаем selectChat (это перерисует DOM).
            if (chatId === state.activeChatId) return;

            e.preventDefault();
            selectChat(chatId);
        });
    });

    // KI-069: двойной клик на названии → inline-edit (ChatGPT-style)
    listEl.querySelectorAll('.chat-list-item-title').forEach(titleEl => {
        titleEl.addEventListener('dblclick', (e) => {
            e.preventDefault();
            e.stopPropagation();

            const item = titleEl.closest('[data-chat-id]');
            if (!item) return;

            const chatId = parseInt(item.dataset.chatId, 10);
            startInlineEditTitle(chatId);
        });
    });

    listEl.querySelectorAll('[data-delete-id]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            deleteChat(parseInt(btn.dataset.deleteId, 10));
        });
    });

    // KI-069: кнопка ✏️ запускает тот же inline-edit (без prompt)
    listEl.querySelectorAll('[data-edit-id]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            startInlineEditTitle(parseInt(btn.dataset.editId, 10));
        });
    });
}

async function createChat() {
    if (!state.defaultModel) {
        toast('Нет доступных моделей LM Studio', 'error');
        return;
    }

    // KI-069: если идёт inline-edit — сначала сохраняем, иначе потеряем ввод при re-render.
    _flushActiveInlineEdit();

    // KI-072: если активен поиск — сбрасываем его. Иначе:
    //   1) новый чат (не совпадающий с фильтром) попадёт в отфильтрованный sidebar;
    //   2) generateNextChatTitle() увидит только отфильтрованный список и может
    //      сгенерировать дубликат номера (например, «Новый чат 6», когда 4 вне фильтра).
    // Поведение как в ChatGPT: создал чат → чистый полный список.
    if (state.searchQuery) {
        state.searchQuery = '';
        const searchInput = document.getElementById('chat-search');
        if (searchInput) searchInput.value = '';
        // Перезагружаем полный список из БД (без фильтра).
        // После этого state.chats — полный, generateNextChatTitle() корректен.
        await loadChats();
    }

    const btn = document.getElementById('btn-new-chat');
    if (btn) btn.disabled = true;

    try {
        const res = await apiPost('/api/chats', {
            model: state.defaultModel,
            title: generateNextChatTitle(),
        });

        if (!res.success) {
            toast(res.message || 'Ошибка создания чата', 'error');
            return;
        }

        state.chats.unshift(res.data);
        renderChatList();
        toast('Чат создан', 'success');
        await selectChat(res.data.id);
    } finally {
        if (btn) btn.disabled = false;
    }
}

async function selectChat(chatId) {
    // KI-069: если пользователь редактировал название другого чата —
    // принудительно сохраняем (blur → save), чтобы не потерять ввод.
    _flushActiveInlineEdit();

    const res = await apiGet(`/api/chats/${chatId}`);
    if (!res.success) {
        toast(res.message || 'Не удалось загрузить чат', 'error');
        return;
    }

    state.activeChatId = chatId;
    state.activeChat = res.data;
    state.activeChatMessageCount = (res.data.messages || []).length;
    state.autoScroll = true;   // при переключении чата — прилипаем к низу

    // v1.11.0 (KI-126, Шаг 1G.2): сброс кэша дебатов при переключении чата —
    // DOM будет перерисован, старые sessionId неактуальны.
    state.debates = {};
    state.feedbackDrafts = {};
    // v1.11.0 (KI-129): сброс маппинга toolCallId → sessionId.
    state.debateSessionByToolCallId = {};

    // v1.11.0 (KI-129): заполнить state.debates из chatDetail.debateSessions
    // ДО renderMessages — иначе renderMessage не найдёт данные для блоков.
    prefillDebatesFromChatDetail(res.data);

    // KI-078A: при переключении чата — закрыть панель поиска (marks устарели).
    closeChatSearch();

    // v1.13.x (KI-204): останавливаем polling OCR старого чата (если активен).
    stopOcrPolling();

    updateUrl(chatId);
    renderChatList();
    renderChatHeader(res.data);
    renderMessages(res.data.messages || []);

    // KI-083 (Шаг 6D): загрузить вложения активного чата.
    await loadAttachments();

    enableInput(true);

    // KI-062: фокус на input (ChatGPT-style). setTimeout — чтобы фокус
    // не сбрасывался re-render'ом sidebar и других элементов.
    setTimeout(() => document.getElementById('chat-input')?.focus(), 0);

    updateScrollDownButton();
}

async function deleteChat(chatId) {
    if (!confirm('Вы уверены, что хотите удалить чат?')) return;

    // KI-069: если удаляем редактируемый чат — сбрасываем флаг, чтобы
    // следующий renderChatList() отработал (иначе он будет заблокирован).
    if (state.editingChatId === chatId) {
        state.editingChatId = null;
    }

    try {
        const res = await fetch(`/api/chats/${chatId}`, {
            method: 'DELETE', credentials: 'same-origin',
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка удаления чата', 'error');
            return;
        }

        toast('Чат удалён', 'success');
        state.chats = state.chats.filter(c => c.id !== chatId);

        if (state.chats.length > 0) {
            await selectChat(state.chats[0].id);
        } else {
            state.activeChatId = null;
            state.activeChat = null;
            updateUrl(null);
            renderChatList();
            showEmptyState();
        }
    } catch (ex) {
        toast(ex.message || 'Ошибка удаления чата', 'error');
    }
}

// ============ Переименование чата — inline-edit (KI-069) ============

/**
 * Начинает inline-edit названия чата в sidebar (KI-069).
 * Заменяет `.chat-list-item-title` на `<input>` с текущим названием.
 *
 * Поведение:
 *   - Enter  = сохранить (PATCH /api/chats/{id})
 *   - Esc    = отмена (без запроса)
 *   - blur   = сохранить (ChatGPT-style)
 *
 * Если редактируется другой чат — предварительно сохраняет его.
 * Игнорируется во время стрима (правило: не трогать UI во время генерации).
 *
 * @param {number} chatId Идентификатор чата
 */
function startInlineEditTitle(chatId) {
    if (state.isStreaming) return;   // не редактируем во время стрима

    const listEl = document.getElementById('chat-list');
    if (!listEl) return;

    // Если уже редактируется другой чат — сохраняем его.
    if (state.editingChatId && state.editingChatId !== chatId) {
        _flushActiveInlineEdit();
    }

    // Уже редактируем этот же чат — повторный клик игнорируем.
    if (state.editingChatId === chatId) return;

    const item = listEl.querySelector(`[data-chat-id="${chatId}"]`);
    if (!item) return;

    const chat = state.chats.find(c => c.id === chatId);
    if (!chat) return;

    const titleEl = item.querySelector('.chat-list-item-title');
    if (!titleEl) return;

    const currentTitle = chat.title || '';

    // Заменяем <div class="chat-list-item-title"> на <input>
    titleEl.innerHTML = `<input type="text" class="chat-list-item-edit-input" maxlength="200" />`;
    const input = titleEl.querySelector('.chat-list-item-edit-input');
    input.value = currentTitle;

    item.classList.add('chat-list-item-editing');
    state.editingChatId = chatId;

    // Фокус + выделение (правило 4.20: setTimeout для re-render DOM)
    setTimeout(() => {
        input.focus();
        input.select();
    }, 0);

    // Флаг «решение принято» — защита от повторного вызова finish()
    // (например, Enter → finish, потом blur от скрытия input).
    let settled = false;

    const onBlur = () => finish(true);

    /**
     * Завершает inline-edit.
     * @param {boolean} save true — сохранить на сервере, false — отменить
     */
    const finish = async (save) => {
        if (settled) return;
        settled = true;

        // Отписываемся от blur, чтобы повторный blur не сработал.
        input.removeEventListener('blur', onBlur);

        const newTitle = (input.value || '').trim();
        const oldTitle = chat.title || '';

        // Esc / отмена — просто восстанавливаем текст
        if (!save) {
            _setItemTitleText(item, oldTitle);
            return;
        }

        // Пустое имя — предупреждаем и откатываем
        if (!newTitle) {
            toast('Имя не может быть пустым', 'warning');
            _setItemTitleText(item, oldTitle);
            return;
        }

        // Без изменений — просто откат на «красивое» отображение
        if (newTitle === oldTitle) {
            _setItemTitleText(item, oldTitle);
            return;
        }

        // Оптимистично: сначала DOM + state, потом PATCH
        _setItemTitleText(item, newTitle);
        chat.title = newTitle;

        if (state.activeChatId === chatId && state.activeChat) {
            state.activeChat.title = newTitle;
            renderChatHeader(state.activeChat);
        }

        try {
            const res = await fetch(`/api/chats/${chatId}`, {
                method: 'PATCH',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title: newTitle }),
            }).then(r => r.json());

            if (!res.success) {
                toast(res.message || 'Ошибка переименования', 'error');
                _rollbackTitle(chatId, oldTitle, item);
                return;
            }

            toast('Чат переименован', 'success');
        } catch (ex) {
            toast(ex.message || 'Ошибка переименования', 'error');
            _rollbackTitle(chatId, oldTitle, item);
        }
    };

    input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            finish(true);
        } else if (e.key === 'Escape') {
            e.preventDefault();
            finish(false);
        }
    });
    input.addEventListener('blur', onBlur);

    // Клики по input не должны всплывать до item (иначе сработает selectChat).
    input.addEventListener('click', (e) => e.stopPropagation());
    input.addEventListener('dblclick', (e) => e.stopPropagation());
}

/**
 * KI-069: принудительно сохраняет активный inline-edit (если есть).
 * Вызывается перед selectChat / createChat / sendMessage — чтобы не потерять ввод.
 * Использует тот же путь, что и blur: `input.blur()` → `finish(true)`.
 */
function _flushActiveInlineEdit() {
    if (!state.editingChatId) return;

    const listEl = document.getElementById('chat-list');
    const editing = listEl?.querySelector('.chat-list-item-editing');
    const input = editing?.querySelector('.chat-list-item-edit-input');

    if (input) {
        // Синхронный blur → finish(true) → _setItemTitleText → state.editingChatId = null
        input.blur();
    } else {
        // Защита: если input потерялся — сбрасываем флаг принудительно.
        state.editingChatId = null;
    }
}

/**
 * KI-069: заменяет `<input>` на текст названия, снимает флаг редактирования.
 * @param {HTMLElement} item — `.chat-list-item` элемент
 * @param {string} title — актуальное название
 */
function _setItemTitleText(item, title) {
    const titleEl = item.querySelector('.chat-list-item-title');
    if (titleEl) {
        titleEl.innerHTML = escapeHtml(title || 'Без названия');
    }
    item.classList.remove('chat-list-item-editing');

    if (state.editingChatId === parseInt(item.dataset.chatId, 10)) {
        state.editingChatId = null;
    }
}

/**
 * KI-069: откатывает оптимистичное обновление названия при ошибке PATCH.
 * @param {number} chatId
 * @param {string} oldTitle
 * @param {HTMLElement} item
 */
function _rollbackTitle(chatId, oldTitle, item) {
    const chat = state.chats.find(c => c.id === chatId);
    if (chat) chat.title = oldTitle;

    _setItemTitleText(item, oldTitle);

    if (state.activeChatId === chatId && state.activeChat) {
        state.activeChat.title = oldTitle;
        renderChatHeader(state.activeChat);
    }
}

// ============ Авто-нумерация «Новый чат N» (Фаза 2.0.5c) ============

/**
 * Генерирует имя для нового чата по аналогии с ChatGPT:
 * «Новый чат», «Новый чат 2», «Новый чат 3», ...
 * Учитывает существующие чаты (максимальный N + 1).
 * @returns {string} Имя нового чата
 */
function generateNextChatTitle() {
    const re = /^Новый чат(?:\s+(\d+))?$/;
    let hasBase = false;
    let maxN = 1;

    for (const c of state.chats) {
        const m = re.exec(c.title || '');
        if (!m) continue;
        if (m[1]) {
            const n = parseInt(m[1], 10);
            if (Number.isFinite(n) && n > maxN) maxN = n;
        } else {
            hasBase = true;
        }
    }

    // Первый чат — «Новый чат» (без номера)
    if (!hasBase && maxN === 1) return 'Новый чат';
    // Последующие — «Новый чат N»
    return `Новый чат ${maxN + 1}`;
}

// ============ SSE — отправка сообщения ============

/**
 * Отправляет сообщение пользователя и стримит ответ ассистента.
 */
async function sendMessage() {
    if (state.isStreaming) return;
    if (!state.activeChatId) {
        toast('Сначала выберите чат', 'warning');
        return;
    }

    const input = document.getElementById('chat-input');
    const message = (input?.value || '').trim();

    if (!message) {
        toast('Сообщение не может быть пустым', 'warning');
        return;
    }

    // KI-069: если идёт inline-edit — сначала сохраняем название,
    // иначе re-render при обновлении sidebar затрёт ввод.
    _flushActiveInlineEdit();

    // Фаза 2.2.1b: запомнить, был ли чат пустым ДО отправки —
    // от этого зависит, генерировать ли AI-title после done.
    const wasEmpty = state.activeChatMessageCount === 0;

    // 1. Сразу добавляем user-пузырь (оптимистично) и очищаем input
    appendUserMessage(message);
    if (input) { input.value = ''; autoResizeTextarea(input); }
    enableInput(false);
    setStreamingUI(true);
    state.isStreaming = true;

    // 2. Создаём пузырь ассистента с индикатором «Печатает…»
    const assistantBubble = appendAssistantBubble();
    showTypingIndicator(assistantBubble, true);

    // 3. AbortController для Stop
    const controller = new AbortController();
    state.abortController = controller;

    try {
        const response = await fetch('/api/chat/stream', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                chatId: state.activeChatId,
                message,
                useTools: true,
            }),
            signal: controller.signal,
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        await readSseStream(response, assistantBubble);
    } catch (ex) {
        showTypingIndicator(assistantBubble, false);

        if (ex.name === 'AbortError') {
            // Фаза 2.1.3: пользователь нажал Stop — частичный ответ отбрасываем.
            // Удаляем bubble с частичным текстом (согласовано: не сохраняем).
            assistantBubble?.remove();
            // KI-065: показать кнопку «🔄 Повторить» на последнем user-сообщении.
            showRetryOnLastUser();
            // Не показываем error-toast (это ожидаемое поведение).
        } else {
            appendAssistantError(assistantBubble, ex.message || 'Ошибка соединения');
            toast(ex.message || 'Ошибка отправки сообщения', 'error');
        }
    } finally {
        state.isStreaming = false;
        state.abortController = null;
        enableInput(true);
        setStreamingUI(false);
        input?.focus();
        updateSidebarTimeLocally();

        // Фаза 2.2.1b: обновляем счётчик только для успешного стрима
        // (при AbortError assistant-пузырь удаляется — счётчик не меняется).
        const streamSucceeded = assistantBubble?.dataset.streamCompleted === '1';

        if (streamSucceeded) {
            state.activeChatMessageCount += 2;   // user + assistant

            // Если это был первый ответ в пустом чате — фоном генерируем
            // название. Без await — не блокируем UI.
            if (wasEmpty) {
                maybeGenerateTitle();
            }
        }
    }
}

/**
 * Фаза 2.2.1b: фоновый запрос на AI-генерацию названия чата.
 * Вызывается только для первого успешного ответа в пустом чате.
 * При успехе обновляет sidebar и (если чат всё ещё активен) header.
 */
async function maybeGenerateTitle() {
    const chatIdAtRequest = state.activeChatId;
    if (!chatIdAtRequest) return;

    // Пропускаем, если title уже не «Новый чат» / «Новый чат N».
    // Это защита от повторного вызова и от перезаписи ручного имени.
    const chat = state.chats.find(c => c.id === chatIdAtRequest);
    if (!chat) return;
    const currentTitle = chat.title || '';
    if (!/^Новый чат(?:\s+\d+)?$/.test(currentTitle)) return;

    try {
        const res = await fetch(`/api/chats/${chatIdAtRequest}/generate-title`, {
            method: 'POST',
            credentials: 'same-origin',
        }).then(r => r.json());

        if (!res?.success || !res.data?.title) return;

        const newTitle = res.data.title;

        // Обновляем sidebar (вне зависимости от того, где сейчас пользователь).
        const chatInState = state.chats.find(c => c.id === chatIdAtRequest);
        if (chatInState) chatInState.title = newTitle;
        renderChatList();

        // Если активный чат всё ещё тот же — обновляем header.
        if (state.activeChatId === chatIdAtRequest && state.activeChat) {
            state.activeChat.title = newTitle;
            renderChatHeader(state.activeChat);
        }

        // Один информативный лог (используется для диагностики).
        console.log(`[chat] AI-title: "${newTitle}"`);
    } catch (ex) {
        // Фоновая фича — не показываем toast, только warning в консоль.
        console.warn('[chat] Ошибка AI-title:', ex);
    }
}

/**
 * Фаза 2.1.3: прерывает текущий SSE-стрим (Stop).
 * Сервер получит разрыв соединения → CancellationToken отменится.
 */
function stopStreaming() {
    if (!state.abortController) return;
    state.abortController.abort();
    toast('Генерация остановлена', 'info');
}

/**
 * Фаза 2.1.3: переключает UI между «Отправить» и «Остановить».
 * @param {boolean} isStreaming
 */
function setStreamingUI(isStreaming) {
    const btnSend = document.getElementById('btn-send');
    const btnStop = document.getElementById('btn-stop');
    if (btnSend) btnSend.hidden = isStreaming;
    if (btnStop) btnStop.hidden = !isStreaming;

    // Фаза 2.2.4: селектор модели disabled во время стрима
    const modelSelect = document.getElementById('chat-model-select');
    if (modelSelect) modelSelect.disabled = isStreaming;

    // v1.13.0 (KI-140): блокируем 🎤 во время стрима — не записываем,
    // когда генерация активна. Не трогаем disabled, если Speech:Enabled = false.
    const btnSpeech = document.getElementById('btn-speech');
    if (btnSpeech) {
        const speechEnabled = document.getElementById('chat-messages')
            ?.dataset.speechEnabled === 'true';
        if (speechEnabled) btnSpeech.disabled = isStreaming;
    }
}

/**
 * Читает SSE-поток и распределяет события по обработчикам.
 * @param {Response} response
 * @param {HTMLElement} assistantBubble
 */
async function readSseStream(response, assistantBubble) {
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const events = buffer.split('\n\n');
        buffer = events.pop() || '';

        for (const raw of events) {
            if (!raw.trim()) continue;

            const lines = raw.split('\n');
            let name = 'message';
            let data = '';
            for (const line of lines) {
                if (line.startsWith('event:')) name = line.slice(6).trim();
                else if (line.startsWith('data:')) data += line.slice(5).trim();
            }

            if (!data) continue;

            let parsed;
            try { parsed = JSON.parse(data); }
            catch (ex) { console.warn('[chat] Не удалось распарсить SSE:', data); continue; }

            handleSseEvent(name, parsed, assistantBubble);
        }
    }
}

/**
 * Обрабатывает одно SSE-событие.
 * @param {string} name
 * @param {object} data
 * @param {HTMLElement} assistantBubble
 */
function handleSseEvent(name, data, assistantBubble) {
    switch (name) {
        case 'start':
            // Фаза 2.2.6b: привязываем messageId к последнему user-bubble
            // (после optimistic-рендера id ещё не известен).
            // Добавляем кнопку ✏️ в actions, если её нет.
            if (data?.userMessageId) {
                const container = document.getElementById('chat-messages');
                const userBubbles = container?.querySelectorAll('.chat-message.user');
                const lastUser = userBubbles?.[userBubbles.length - 1];
                if (lastUser) {
                    lastUser.dataset.messageId = String(data.userMessageId);

                    // KI-084b: показать токены user-сообщения сразу (без F5).
                    if (data.userTokens != null) {
                        updateBubbleMeta(lastUser, data.userTokens, null);
                    }

                    const actionsEl = lastUser.querySelector('.chat-message-actions');
                    if (actionsEl && !actionsEl.querySelector('[data-action="edit"]')) {
                        actionsEl.insertAdjacentHTML('afterbegin', `
                            <button type="button"
                                    class="chat-message-action"
                                    data-action="edit"
                                    title="Редактировать"
                                    aria-label="Редактировать сообщение">✏️</button>`);
                    }
                }
            }
            break;

        case 'delta':
            showTypingIndicator(assistantBubble, false);
            // KI-230: если идёт Continue-стрим — клеим в append-контейнер
            // внутри СУЩЕСТВУЮЩЕГО bubble (не создаём новый).
            if (state.appendTarget) {
                const t = data.text || '';
                state.appendTarget.appendedRaw += t;
                state.appendTarget.appendEl.innerHTML =
                    escapeHtml(state.appendTarget.appendedRaw).replace(/\n/g, '<br>');
                scrollToBottom();
            } else {
                appendDelta(assistantBubble, data.text || '');
            }
            break;

        case 'tool_call':
            showTypingIndicator(assistantBubble, false);
            appendToolCallBlock(assistantBubble, data);
            showTypingIndicator(assistantBubble, true);
            break;

        case 'tool_result':
            appendToolResultBlock(assistantBubble, data);
            break;

        case 'tool_approval_required':
            handleApprovalRequired(data);
            break;

        case 'tool_approval_resolved':
            handleApprovalResolved(data);
            break;

        // v1.11.0 (KI-126, Шаг 1G.1): события Actor-Critic.
        case 'debate_started':
            handleDebateStarted(assistantBubble, data);
            break;
        case 'debate_round':
            handleDebateRound(assistantBubble, data);
            break;
        case 'debate_completed':
            handleDebateCompleted(assistantBubble, data);
            break;

        case 'done':
            showTypingIndicator(assistantBubble, false);

            // KI-230: если это Continue-стрим — merge appended в основной
            // контент + markdown-перерендер СУЩЕСТВУЮЩЕГО bubble.
            if (state.appendTarget) {
                const t = state.appendTarget;
                const mergedRaw = (t.contentEl.dataset.raw || '') + t.appendedRaw;
                t.contentEl.dataset.raw = mergedRaw;

                // Убираем append-контейнер (временно был внутри content).
                t.appendEl.remove();

                // Markdown полного текста.
                t.contentEl.classList.add('chat-markdown');
                t.contentEl.innerHTML = renderMarkdown(mergedRaw);
                enhanceCodeBlocks(t.bubble);

                // Copy-текст и meta (токены/длительность).
                const actionsEl = t.bubble.querySelector('.chat-message-actions');
                if (actionsEl) actionsEl.dataset.copyText = mergedRaw;
                if (data?.tokensIn != null || data?.tokensOut != null) {
                    updateBubbleMeta(t.bubble, data.tokensIn, data.tokensOut,
                        data.durationMs, data.firstTokenMs);
                }

                // Если снова length — показать плашку снова.
                if (data?.finishReason === 'length') {
                    showLengthWarning(t.bubble);
                }

                t.bubble.dataset.streamCompleted = '1';
                state.appendTarget = null;
                break;
            }

            // Обычный flow (не Continue).
            finalizeAssistantBubble(assistantBubble, data);
            // Фаза 2.2.1b: флаг успешного завершения — для maybeGenerateTitle.
            if (assistantBubble) assistantBubble.dataset.streamCompleted = '1';

            // KI-230: показать плашку + кнопку «Продолжить», если ответ обрезан.
            if (data?.finishReason === 'length' && assistantBubble) {
                showLengthWarning(assistantBubble);
            }

            // KI-084b + KI-084a: токены + tok/s + длительность — сразу, без F5.
            if (assistantBubble && (data?.tokensIn != null || data?.tokensOut != null)) {
                updateBubbleMeta(
                    assistantBubble,
                    data.tokensIn,
                    data.tokensOut,
                    data.durationMs,
                    data.firstTokenMs);
            }
            break;

        case 'error':
            showTypingIndicator(assistantBubble, false);
            appendAssistantError(assistantBubble, data.message || 'Неизвестная ошибка');
            break;

        default:
            console.debug('[chat] Неизвестное SSE-событие:', name, data);
    }
}

// ============ Retry после Stop (KI-065) ============

/**
 * Показывает кнопку «🔄 Повторить» на последнем user-сообщении.
 * Вызывается при Stop (AbortError).
 */
function showRetryOnLastUser() {
    const container = document.getElementById('chat-messages');
    if (!container) return;

    // Убрать со всех (на случай повторного вызова)
    container.querySelectorAll('[data-action="retry"]').forEach(b => b.remove());

    // Найти последний user-bubble
    const userBubbles = container.querySelectorAll('.chat-message.user');
    if (userBubbles.length === 0) return;

    const lastUser = userBubbles[userBubbles.length - 1];
    let actionsEl = lastUser.querySelector('.chat-message-actions');
    if (!actionsEl) {
        // Если контейнера нет (не должно случиться, но подстрахуемся) — создаём
        const bodyEl = lastUser.querySelector('.chat-message-body');
        if (!bodyEl) return;
        bodyEl.insertAdjacentHTML('beforeend', '<div class="chat-message-actions"></div>');
        actionsEl = bodyEl.querySelector('.chat-message-actions');
    }

    if (!actionsEl.querySelector('[data-action="retry"]')) {
        actionsEl.insertAdjacentHTML('beforeend', `
            <button type="button"
                    class="chat-message-action chat-message-action-with-text"
                    data-action="retry"
                    title="Повторить"
                    aria-label="Повторить">🔄 Повторить</button>`);
    }

    actionsEl.classList.add('chat-message-actions-always-visible');
    scrollToBottom();
}

// ============ Regenerate (Фаза 2.1.2.3) ============

/**
 * Перегенерирует последний ответ ассистента в чате.
 * Удаляет последний assistant-пузырь из DOM, отправляет POST /api/chat/regenerate
 * и стримит новый ответ (через существующий readSseStream).
 */
async function regenerateLastMessage(opts = {}) {
    if (state.isStreaming) return;
    if (!state.activeChatId) {
        toast('Сначала выберите чат', 'warning');
        return;
    }

    const container = document.getElementById('chat-messages');
    if (!container) return;

    // Найти последний assistant-пузырь
    const assistantBubbles = container.querySelectorAll('.chat-message.assistant');
    if (assistantBubbles.length > 0) {
        assistantBubbles[assistantBubbles.length - 1].remove();
    } else if (!opts.allowNoAssistant) {
        // Обычный Regenerate требует существующий ответ.
        // Retry (allowNoAssistant) — нет: после Stop ответ удалён.
        toast('Нет ответа для регенерации', 'warning');
        return;
    }

    // KI-065: снять кнопку «Повторить» со всех user-сообщений
    container.querySelectorAll('[data-action="retry"]').forEach(b => b.remove());
    container.querySelectorAll('.chat-message-actions-always-visible')
        .forEach(el => el.classList.remove('chat-message-actions-always-visible'));

    // Заблокировать input
    enableInput(false);
    setStreamingUI(true);
    state.isStreaming = true;

    // Создать пустой bubble с typing indicator
    const newBubble = appendAssistantBubble();
    showTypingIndicator(newBubble, true);

    // AbortController для Stop
    const controller = new AbortController();
    state.abortController = controller;

    try {
        const response = await fetch('/api/chat/regenerate', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ chatId: state.activeChatId }),
            signal: controller.signal,
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        await readSseStream(response, newBubble);
    } catch (ex) {
        showTypingIndicator(newBubble, false);

        if (ex.name === 'AbortError') {
            newBubble?.remove();
            // Частичный regen-ответ отбрасываем.
        } else {
            appendAssistantError(newBubble, ex.message || 'Ошибка регенерации');
            toast(ex.message || 'Ошибка регенерации', 'error');
        }
    } finally {
        state.isStreaming = false;
        state.abortController = null;
        enableInput(true);
        setStreamingUI(false);
        document.getElementById('chat-input')?.focus();
        updateSidebarTimeLocally();
    }
}

// ============ Approvals (Фаза 2.0.4) ============

/**
 * Обрабатывает SSE-событие tool_approval_required.
 * Показывает модалку подтверждения. Модалка сама отправит approve/reject
 * через REST /api/chat/approvals/{callId}/... — сервер продолжит стрим.
 *
 * ВАЖНО: не await — SSE reader продолжает читать события. Разрешение
 * придёт в стрим как tool_approval_resolved (сервер ждёт решение 5 минут).
 *
 * @param {object} data { id, name, arguments, expiresAt }
 */
function handleApprovalRequired(data) {
    const expiresMs = new Date(data.expiresAt).getTime();
    const argsJson = JSON.stringify(data.arguments || {});

    console.log(`[chat] Approval required: ${data.name} (${data.id}), expires ${data.expiresAt}`);

    requestChatApproval(data.id, data.name, argsJson, expiresMs)
        .then(result => {
            console.log(`[chat] Approval resolved: ${data.id} → ${result.decision}`);
        })
        .catch(ex => {
            console.error('[chat] Ошибка approval:', ex);
            toast('Ошибка подтверждения', 'error');
        });
}

/**
 * Обрабатывает SSE-событие tool_approval_resolved.
 * Модалка уже закрылась сама (в requestChatApproval). Здесь только
 * логирование — можно будет использовать для индикатора в UI (v1.3.x).
 *
 * @param {object} data { id, decision }
 */
function handleApprovalResolved(data) {
    console.log(`[chat] Approval resolved event: ${data.id} → ${data.decision}`);
}

// ============ v1.11.0 (KI-126): Рендер дебатов (Actor-Critic) ============

/**
 * v1.11.0 (KI-126, Шаг 1H): читает локализованную строку из data-label-*
 * на #chat-messages.
 * @param {string} key — camelCase ключ (например, 'debateTitle')
 * @param {string} fallback — если нет в data-*
 * @returns {string}
 */
function L(key, fallback) {
    const container = document.getElementById('chat-messages');
    if (!container) return fallback || '';
    return container.dataset[key] || fallback || '';
}

/**
 * v1.11.0 (KI-126, Шаг 1H): подставляет {0}, {1}, ... в шаблон.
 * @param {string} tpl
 * @param  {...any} args
 * @returns {string}
 */
function fmt(tpl, ...args) {
    return String(tpl || '').replace(/\{(\d+)\}/g, (m, i) => {
        const idx = parseInt(i, 10);
        return idx < args.length ? String(args[idx]) : m;
    });
}

/**
 * Обрабатывает SSE-событие `debate_started`.
 * Сохраняет данные в кэш и создаёт контейнер в DOM.
 *
 * @param {HTMLElement} bubble — текущий assistant-пузырь
 * @param {object} data — ChatDebateStartedDto
 */
function handleDebateStarted(bubble, data) {
    if (!bubble || !data?.sessionId) return;

    // Сохраняем данные в кэш для перерендера при смене вида.
    state.debates[data.sessionId] = {
        started: data,
        rounds: [],
        completed: null
    };

    const bodyEl = bubble.querySelector('.chat-message-body');
    if (!bodyEl) return;

    // Проверяем, нет ли уже контейнера для этой сессии
    const existing = bodyEl.querySelector(`[data-debate-session-id="${data.sessionId}"]`);
    if (existing) return;

    const container = document.createElement('div');
    container.className = `chat-debate-block chat-debate-${state.debateView}`;
    container.dataset.debateSessionId = data.sessionId;
    container.innerHTML = renderDebateContainerInner(data.sessionId);

    // v1.11.0 (KI-126, Шаг 1G.1-fix): вставляем в .chat-message-tools,
    // чтобы блок дебатов логически предшествовал финальному тексту ответа.
    const toolsEl = bodyEl.querySelector('.chat-message-tools');
    if (toolsEl) {
        toolsEl.appendChild(container);
    } else {
        // Fallback: если .chat-message-tools нет (не должно случаться),
        // вставляем перед actions или в конец body.
        const actionsEl = bodyEl.querySelector('.chat-message-actions');
        if (actionsEl) {
            bodyEl.insertBefore(container, actionsEl);
        } else {
            bodyEl.appendChild(container);
        }
    }

    scrollToBottom();
}

/**
 * Обрабатывает SSE-событие `debate_round`.
 * Сохраняет раунд в кэш и перерендеривает контейнер.
 *
 * @param {HTMLElement} bubble — текущий assistant-пузырь
 * @param {object} data — ChatDebateRoundDto
 */
function handleDebateRound(bubble, data) {
    if (!bubble || !data?.sessionId) return;

    const entry = state.debates[data.sessionId];
    if (!entry) {
        // Если по какой-то причине не было debate_started — создаём entry.
        // Восстанавливаем sessionId и пустой started (пригодится для рендера).
        console.warn(`[chat] debate_round без debate_started: sessionId=${data.sessionId}`);
        return;
    }

    entry.rounds.push(data);

    // v1.11.0 (KI-126, Шаг 1G.3): после Rejected-раунда — показать inline-блок
    // feedback, если включён HumanApproval = "BetweenRounds".
    // Блок показываем только один раз на сессию (последний Rejected раунд),
    // старые черновики сбрасываем.
    const humanApproval = entry.started?.humanApproval;
    const isRejected = data.criticVerdict === 'Rejected';

    if (isRejected && humanApproval === 'BetweenRounds') {
        state.feedbackDrafts[data.sessionId] = {
            status: 'pending',
            expiresAt: Date.now() + 5 * 60 * 1000,  // 5 минут
            text: ''
        };
    } else if (data.criticVerdict === 'Approved') {
        // Раунд Approved — feedback больше не нужен.
        delete state.feedbackDrafts[data.sessionId];
    }

    // Полный перерендер контейнера.
    const container = bubble.querySelector(`[data-debate-session-id="${data.sessionId}"]`);
    if (container) {
        container.innerHTML = renderDebateContainerInner(data.sessionId);
    }

    scrollToBottom();
}

/**
 * Обрабатывает SSE-событие `debate_completed`.
 * Сохраняет финал в кэш и перерендеривает контейнер.
 *
 * @param {HTMLElement} bubble — текущий assistant-пузырь
 * @param {object} data — ChatDebateCompletedDto
 */
function handleDebateCompleted(bubble, data) {
    if (!bubble || !data?.sessionId) return;

    const entry = state.debates[data.sessionId];
    if (!entry) {
        console.warn(`[chat] debate_completed без debate_started: sessionId=${data.sessionId}`);
        return;
    }

    entry.completed = data;

    // v1.11.0 (KI-126, Шаг 1G.3): feedback больше не нужен — сессия завершена.
    delete state.feedbackDrafts[data.sessionId];

    // Полный перерендер контейнера.
    const container = bubble.querySelector(`[data-debate-session-id="${data.sessionId}"]`);
    if (container) {
        container.innerHTML = renderDebateContainerInner(data.sessionId);
    }

    // Скрываем индикатор «Печатает…», если он ещё виден
    showTypingIndicator(bubble, false);

    scrollToBottom();
}

/**
 * Перерисовывает ВСЕ отображённые блоки дебатов в соответствии с текущим
 * значением `state.debateView`. Вызывается при переключении вида.
 */
function refreshDebateViews() {
    const containers = document.querySelectorAll('[data-debate-session-id]');
    containers.forEach(container => {
        const sessionId = parseInt(container.dataset.debateSessionId, 10);
        if (!Number.isFinite(sessionId)) return;

        // Обновляем класс и полностью перерендериваем содержимое.
        container.classList.toggle('chat-debate-dialog', state.debateView === 'dialog');
        container.classList.toggle('chat-debate-collapsed', state.debateView === 'collapsed');
        container.innerHTML = renderDebateContainerInner(sessionId);
    });
}

// ============ v1.11.0 (KI-129): Восстановление дебатов при F5 ============

/**
 * Заполняет state.debates и state.debateSessionByToolCallId из chatDetail
 * при перезагрузке страницы (F5).
 *
 * <para>
 * Маппинг на формат live-режима:
 * <list type="bullet">
 *   <item>started   ← { sessionId, task, actorAgent, criticAgent, maxRounds,
 *                       humanApproval, status }  (status — для orphaned);</item>
 *   <item>rounds    ← [{ roundNumber, actorOutput, criticVerdict,
 *                       criticFeedback, wasEscalated }]  (feedback → camelCase);</item>
 *   <item>completed ← { verdict, totalRounds, finalArtifact, totalCostUsd }.</item>
 * </list>
 * </para>
 *
 * @param {object} chatDetail — ответ GET /api/chats/{id}
 */
function prefillDebatesFromChatDetail(chatDetail) {
    if (!chatDetail) return;

    // 1. Заполнить state.debates[sid] для каждой сессии.
    const sessions = Array.isArray(chatDetail.debateSessions)
        ? chatDetail.debateSessions
        : [];

    for (const s of sessions) {
        if (!s || !Number.isFinite(s.sessionId)) continue;

        state.debates[s.sessionId] = {
            started: {
                sessionId: s.sessionId,
                task: s.task || '',
                actorAgent: s.actorAgent || 'code_agent',
                criticAgent: s.criticAgent || 'code_reviewer_agent',
                maxRounds: s.maxRounds || 3,
                humanApproval: s.humanApproval || 'BetweenRounds',
                // v1.11.0 (KI-129): статус из БД — нужен для orphaned-детекции.
                status: s.status || null
            },
            rounds: (Array.isArray(s.rounds) ? s.rounds : []).map(r => ({
                roundNumber: r.roundNumber,
                actorOutput: r.actorOutput || '',
                criticVerdict: r.criticVerdict || 'Uncertain',
                // В БД хранится как CriticFeedbackJson (Newtonsoft),
                // в SSE (live) — criticFeedback. Унифицируем на клиенте.
                criticFeedback: r.criticFeedbackJson || null,
                wasEscalated: !!r.wasEscalated
            })),
            completed: s.finalVerdict
                ? {
                    verdict: s.finalVerdict,
                    totalRounds: s.totalRounds || 0,
                    finalArtifact: parseFinalArtifact(s.finalArtifactJson),
                    totalCostUsd: Number(s.totalCostUsd) || 0
                }
                : null
        };
    }

    // 2. Построить маппинг toolCallId → sessionId из tool-сообщений.
    const messages = Array.isArray(chatDetail.messages) ? chatDetail.messages : [];
    for (const m of messages) {
        if (m.role !== 'tool') continue;
        if (m.toolName !== 'code_agent_with_review') continue;
        if (!Number.isFinite(m.debateSessionId)) continue;
        if (!m.toolCallId) continue;
        state.debateSessionByToolCallId[m.toolCallId] = m.debateSessionId;
    }
}

/**
 * Парсит finalArtifactJson из backend DTO: ожидаемый формат
 * { "finalArtifact": "..." }. При ошибке — возвращает исходную строку
 * (лучше показать сырой JSON, чем ничего).
 * @param {string} raw
 * @returns {string}
 */
function parseFinalArtifact(raw) {
    if (!raw) return '';
    try {
        const parsed = JSON.parse(raw);
        return parsed?.finalArtifact || '';
    } catch {
        return raw;
    }
}

/**
 * Возвращает HTML-фрагмент с блоками дебатов для assistant-сообщения.
 * Для каждого tool_call с именем `code_agent_with_review` ищем sessionId
 * через state.debateSessionByToolCallId → state.debates[sid] → рендерим блок.
 *
 * @param {object} msg — ChatMessageDto
 * @returns {string} HTML или '' (если это не assistant / нет tool_calls /
 *                   нет соответствующих сессий)
 */
function renderDebatesForMessage(msg) {
    if (!msg || msg.role !== 'assistant' || !msg.toolCallsJson) return '';

    let calls;
    try { calls = JSON.parse(msg.toolCallsJson); }
    catch { return ''; }
    if (!Array.isArray(calls) || calls.length === 0) return '';

    const sids = new Set();
    for (const call of calls) {
        if (call?.function?.name !== 'code_agent_with_review') continue;
        const callId = call.id;
        if (!callId) continue;
        const sid = state.debateSessionByToolCallId[callId];
        if (Number.isFinite(sid)) sids.add(sid);
    }

    if (sids.size === 0) return '';

    return Array.from(sids).map(sid => {
        const entry = state.debates[sid];
        if (!entry) return '';
        return `
            <div class="chat-debate-block chat-debate-${state.debateView}"
                 data-debate-session-id="${sid}">
                ${renderDebateContainerInner(sid)}
            </div>`;
    }).join('');
}

/**
 * Возвращает HTML-содержимое блока дебатов (без внешнего div-обёртки).
 * В зависимости от `state.debateView` рендерит в режиме «диалог» или «свёрнутый».
 *
 * @param {number} sessionId
 * @returns {string} HTML
 */
function renderDebateContainerInner(sessionId) {
    const entry = state.debates[sessionId];
    if (!entry || !entry.started) return '';

    const started = entry.started;
    const isDialog = state.debateView === 'dialog';

    // v1.11.0 (KI-129): orphaned — сессия восстановлена из БД (F5) в статусе
    // InProgress / Pending без completed. Серверный координатор уже мёртв,
    // feedback-UI показывать нельзя (POST /inject вернёт false).
    //
    // v1.11.0 (KI-134/KI-135): сценарий «F5 посреди feedback» теперь приводит
    // сессию в статус "Cancelled" (backend корректно сохраняет tool-сообщение
    // и завершает сессию). Поэтому эта ветка становится fallback'ом для
    // действительно «зависших» сессий (например, упал процесс приложения).
    const isOrphaned = !entry.completed
        && started.status
        && (started.status === 'InProgress' || started.status === 'Pending');

    // --- Заголовок ---
    const headerHtml = `
        <div class="chat-debate-header">
            <span class="chat-debate-icon">⚔️</span>
            <span class="chat-debate-title">${escapeHtml(L('labelDebateTitle', 'Actor-Critic'))}</span>
            <span class="chat-debate-task" title="${escapeAttr(started.task)}">${escapeHtml(truncateText(started.task, 80))}</span>
            <span class="chat-debate-meta">${escapeHtml(fmt(L('labelDebateMaxRounds', 'Max {0} rounds'), started.maxRounds))}</span>
            ${entry.rounds.length > 0 ? `<span class="chat-debate-rounds-count">${escapeHtml(fmt(L('labelDebateRoundsCount', '{0} rounds'), entry.rounds.length))}</span>` : ''}
        </div>
    `;

    // v1.11.0 (KI-126, Шаг 1G.3): feedback-блок между раундами.
    // v1.11.0 (KI-129): для orphaned-сессий feedback не показываем.
    const feedbackHtml = isOrphaned ? '' : renderFeedbackBlock(sessionId);

    if (isDialog) {
        // --- Диалоговый режим: все раунды + feedback + финал ---
        const roundsHtml = entry.rounds.map(r => renderDebateRoundDialog(r)).join('');
        const completedHtml = entry.completed
            ? renderDebateCompletedDialog(entry.completed)
            : '';

        return `
            ${headerHtml}
            <div class="chat-debate-rounds">${roundsHtml}</div>
            ${feedbackHtml}
            ${completedHtml}
        `;
    } else {
        // --- Свёрнутый режим: финал + <details> со всеми раундами ---
        const completed = entry.completed;
        const verdict = completed?.verdict
            || (isOrphaned ? 'Orphaned' : 'InProgress');
        const verdictClass = verdict.toLowerCase();
        const verdictLabel = completed
            ? getVerdictLabel(verdict)
            : (isOrphaned
                ? L('labelDebateOrphaned', '⚠️ Interrupted')
                : L('labelDebateInProgress', '⏳ In progress…'));

        let costHtml = '';
        if (completed && completed.totalCostUsd > 0) {
            costHtml = `<span class="chat-debate-cost">${escapeHtml(fmt(L('labelDebateTotalCost', 'Total cost: ${0}'), completed.totalCostUsd.toFixed(4)))}</span>`;
        }

        const finalArtifactHtml = completed?.finalArtifact
            ? `<pre class="chat-debate-final-artifact">${escapeHtml(completed.finalArtifact)}</pre>`
            : '';

        const roundsHtml = entry.rounds.map(r => renderDebateRoundDialog(r)).join('');
        const detailsOpenAttr = completed ? '' : ' open';
        const detailsHtml = entry.rounds.length > 0
            ? `
                <details class="chat-debate-details"${detailsOpenAttr}>
                    <summary class="chat-debate-details-summary">
                        ${escapeHtml(renderDetailsLabel(entry.rounds.length, completed))}
                    </summary>
                    <div class="chat-debate-rounds">${roundsHtml}</div>
                </details>
            `
            : '';

        const icon = completed
            ? (verdict === 'Approved' ? '✅' : '⚠️')
            : (isOrphaned ? '⚠️' : '⏳');

        return `
            ${headerHtml}
            <div class="chat-debate-completed chat-debate-completed-${verdictClass}">
                <div class="chat-debate-completed-header">
                    <span class="chat-debate-icon">${icon}</span>
                    <span class="chat-debate-completed-title">${verdictLabel}</span>
                    ${completed ? `<span class="chat-debate-completed-meta">${escapeHtml(fmt(L('labelDebateRoundsCount', '{0} rounds'), completed.totalRounds))}</span>` : ''}
                    ${costHtml}
                </div>
                ${finalArtifactHtml}
                ${feedbackHtml}
                ${detailsHtml}
            </div>
        `;
    }
}

/**
 * Формирует текст для `<summary>` в свёрнутом режиме.
 * @param {number} roundCount
 * @param {object|null} completed
 * @returns {string}
 */
function renderDetailsLabel(roundCount, completed) {
    const count = fmt(L('labelDebateRoundsCount', '{0} rounds'), roundCount);
    if (completed) {
        return fmt(L('labelDebateDetailsShow', '▼ Show details ({0})'), count);
    }
    return fmt(L('labelDebateDetailsHide', '▲ Rounds ({0})'), count);
}

/**
 * Рендерит один раунд в диалоговом стиле (Actor + Critic отдельными блоками).
 * Используется и в `dialog`, и в `collapsed` (внутри <details>) — визуально
 * один и тот же раунд.
 *
 * @param {object} data — ChatDebateRoundDto
 * @returns {string} HTML
 */
function renderDebateRoundDialog(data) {
    const roundNum = data.roundNumber;
    const verdict = data.criticVerdict || 'Unknown';
    const verdictClass = verdict.toLowerCase();
    const verdictLabel = getVerdictLabel(verdict);

    const actorLabel = L('labelDebateActor', 'Actor');
    const criticLabel = L('labelDebateCritic', 'Critic');
    const roundLabel = fmt(L('labelDebateRound', 'Round {0}'), roundNum);
    const escalatedLabel = L('labelDebateEscalated', '🌐 escalated');

    const actorHtml = `
        <div class="chat-debate-actor">
            <div class="chat-debate-round-title">
                <span class="chat-debate-role-icon">🤖</span>
                <span class="chat-debate-role-name">${escapeHtml(actorLabel)}</span>
                <span class="chat-debate-round-num">${escapeHtml(roundLabel)}</span>
            </div>
            <pre class="chat-debate-actor-output">${escapeHtml(data.actorOutput || '')}</pre>
        </div>
    `;

    const criticHtml = `
        <div class="chat-debate-critic">
            <div class="chat-debate-round-title">
                <span class="chat-debate-role-icon">🧐</span>
                <span class="chat-debate-role-name">${escapeHtml(criticLabel)}</span>
                <span class="chat-debate-round-num">${escapeHtml(roundLabel)}</span>
                <span class="chat-debate-verdict chat-debate-verdict-${verdictClass}">${verdictLabel}</span>
                ${data.wasEscalated ? `<span class="chat-debate-escalation-badge">${escapeHtml(escalatedLabel)}</span>` : ''}
            </div>
            <div class="chat-debate-critic-feedback">${renderCriticFeedback(data.criticFeedback)}</div>
        </div>
    `;

    return `<div class="chat-debate-round">${actorHtml}${criticHtml}</div>`;
}

/**
 * Рендерит финальный блок дебатов в диалоговом режиме.
 *
 * @param {object} data — ChatDebateCompletedDto
 * @returns {string} HTML
 */
function renderDebateCompletedDialog(data) {
    const verdict = data.verdict || 'Unknown';
    const verdictLabel = getVerdictLabel(verdict);

    let costHtml = '';
    if (data.totalCostUsd > 0) {
        costHtml = `<span class="chat-debate-cost">${escapeHtml(fmt(L('labelDebateTotalCost', 'Total cost: ${0}'), data.totalCostUsd.toFixed(4)))}</span>`;
    }

    return `
        <div class="chat-debate-completed">
            <div class="chat-debate-completed-header">
                <span class="chat-debate-icon">${verdict === 'Approved' ? '✅' : '⚠️'}</span>
                <span class="chat-debate-completed-title">${verdictLabel}</span>
                <span class="chat-debate-completed-meta">${data.totalRounds} rounds</span>
                ${costHtml}
            </div>
            <pre class="chat-debate-final-artifact">${escapeHtml(data.finalArtifact || '')}</pre>
        </div>
    `;
}

/**
 * Возвращает локализованную метку для вердикта.
 * @param {string} verdict
 * @returns {string}
 */
function getVerdictLabel(verdict) {
    const map = {
        'Approved': 'labelDebateVerdictApproved',
        'Rejected': 'labelDebateVerdictRejected',
        'Uncertain': 'labelDebateVerdictUncertain',
        'MaxRoundsReached': 'labelDebateVerdictMaxRounds',
        'Cancelled': 'labelDebateVerdictCancelled'
    };
    const key = map[verdict];
    return key ? L(key, verdict) : verdict;
}

/**
 * Рендерит feedback критика (JSON или текст).
 * @param {string} feedback
 * @returns {string} HTML
 */
function renderCriticFeedback(feedback) {
    const noFeedback = L('labelDebateNoFeedback', 'No feedback');
    const noIssues = L('labelDebateNoIssues', 'No issues');

    if (!feedback) return `<span class="text-muted">${escapeHtml(noFeedback)}</span>`;

    try {
        const parsed = JSON.parse(feedback);
        if (parsed.issues && Array.isArray(parsed.issues)) {
            const issuesHtml = parsed.issues.map(issue => {
                const severityRaw = issue.severity || 'Minor';
                const severity = severityRaw.toLowerCase();
                const severityClass = `chat-debate-issue-${severity}`;
                const severityLabel = getSeverityLabel(severityRaw);
                return `
                    <div class="chat-debate-issue ${severityClass}">
                        <span class="chat-debate-issue-severity">${escapeHtml(severityLabel)}</span>
                        <span class="chat-debate-issue-desc">${escapeHtml(issue.description || '')}</span>
                        ${issue.suggestion ? `<span class="chat-debate-issue-suggestion">💡 ${escapeHtml(issue.suggestion)}</span>` : ''}
                    </div>
                `;
            }).join('');
            return issuesHtml || `<span class="text-muted">${escapeHtml(noIssues)}</span>`;
        }
        return `<pre>${escapeHtml(JSON.stringify(parsed, null, 2))}</pre>`;
    } catch {
        return `<pre>${escapeHtml(feedback)}</pre>`;
    }
}

/**
 * v1.11.0 (KI-126, Шаг 1H): локализованная метка серьёзности замечания.
 */
function getSeverityLabel(severity) {
    const map = {
        'Critical': 'labelDebateSeverityCritical',
        'Major': 'labelDebateSeverityMajor',
        'Minor': 'labelDebateSeverityMinor'
    };
    const key = map[severity];
    return key ? L(key, severity) : severity;
}

/**
 * Обрезает текст до указанной длины, добавляя многоточие.
 * @param {string} text
 * @param {number} maxLen
 * @returns {string}
 */
function truncateText(text, maxLen) {
    if (!text) return '';
    if (text.length <= maxLen) return text;
    return text.substring(0, maxLen) + '…';
}

// ============ v1.11.0 (KI-126, Шаг 1G.3): Feedback между раундами ============

/**
 * Рендерит блок feedback для указанной сессии.
 * Возвращает '' — если блока нет (нет draft, или он уже отправлен/пропущен).
 *
 * @param {number} sessionId
 * @returns {string} HTML
 */
function renderFeedbackBlock(sessionId) {
    const draft = state.feedbackDrafts[sessionId];
    if (!draft) return '';

    const container = document.getElementById('chat-messages');

    if (draft.status === 'sending') {
        const sending = container?.dataset.labelDebateFeedbackSending || 'Sending…';
        return `
            <div class="chat-debate-feedback chat-debate-feedback-sending">
                <span class="chat-debate-feedback-icon">⏳</span>
                <span>${escapeHtml(sending)}</span>
            </div>`;
    }

    if (draft.status === 'timeout') {
        const timeout = container?.dataset.labelDebateFeedbackTimeout
            || 'Continuing with critic feedback';
        return `
            <div class="chat-debate-feedback chat-debate-feedback-timeout">
                <span class="chat-debate-feedback-icon">⏰</span>
                <span>${escapeHtml(timeout)}</span>
            </div>`;
    }

    if (draft.status === 'sent' || draft.status === 'skipped') {
        return '';   // ничего не показываем
    }

    // status === 'pending'
    const title = container?.dataset.labelDebateFeedbackTitle || 'Feedback';
    const placeholder = container?.dataset.labelDebateFeedbackPlaceholder || 'Describe what to fix…';
    const sendLabel = container?.dataset.labelDebateFeedbackSend || 'Send';
    const skipLabel = container?.dataset.labelDebateFeedbackSkip || 'Skip';

    const remainSec = Math.max(0, Math.floor((draft.expiresAt - Date.now()) / 1000));
    const mm = Math.floor(remainSec / 60);
    const ss = String(remainSec % 60).padStart(2, '0');

    // Восстанавливаем сохранённый текст (при перерендере).
    const textValue = draft.text ? escapeHtml(draft.text) : '';

    return `
        <div class="chat-debate-feedback" data-session-id="${sessionId}">
            <div class="chat-debate-feedback-header">
                <span class="chat-debate-feedback-icon">💬</span>
                <span class="chat-debate-feedback-title">${escapeHtml(title)}</span>
                <span class="chat-debate-feedback-timer" data-feedback-timer>⏱ ${mm}:${ss}</span>
            </div>
            <textarea class="chat-debate-feedback-textarea"
                      placeholder="${escapeAttr(placeholder)}"
                      rows="2">${textValue}</textarea>
            <div class="chat-debate-feedback-actions">
                <button type="button"
                        class="btn btn-sm btn-outline-secondary"
                        data-action="feedback-skip"
                        data-session-id="${sessionId}">${escapeHtml(skipLabel)}</button>
                <button type="button"
                        class="btn btn-sm btn-primary"
                        data-action="feedback-send"
                        data-session-id="${sessionId}">${escapeHtml(sendLabel)}</button>
            </div>
        </div>`;
}

/**
 * Обновляет таймер в конкретном feedback-блоке.
 * @param {number} sessionId
 */
function refreshFeedbackBlock(sessionId) {
    const container = document.querySelector(`[data-debate-session-id="${sessionId}"]`);
    if (!container) return;

    const old = container.querySelector('.chat-debate-feedback');
    if (old) old.remove();

    const html = renderFeedbackBlock(sessionId);
    if (html) {
        container.insertAdjacentHTML('beforeend', html);
        // Восстанавливаем текст из draft (при перерисовке).
        const draft = state.feedbackDrafts[sessionId];
        if (draft?.text) {
            const ta = container.querySelector('.chat-debate-feedback textarea');
            if (ta) ta.value = draft.text;
        }
    }
}

/**
 * Глобальный tick: обновляет все countdown-таймеры + помечает истёкшие.
 * Вызывается раз в секунду через setInterval.
 */
function updateAllFeedbackCountdowns() {
    const now = Date.now();

    for (const [sid, draft] of Object.entries(state.feedbackDrafts)) {
        if (draft.status !== 'pending') continue;

        const remaining = draft.expiresAt - now;

        if (remaining <= 0) {
            draft.status = 'timeout';
            refreshFeedbackBlock(parseInt(sid, 10));
            continue;
        }

        // Обновляем только текст таймера (без перерендера всего блока).
        const container = document.querySelector(`[data-debate-session-id="${sid}"]`);
        const timerEl = container?.querySelector('[data-feedback-timer]');
        if (timerEl) {
            const sec = Math.floor(remaining / 1000);
            const mm = Math.floor(sec / 60);
            const ss = String(sec % 60).padStart(2, '0');
            timerEl.textContent = `⏱ ${mm}:${ss}`;
        }
    }
}

/**
 * Отправляет feedback пользователя через POST /api/chat/debate/{sessionId}/inject.
 * @param {number} sessionId
 */
async function submitFeedback(sessionId) {
    const draft = state.feedbackDrafts[sessionId];
    if (!draft || draft.status !== 'pending') return;

    const container = document.querySelector(`[data-debate-session-id="${sessionId}"]`);
    const textarea = container?.querySelector('.chat-debate-feedback textarea');
    const text = (textarea?.value || draft.text || '').trim();

    if (!text) {
        toast('Введите feedback или нажмите «Пропустить»', 'warning');
        return;
    }

    draft.text = text;
    draft.status = 'sending';
    refreshFeedbackBlock(sessionId);

    try {
        const res = await apiPost(`/api/chat/debate/${sessionId}/inject`, { feedback: text });
        if (res.success) {
            draft.status = 'sent';
        } else {
            toast(res.message || 'Ошибка отправки feedback', 'error');
            draft.status = 'pending';
        }
    } catch (ex) {
        toast(ex.message || 'Ошибка отправки feedback', 'error');
        draft.status = 'pending';
    }

    refreshFeedbackBlock(sessionId);
}

/**
 * v1.11.0 (KI-133): пропускает feedback — POST /api/chat/debate/{id}/skip.
 * Сервер немедленно разблокирует раунд и продолжит с feedback критика,
 * не дожидаясь таймаута 5 минут.
 * @param {number} sessionId
 */
async function skipFeedback(sessionId) {
    const draft = state.feedbackDrafts[sessionId];
    if (!draft || draft.status !== 'pending') return;

    // Показываем индикатор «отправка», как и при submitFeedback.
    draft.status = 'sending';
    refreshFeedbackBlock(sessionId);

    try {
        const res = await apiPost(`/api/chat/debate/${sessionId}/skip`, {});
        // Успех или «уже неактуально» — в обоих случаях блок можно скрыть.
        draft.status = 'skipped';
    } catch (ex) {
        console.warn('[chat] Ошибка skip feedback:', ex);
        // Сеть отвалилась — оставляем локальный skipped, чтобы не висело.
        draft.status = 'skipped';
    }

    refreshFeedbackBlock(sessionId);
}

// ============ Рендер сообщений ============

/**
 * KI-049b/084a/084b: форматирует ТОЛЬКО текстовую часть статистики (без времени).
 * User:      '15 токенов'
 * Assistant: '123 / 45 токенов' + опционально ' · 7.4 tok/s · 9.1 с'
 * @param {boolean} isUser
 * @param {number|null} tokensIn
 * @param {number|null} tokensOut
 * @param {number|null} durationMs
 * @param {number|null} firstTokenMs
 * @returns {string}
 */
function formatTokenMetaText(isUser, tokensIn, tokensOut, durationMs, firstTokenMs) {
    const container = document.getElementById('chat-messages');
    const tin = Number.isFinite(tokensIn) ? tokensIn : null;
    const tout = Number.isFinite(tokensOut) ? tokensOut : null;

    if (isUser) {
        if (tin == null || tin <= 0) return '';
        const tpl = container?.dataset.labelTokensUser || '{0} токенов';
        return tpl.replace('{0}', String(tin));
    }

    const hasIn = tin != null && tin > 0;
    const hasOut = tout != null && tout > 0;
    if (!hasIn && !hasOut) return '';

    const tpl = container?.dataset.labelTokensAssistant || '{0} / {1} токенов';
    const tokensPart = tpl
        .replace('{0}', hasIn ? String(tin) : '—')
        .replace('{1}', hasOut ? String(tout) : '—');

    // KI-084a: сборка tok/s + длительности (только для assistant).
    const stats = [];
    if (Number.isFinite(durationMs) && durationMs > 0) {
        // tok/s — только если есть firstTokenMs и есть время генерации.
        if (Number.isFinite(firstTokenMs) && durationMs > firstTokenMs && hasOut) {
            const genSec = (durationMs - firstTokenMs) / 1000;
            if (genSec > 0.05) {
                const tokPerSec = tout / genSec;
                const tplTok = container?.dataset.labelTokPerSec || '{0} tok/s';
                stats.push(tplTok.replace('{0}', tokPerSec.toFixed(1)));
            }
        }
        const tplDur = container?.dataset.labelDuration || '{0} с';
        stats.push(tplDur.replace('{0}', (durationMs / 1000).toFixed(1)));
    }

    return [tokensPart, ...stats].join(' · ');
}

/**
 * KI-084b/084a: обновляет meta-строку bubble (токены + tok/s + длительность).
 * Используется в live-стриме при SSE-событиях `start` (user) и `done` (assistant).
 * @param {HTMLElement} bubble
 * @param {number|null} tokensIn
 * @param {number|null} tokensOut
 * @param {number|null} [durationMs]
 * @param {number|null} [firstTokenMs]
 */
function updateBubbleMeta(bubble, tokensIn, tokensOut, durationMs, firstTokenMs) {
    if (!bubble) return;

    const metaEl = bubble.querySelector('.chat-message-meta');
    if (!metaEl) return;

    const isUser = bubble.classList.contains('user');
    const roleLabel = bubble.dataset.roleLabel || (isUser ? 'Вы' : 'Ассистент');
    const time = bubble.dataset.time || '';
    const tokenPart = formatTokenMetaText(isUser, tokensIn, tokensOut, durationMs, firstTokenMs);

    metaEl.innerHTML = tokenPart
        ? `${escapeHtml(roleLabel)} · ${escapeHtml(time)} · ${escapeHtml(tokenPart)}`
        : `${escapeHtml(roleLabel)} · ${escapeHtml(time)}`;
}

/**
 * KI-049b/084a: форматирует meta-строку статистики (для renderMessage при F5-загрузке).
 * @param {object} msg — DTO сообщения
 * @param {boolean} isUser — user или assistant
 * @returns {string}
 */
function formatTokenMeta(msg, isUser) {
    return formatTokenMetaText(
        isUser,
        msg.tokensIn,
        msg.tokensOut,
        msg.durationMs,
        msg.firstTokenMs);
}

// ============ v1.13.17 (KI-235): Suggested prompts на пустом чате ============

/**
 * KI-235: рендер карточек suggested-prompts для пустого чата.
 *
 * <para>
 * 6 prompts — хардкод через .resx (data-label-prompt-1..6 на #chat-messages,
 * RULES § 4.17). Клик → подставляет текст в input (не отправляет) + фокус
 * (ChatGPT-style — пользователь может отредактировать перед отправкой).
 * </para>
 *
 * @returns {string} HTML или '' (если prompts не заданы в .resx)
 */
function renderSuggestedPrompts() {
    const container = document.getElementById('chat-messages');
    if (!container) return '';

    const icons = ['📝', '📊', '🧮', '📚', '🔍', '🌐'];
    const items = [];
    for (let i = 1; i <= 6; i++) {
        // KI-235-fix: HTML-спека: `data-label-prompt-1` → `dataset['labelPrompt-1']`
        // (дефис убирается только перед строчной буквой [a-z], не перед цифрой).
        // Поэтому — getAttribute, а не dataset.camelCase.
        const text = container.getAttribute(`data-label-prompt-${i}`);
        if (!text) continue;
        const icon = icons[i - 1] || '💬';
        items.push(`
            <button type="button"
                    class="chat-suggested-prompt-item"
                    data-prompt="${escapeAttr(text)}">
                <span class="chat-suggested-prompt-icon" aria-hidden="true">${icon}</span>
                <span class="chat-suggested-prompt-text">${escapeHtml(text)}</span>
            </button>`);
    }
    if (items.length === 0) return '';

    const title = container.dataset.labelPromptsTitle || 'Попробуйте так:';
    return `
        <div class="chat-suggested-prompts">
            <div class="chat-suggested-prompts-title">${escapeHtml(title)}</div>
            <div class="chat-suggested-prompts-grid">${items.join('')}</div>
        </div>`;
}

/**
 * KI-235: обработчик клика по suggested-prompt.
 * Подставляет текст в input (не отправляет) + фокус + курсор в конец.
 * @param {MouseEvent} e
 */
function onSuggestedPromptClick(e) {
    const btn = e.target.closest('[data-prompt]');
    if (!btn) return;

    e.preventDefault();
    const text = btn.dataset.prompt || '';
    const input = document.getElementById('chat-input');
    if (!input) return;

    input.value = text;
    autoResizeTextarea(input);
    input.focus();
    // Курсор в конец — пользователь может сразу редактировать.
    input.setSelectionRange(text.length, text.length);
}

function renderMessages(messages) {
    const container = document.getElementById('chat-messages');
    if (!container) return;

    if (!messages || messages.length === 0) {
        // KI-235: на пустом чате — suggested prompts (ChatGPT-style).
        const emptyHint = container.dataset.labelEmptyChatHint
            || 'История пуста. Начните диалог.';
        container.innerHTML = `
            <div class="chat-empty-state">
                <div class="text-muted text-center">
                    <img src="/images/logo-icon.svg"
                         alt="IIChatTools"
                         class="chat-empty-logo"
                         aria-hidden="true" />
                    <p class="mb-0">${escapeHtml(emptyHint)}</p>
                    ${renderSuggestedPrompts()}
                </div>
            </div>`;
        return;
    }

    // Определяем индекс последнего assistant — на нём покажем 🔄 Regenerate
    let lastAssistantIndex = -1;
    for (let i = messages.length - 1; i >= 0; i--) {
        if (messages[i].role === 'assistant') {
            lastAssistantIndex = i;
            break;
        }
    }

    // KI-065: если последнее сообщение — user (после Stop / прерывания),
    // на нём покажем «🔄 Повторить».
    const lastIndex = messages.length - 1;
    const lastIsUser = messages[lastIndex]?.role === 'user';

    container.innerHTML = messages
        .map((msg, i) => renderMessage(msg, {
            showRegenerate: i === lastAssistantIndex,
            showRetry: i === lastIndex && lastIsUser,
        }))
        .join('');
    enhanceCodeBlocks(container);
    scrollToBottom(true);
}

function renderMessage(msg, opts = {}) {
    if (msg.role === 'system') return '';
    if (msg.role === 'tool') return renderToolResultBlock(msg);

    const isUser = msg.role === 'user';
    const avatar = isUser ? '👤' : '🤖';
    const roleLabel = isUser ? 'Вы' : 'Ассистент';

    // KI-049b: токены в meta-строке (опционально).
    const tokenMeta = formatTokenMeta(msg, isUser);
    const tokenMetaHtml = tokenMeta ? ` · ${escapeHtml(tokenMeta)}` : '';

    let toolCallsHtml = '';
    if (msg.toolCallsJson) {
        toolCallsHtml = renderToolCallsFromJson(msg.toolCallsJson);
    }

    // v1.11.0 (KI-129): блоки дебатов для assistant-сообщений —
    // восстанавливаются при F5 через state.debates + маппинг toolCallId.
    const debatesHtml = renderDebatesForMessage(msg);

    // User-сообщения — plain text (пользователь пишет текст, а не Markdown).
    // Assistant — полноценный Markdown-рендер.
    const contentHtml = msg.content
        ? (isUser
            ? `<div class="chat-message-content">${renderUserContent(msg.content)}</div>`
            : `<div class="chat-message-content chat-markdown">${renderMarkdown(msg.content)}</div>`)
        : '';

    // v1.6.0 (KI-086): блок «Источники» — только для assistant-сообщений.
    // При F5 данные приходят из ChatMessageDto.Sources (структурный массив).
    const sourcesHtml = (!isUser && Array.isArray(msg.sources) && msg.sources.length > 0)
        ? renderSourcesBlock(msg.sources)
        : '';

    // KI-230: плашка + кнопка «Продолжить» — если ответ был обрезан.
    // Показываем ТОЛЬКО на последнем assistant-сообщении (продолжать
    // не-последнее — UX-ломающее; пользователь должен дойти до конца).
    const isLastAssistant = opts.showRegenerate === true && !isUser;
    const lengthWarningHtml = (isLastAssistant
        && msg.finishReason === 'length'
        && msg.content)
        ? renderLengthWarningHtml(msg.id)
        : '';

    // Actions:
    //  - Copy      — если есть текст
    //  - Edit ✏️    — на user-сообщениях (Фаза 2.2.6b)
    //  - Regenerate 🔄 — на последнем assistant
    //  - Retry 🔄 Повторить — на последнем user, если после него нет assistant (Stop/отмена)
    const hasActions = !!msg.content || (opts.showRetry && isUser);
    const actionsHtml = hasActions
        ? renderMessageActions(msg.content || '', {
            showEdit: isUser && !!msg.id,
            showRegenerate: opts.showRegenerate && !isUser,
            showRetry: opts.showRetry && isUser,
            // KI-233: 👍/👎 — только для assistant, только если у сообщения есть id.
            showFeedback: !isUser && !!msg.id,
            feedback: msg.feedback || null,
        })
        : '';

    // v1.11.0 (KI-129): контейнер .chat-message-tools — для консистентности
    // с live-режимом (в appendAssistantBubble он создаётся заранее).
    const toolsContent = (toolCallsHtml || debatesHtml)
        ? `<div class="chat-message-tools">${toolCallsHtml}${debatesHtml}</div>`
        : '';

    return `
        <div class="chat-message ${isUser ? 'user' : 'assistant'}" data-message-id="${msg.id}">
            <div class="chat-message-avatar">${avatar}</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">${roleLabel} · ${escapeHtml(formatTime(msg.createdAt))}${tokenMetaHtml}</div>
                ${toolsContent}
                ${contentHtml}
                ${lengthWarningHtml}
                ${sourcesHtml}
                ${actionsHtml}
            </div>
        </div>`;
}

// ============ v1.13.13 (KI-230): Continue (продолжение обрезанного ответа) ============

/**
 * KI-230: возвращает HTML плашки + кнопки «Продолжить» для ответа,
 * обрезанного по лимиту контекста (finishReason = "length").
 *
 * <para>
 * Используется в live-режиме (после `done` SSE-события) и при F5-загрузке
 * (renderMessage) — единый вид.
 * </para>
 *
 * @param {number|string|null} assistantMessageId — id сообщения (для POST /api/chat/continue).
 *   Может быть null в live-режиме: там id берётся из bubble.dataset.assistantMessageId
 *   после finalizeAssistantBubble.
 * @returns {string} HTML
 */
function renderLengthWarningHtml(assistantMessageId) {
    const container = document.getElementById('chat-messages');
    const warningText = container?.dataset.labelLengthWarning || '⚠️ Ответ обрезан из-за лимита контекста.';
    const continueLabel = container?.dataset.labelContinue || '➕ Продолжить';
    const continueTooltip = container?.dataset.labelContinueTooltip || 'Продолжить с того места, где оборвалось';

    const idAttr = (assistantMessageId != null)
        ? ` data-assistant-message-id="${assistantMessageId}"`
        : '';

    return `
        <div class="chat-message-length-warning">
            <span class="chat-message-length-warning-icon">⚠️</span>
            <span class="chat-message-length-warning-text">${escapeHtml(warningText)}</span>
            <button type="button"
                    class="chat-message-action chat-message-action-with-text chat-message-continue-btn"
                    data-action="continue"${idAttr}
                    title="${escapeAttr(continueTooltip)}"
                    aria-label="${escapeAttr(continueLabel)}">${escapeHtml(continueLabel)}</button>
        </div>`;
}

/**
 * KI-230: показывает плашку + кнопку «Продолжить» на bubble (live-режим,
 * после `done` с finishReason=length). Идемпотентно — повторный вызов
 * не дублирует плашку.
 *
 * @param {HTMLElement} bubble
 */
function showLengthWarning(bubble) {
    if (!bubble) return;
    if (bubble.querySelector('.chat-message-length-warning')) return;

    const bodyEl = bubble.querySelector('.chat-message-body');
    if (!bodyEl) return;

    // Вставляем ПЕРЕД actions (или в конец body).
    const html = renderLengthWarningHtml(null);
    const actionsEl = bodyEl.querySelector('.chat-message-actions');
    if (actionsEl) {
        actionsEl.insertAdjacentHTML('beforebegin', html);
    } else {
        bodyEl.insertAdjacentHTML('beforeend', html);
    }
}

/**
 * KI-230: продолжение обрезанного ответа (ChatGPT-style).
 *
 * <para>
 * Стрим идёт в СУЩЕСТВУЮЩИЙ assistant-bubble:
 * <list type="number">
 *   <item>Создаём <c>&lt;span class="chat-message-content-append"&gt;</c> внутри
 *     <c>.chat-message-content</c> — для потоковой доклейки текста.</item>
 *   <item>SSE-событие <c>delta</c> доклеивает в этот span
 *     (см. handleSseEvent case 'delta').</item>
 *   <item>По <c>done</c> — merge appended в основной контент + полный
 *     markdown-перерендер.</item>
 * </list>
 * </para>
 *
 * @param {number} assistantMessageId
 * @param {HTMLElement} bubble — существующий `.chat-message.assistant`
 */
async function continueLastMessage(assistantMessageId, bubble) {
    if (state.isStreaming) return;
    if (!state.activeChatId || !assistantMessageId || !bubble) {
        toast('Не удалось продолжить: сообщение не найдено', 'error');
        return;
    }

    // Скрываем плашку + кнопку, пока идёт стрим (пересоздадим после, если снова length).
    bubble.querySelector('.chat-message-length-warning')?.remove();

    const contentEl = bubble.querySelector('.chat-message-content');
    if (!contentEl) return;

    // Сохраняем текущий raw-текст и переводим в plain для стрима.
    const currentRaw = contentEl.dataset.raw || contentEl.textContent || '';
    contentEl.classList.remove('chat-markdown');
    contentEl.dataset.raw = currentRaw;
    contentEl.innerHTML = escapeHtml(currentRaw).replace(/\n/g, '<br>');

    // Append-контейнер (курсор появится из CSS `::after`).
    const appendEl = document.createElement('span');
    appendEl.className = 'chat-message-content-append';
    appendEl.dataset.appendContent = '1';
    contentEl.appendChild(appendEl);

    state.appendTarget = {
        bubble,
        contentEl,
        appendEl,
        assistantMessageId,
        appendedRaw: '',
    };

    // UI: блокируем input, показываем stop.
    enableInput(false);
    setStreamingUI(true);
    state.isStreaming = true;

    const controller = new AbortController();
    state.abortController = controller;

    try {
        const response = await fetch('/api/chat/continue', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                chatId: state.activeChatId,
                assistantMessageId,
            }),
            signal: controller.signal,
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        // События идут в state.appendTarget (см. handleSseEvent).
        await readSseStream(response, bubble);
    } catch (ex) {
        // Восстанавливаем markdown исходного текста (без продолжения).
        contentEl.classList.add('chat-markdown');
        contentEl.innerHTML = renderMarkdown(currentRaw);
        enhanceCodeBlocks(bubble);

        if (ex.name !== 'AbortError') {
            toast(ex.message || 'Ошибка продолжения', 'error');
        }
    } finally {
        state.isStreaming = false;
        state.abortController = null;
        state.appendTarget = null;
        enableInput(true);
        setStreamingUI(false);
        document.getElementById('chat-input')?.focus();
    }
}

function renderToolCallsFromJson(toolCallsJson) {
    let calls = [];
    try { calls = JSON.parse(toolCallsJson); } catch { return ''; }
    if (!Array.isArray(calls) || calls.length === 0) return '';

    return calls.map(call => {
        const name = call?.function?.name || 'unknown';
        return `
            <div class="chat-tool-block">
                <span class="chat-tool-icon">${getToolIcon(name)}</span>
                <span class="chat-tool-name">${escapeHtml(name)}</span>
            </div>`;
    }).join('');
}

// ============ KI-228: Рендер tool-блока как карточки ============

/**
 * KI-228: эмодзи-иконка по имени инструмента.
 * @param {string} toolName
 * @returns {string}
 */
function getToolIcon(toolName) {
    const map = {
        // Агенты (SubAgents)
        browser_agent: '🌐',
        vision_agent: '👁',
        code_agent: '💻',
        code_agent_with_review: '⚔',
        code_reviewer_agent: '🧐',
        mail_agent: '✉️',
        external_llm_agent: '🌍',
        database_agent: '🗄',
        file_system_agent: '📁',
        web_agent: '🔍',
        git_agent: '🔀',
        github_agent: '🐙',
        planner_agent: '📋',
        consult_secondary_agent: '🛠',
        // RAG
        search_knowledge_base: '📚',
        search_chat_history: '📚',
        search_workspace: '📚',
        // Raw tools (для прямых вызовов и старого /test-стиля)
        list_directory: '📁',
        read_file: '📄',
        save_file: '💾',
        run_python: '🐍',
        run_javascript: '📜',
        execute_command: '⚙',
        web_search: '🔍',
        wikipedia_search: '📖',
        fetch_web_content: '🌐',
        git_status: '🔀',
        git_commit: '💾',
        git_push: '⬆',
        gh_create_issue: '🐙'
    };
    return map[toolName] || '🛠';
}

/**
 * KI-228: извлекает расширенный summary из data tool'а.
 * Приоритет: finalAnswer (SubAgent) → description (vision) → output (code)
 *            → summary → message → error.
 * @param {string} toolName
 * @param {object|null} content — ToolResult.Data
 * @returns {string|null}
 */
function extractToolSummary(toolName, content) {
    if (!content || typeof content !== 'object') return null;

    if (typeof content.finalAnswer === 'string' && content.finalAnswer.trim()) {
        return content.finalAnswer;
    }
    if (typeof content.description === 'string' && content.description.trim()) {
        return content.description;
    }
    if (typeof content.output === 'string' && content.output.trim()) {
        return content.output;
    }
    if (typeof content.summary === 'string' && content.summary.trim()) {
        return content.summary;
    }
    if (typeof content.message === 'string' && content.message.trim()) {
        return content.message;
    }
    if (typeof content.error === 'string' && content.error.trim()) {
        return `Ошибка: ${content.error}`;
    }
    return null;
}

/**
 * KI-228: единый рендер tool-результата — свёрнутая карточка <details>.
 *
 * <para>
 * Структура:
 * <list type="bullet">
 *   <item><b>Header (всегда видно):</b> иконка + имя tool + короткий message + статус ✅/❌.</item>
 *   <item><b>Body (по клику):</b> расширенный summary (finalAnswer / description / output),
 *     inline-PNG (если есть base64 — KI-175), вложенный <c>&lt;details&gt;</c> с полным JSON.</item>
 * </list>
 * </para>
 *
 * <para>
 * Карточка <b>раскрыта по умолчанию</b>, если tool вернул PNG-скриншот
 * (это и есть результат действия — скрывать его в свёрнутом виде бессмысленно).
 * </para>
 *
 * @param {object} opts
 * @param {string} opts.toolName
 * @param {boolean} opts.success
 * @param {string} [opts.message] — короткое сообщение из ToolResult.Message
 * @param {object|null} [opts.content] — ToolResult.Data (для JSON + summary + image)
 * @returns {string} HTML карточки
 */
function renderToolResultCard(opts) {
    const toolName = opts.toolName || 'tool';
    const success = opts.success !== false;
    const message = opts.message || '';
    const content = opts.content || null;

    const icon = getToolIcon(toolName);
    const status = success ? '✅' : '❌';

    // Расширенный summary — в раскрытую часть.
    const extended = extractToolSummary(toolName, content);

    // Короткая строка в шапке.
    const shortMsg = message.length > 80
        ? message.substring(0, 80) + '…'
        : message;

    // Inline PNG (KI-175): рендерим ВНУТРИ тела карточки.
    const imageHtml = renderToolResultImage(success, toolName, content);
    const hasImage = !!imageHtml;

    // Полный JSON — во вложенном <details>, свёрнут.
    let jsonHtml = '';
    if (content && typeof content === 'object') {
        try {
            let json = JSON.stringify(content, null, 2);
            if (json.length > 8000) {
                json = json.substring(0, 8000) + '\n… (обрезано)';
            }
            jsonHtml = `
                <details class="chat-tool-card-json">
                    <summary>JSON</summary>
                    <pre>${escapeHtml(json)}</pre>
                </details>`;
        } catch { /* circular — пропускаем */ }
    }

    // Тело карточки.
    let bodyHtml = '';
    if (extended) {
        bodyHtml += `<div class="chat-tool-card-extended">${escapeHtml(extended)}</div>`;
    }
    bodyHtml += imageHtml;
    bodyHtml += jsonHtml;

    if (!bodyHtml) {
        // Нечего показывать в развёрнутом виде — не даём кликать в пустоту.
        bodyHtml = `<div class="chat-tool-card-empty">—</div>`;
    }

    const openAttr = hasImage ? ' open' : '';

    return `
        <details class="chat-tool-card ${success ? 'success' : 'error'}"${openAttr}>
            <summary class="chat-tool-card-header">
                <span class="chat-tool-card-icon">${icon}</span>
                <span class="chat-tool-card-name">${escapeHtml(toolName)}</span>
                <span class="chat-tool-card-summary">${escapeHtml(shortMsg)}</span>
                <span class="chat-tool-card-status">${status}</span>
            </summary>
            <div class="chat-tool-card-body">${bodyHtml}</div>
        </details>`;
}

/**
 * KI-228: рендер tool-сообщения из истории (F5).
 * @param {object} msg — ChatMessageDto (role='tool')
 * @returns {string} HTML карточки
 */
function renderToolResultBlock(msg) {
    const toolName = msg.toolName || 'tool';

    let parsed = null;
    if (msg.content) {
        try { parsed = JSON.parse(msg.content); } catch { /* не JSON */ }
    }

    // Если content не парсится — используем как plain message (fallback).
    const success = parsed ? parsed.success !== false : true;
    const message = parsed?.message || (parsed ? '' : (msg.content || ''));
    const content = parsed?.data ?? null;

    return renderToolResultCard({ toolName, success, message, content });
}

// ============ Live-рендер (во время стрима) ============

/**
 * Добавляет user-пузырь в ленту.
 * @param {string} text
 */
function appendUserMessage(text) {
    const container = document.getElementById('chat-messages');
    if (!container) return;

    // Убираем пустое состояние, если было
    container.querySelector('.chat-empty-state')?.remove();

    const now = new Date().toISOString();
    const time = formatTime(now);
    const html = `
        <div class="chat-message user">
            <div class="chat-message-avatar">👤</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">Вы · ${escapeHtml(time)}</div>
                <div class="chat-message-content">${renderUserContent(text)}</div>
                ${renderMessageActions(text)}
            </div>
        </div>`;
    container.insertAdjacentHTML('beforeend', html);

    // KI-084b: сохраняем время и роль для обновления meta (токены).
    const lastBubble = container.lastElementChild;
    if (lastBubble) {
        lastBubble.dataset.time = time;
        lastBubble.dataset.roleLabel = 'Вы';
    }

    // enhanceCodeBlocks для user не нужен — там plain text без <pre><code>
    scrollToBottom();
}

/**
 * Создаёт пустой пузырь ассистента для потокового заполнения.
 * @returns {HTMLElement}
 */
function appendAssistantBubble() {
    const container = document.getElementById('chat-messages');
    if (!container) return null;

    container.querySelector('.chat-empty-state')?.remove();

    const now = new Date().toISOString();
    const time = formatTime(now);
    const html = `
        <div class="chat-message assistant chat-message-streaming">
            <div class="chat-message-avatar">🤖</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">Ассистент · ${escapeHtml(time)}</div>
                <div class="chat-message-tools"></div>
                <div class="chat-message-content" data-stream-content></div>
                <div class="chat-typing-indicator" data-typing hidden>
                    <span></span><span></span><span></span>
                </div>
                ${renderMessageActions('')}
            </div>
        </div>`;

    container.insertAdjacentHTML('beforeend', html);

    // KI-084b: сохраняем время и роль для обновления meta (токены).
    const bubble = container.lastElementChild;
    if (bubble) {
        bubble.dataset.time = time;
        bubble.dataset.roleLabel = 'Ассистент';
    }

    scrollToBottom();
    return bubble;
}

/**
 * Показывает/скрывает индикатор «Печатает…».
 * @param {HTMLElement} bubble
 * @param {boolean} show
 */
function showTypingIndicator(bubble, show) {
    if (!bubble) return;
    const el = bubble.querySelector('[data-typing]');
    if (el) el.hidden = !show;
    if (show) scrollToBottom();
}

/**
 * Добавляет delta-текст в пузырь ассистента.
 * @param {HTMLElement} bubble
 * @param {string} text
 */
function appendDelta(bubble, text) {
    if (!bubble || !text) return;
    const el = bubble.querySelector('[data-stream-content]');
    if (!el) return;

    // Накопление текста + сохранение в dataset для финализации
    el.dataset.raw = (el.dataset.raw || '') + text;
    el.innerHTML = escapeHtml(el.dataset.raw).replace(/\n/g, '<br>');

    // Синхронизация текста для Copy (кнопка может быть нажата во время стрима)
    const actionsEl = bubble.querySelector('.chat-message-actions');
    if (actionsEl) {
        actionsEl.dataset.copyText = el.dataset.raw;
    }

    scrollToBottom();
}

/**
 * Добавляет блок tool_call в пузырь ассистента.
 * @param {HTMLElement} bubble
 * @param {object} call
 */
function appendToolCallBlock(bubble, call) {
    if (!bubble) return;
    const toolsEl = bubble.querySelector('.chat-message-tools');
    if (!toolsEl) return;

    toolsEl.insertAdjacentHTML('beforeend', `
        <div class="chat-tool-block">
            <span class="chat-tool-icon">${getToolIcon(call.name || 'unknown')}</span>
            <span class="chat-tool-name">${escapeHtml(call.name || 'unknown')}</span>
        </div>`);
    scrollToBottom();
}

/**
 * Добавляет блок tool_result в пузырь ассистента.
 * @param {HTMLElement} bubble
 * @param {object} result
 */
function appendToolResultBlock(bubble, result) {
    if (!bubble) return;
    const toolsEl = bubble.querySelector('.chat-message-tools');
    if (!toolsEl) return;

    // KI-228: единый рендер — карточка <details> (иконка + имя + summary +
    // status badge; внутри — расширенный summary, PNG-скриншот (KI-175)
    // и полный JSON в вложенном <details>).
    // ChatToolResultDto не меняется: source = SSE-событие tool_result.
    toolsEl.insertAdjacentHTML('beforeend', renderToolResultCard({
        toolName: result.name || 'tool',
        success: result.success !== false,
        message: result.message || '',
        content: result.content || null
    }));
    scrollToBottom();
}

/**
 * KI-175: рендерит <img> из base64-ответа tool'а (vision_agent screenshot).
 * Возвращает HTML или '' (если это не PNG-screenshot).
 *
 * @param {boolean} ok — успех tool'а
 * @param {string} toolName — имя tool'а
 * @param {object} content — result.content (ToolResult.Data)
 * @returns {string} HTML
 */
function renderToolResultImage(ok, toolName, content) {
    if (!ok || !content) return '';

    // KI-229: два независимых сценария.
    //  (A) base64 есть (live SSE) → inline <img> с data-URL + клик → lightbox.
    //  (B) base64 нет (F5 — KI-176) и path = screenshots/{taskId}/{file}
    //      → placeholder + кнопка «Открыть» → lightbox с URL-src.
    //
    // Другие tool'ы могут иметь base64/path с другой семантикой — не трогаем.

    const mime = content.mimeType || 'image/png';
    const sizeBytes = content.sizeBytes || 0;
    const path = content.path || '';
    const MAX_INLINE_BYTES = 2 * 1024 * 1024;

    // (A) Live: base64 в SSE.
    if (content.base64 && typeof content.base64 === 'string') {
        if (sizeBytes > MAX_INLINE_BYTES) {
            return `
                <div class="chat-tool-result-image-meta">
                    📎 <code>${escapeHtml(path)}</code> (${escapeHtml(formatFileSize(sizeBytes))})
                    — файл слишком большой для инлайн-просмотра.
                </div>`;
        }

        const safeMime = escapeAttr(mime);
        const safeAlt = escapeAttr(`Screenshot от ${toolName || 'tool'}`);
        const dataUrl = `data:${safeMime};base64,${content.base64}`;

        return `
            <div class="chat-tool-result-image">
                <img src="${dataUrl}"
                     alt="${safeAlt}"
                     data-lightbox-src="${escapeAttr(dataUrl)}"
                     data-lightbox-title="${escapeAttr(path || 'Screenshot')}"
                     loading="lazy" />
                <div class="chat-tool-result-image-meta">
                    ${escapeHtml(formatFileSize(sizeBytes))}
                    ${path ? ` · <code>${escapeHtml(path)}</code>` : ''}
                </div>
            </div>`;
    }

    // (B) F5: path = screenshots/{taskId}/{file} — placeholder + Open.
    const pathMatch = /^screenshots\/([^/]+)\/([^/]+)$/.exec(path);
    if (pathMatch) {
        const taskId = pathMatch[1];
        const fileName = pathMatch[2];
        const openLabel = L('labelLightboxOpenScreenshot', 'Open screenshot');

        return `
            <div class="chat-tool-result-image-placeholder">
                <span class="chat-tool-result-image-placeholder-icon">👁</span>
                <span class="chat-tool-result-image-placeholder-text">
                    <code>${escapeHtml(path)}</code>
                    <span class="chat-tool-result-image-placeholder-size">${escapeHtml(formatFileSize(sizeBytes))}</span>
                </span>
                <button type="button"
                        class="chat-tool-result-image-placeholder-btn"
                        data-open-screenshot="1"
                        data-task-id="${escapeAttr(taskId)}"
                        data-file-name="${escapeAttr(fileName)}"
                        title="${escapeAttr(openLabel)}"
                        aria-label="${escapeAttr(openLabel)}">${escapeHtml(openLabel)}</button>
            </div>`;
    }

    return '';
}

/**
 * Финализирует пузырь ассистента после события done.
 * @param {HTMLElement} bubble
 * @param {object} data
 */
function finalizeAssistantBubble(bubble, data) {
    if (!bubble) return;
    bubble.classList.remove('chat-message-streaming');
    bubble.dataset.assistantMessageId = data.assistantMessageId || '';

    // Финальный Markdown-рендер: во время стрима — plain-text (без мигания),
    // после done — полноценный Markdown + code blocks.
    const contentEl = bubble.querySelector('[data-stream-content]');
    if (contentEl) {
        const raw = contentEl.dataset.raw || '';
        if (raw) {
            contentEl.classList.add('chat-markdown');
            contentEl.innerHTML = renderMarkdown(raw);
            enhanceCodeBlocks(bubble);

            // KI-066: пересоздаём actions с финальным текстом.
            // appendAssistantBubble() вызывал renderMessageActions('') — кнопка Copy
            // не создавалась (пустой текст). Здесь создаём её с реальным содержимым.
            const bodyEl = bubble.querySelector('.chat-message-body');
            if (bodyEl) {
                const oldActions = bodyEl.querySelector('.chat-message-actions');
                if (oldActions) oldActions.remove();

                // v1.6.0 (KI-086): блок «Источники» — между ответом и actions.
                // Источники приходят в SSE-событии `done` (параметр data.sources).
                if (data?.sources && data.sources.length > 0) {
                    bodyEl.insertAdjacentHTML('beforeend', renderSourcesBlock(data.sources));
                }

                // KI-233: показываем 👍/👎 на финальном assistant-сообщении.
                // id сообщения известен — data.assistantMessageId (id проставляется
                // в bubble.dataset.assistantMessageId выше).
                bodyEl.insertAdjacentHTML('beforeend', renderMessageActions(raw, {
                    showFeedback: true,
                    feedback: null,   // свежее сообщение — оценок нет
                }));
            }
        }
    }

    // Кнопка 🔄 Regenerate: только на последнем assistant.
    // Снимаем её со всех существующих bubble, добавляем на текущий.
    const container = document.getElementById('chat-messages');
    if (container) {
        container.querySelectorAll('.chat-message-actions [data-action="regenerate"]')
            .forEach(btn => btn.remove());
    }
    const currentActions = bubble.querySelector('.chat-message-actions');
    if (currentActions && !currentActions.querySelector('[data-action="regenerate"]')) {
        currentActions.insertAdjacentHTML('beforeend', `
            <button type="button"
                    class="chat-message-action"
                    data-action="regenerate"
                    title="Сгенерировать заново"
                    aria-label="Сгенерировать заново">🔄</button>`);
    }
}

/**
 * Показывает ошибку в пузыре ассистента.
 *
 * <para>
 * v1.13.17 (KI-234): добавлена кнопка «Повторить» прямо в error-bubble.
 * Клик → удаляет текущий bubble → запускает regenerate (allowNoAssistant).
 * Поведение как в ChatGPT: новый стрим = новый bubble, старый error уходит.
 * </para>
 *
 * @param {HTMLElement} bubble
 * @param {string} message
 */
function appendAssistantError(bubble, message) {
    if (!bubble) return;
    const contentEl = bubble.querySelector('[data-stream-content]');
    if (!contentEl) return;

    contentEl.classList.add('chat-message-error');

    // KI-234: компактная inline-кнопка Retry в цвет error-плашки.
    // Локализация через data-label-retry-error на #chat-messages (RULES § 4.17).
    const container = document.getElementById('chat-messages');
    const retryLabel = container?.dataset.labelRetryError || 'Повторить';

    contentEl.innerHTML = `
        <div class="chat-message-error-content">
            <span class="chat-message-error-icon" aria-hidden="true">⚠️</span>
            <span class="chat-message-error-text">${escapeHtml(message)}</span>
        </div>
        <button type="button"
                class="chat-message-error-retry"
                data-action="retry-error"
                title="${escapeAttr(retryLabel)}"
                aria-label="${escapeAttr(retryLabel)}">🔄 ${escapeHtml(retryLabel)}</button>`;

    scrollToBottom();
}

// ============ Header ============

function renderChatHeader(chat) {
    const headerEl = document.getElementById('chat-header');
    const titleEl = document.getElementById('chat-title');
    const selectEl = document.getElementById('chat-model-select');

    if (headerEl) headerEl.classList.remove('d-none');
    // KI-079: показать информационную часть (title + select + delete).
    // Сама кнопка «Открыть sidebar» (#btn-expand-sidebar) — вне #chat-header-info,
    // поэтому её видимость управляется отдельно (в applySidebarCollapsed).
    document.getElementById('chat-header-info')?.classList.remove('d-none');
    if (titleEl) titleEl.textContent = chat.title || 'Без названия';
    if (selectEl) populateChatModelSelect(selectEl, chat.model || '');
}

/**
 * Фаза 2.2.4: заполняет <select> моделей чата.
 * @param {HTMLSelectElement} selectEl
 * @param {string} currentModel
 */
function populateChatModelSelect(selectEl, currentModel) {
    const models = state.models || [];
    const hasCurrent = models.some(m => m.id === currentModel);

    const labelDefault = selectEl.dataset.labelDefault || 'по умолчанию';
    const labelUnavailable = selectEl.dataset.labelUnavailable || 'недоступна';
    const labelEmpty = selectEl.dataset.labelEmpty || 'Нет моделей';

    let html = '';

    // Текущей модели нет в списке — показываем как disabled option
    if (currentModel && !hasCurrent) {
        html += `<option value="${escapeHtml(currentModel)}" selected disabled>${
            escapeHtml(currentModel)} (${escapeHtml(labelUnavailable)})</option>`;
    }

    for (const m of models) {
        const selected = m.id === currentModel ? ' selected' : '';
        const suffix = m.isDefault ? ` (${escapeHtml(labelDefault)})` : '';
        html += `<option value="${escapeHtml(m.id)}"${selected}>${escapeHtml(m.id)}${suffix}</option>`;
    }

    // Совсем пусто (ни models, ни текущей) — placeholder
    if (!html) {
        html = `<option value="" selected disabled>${escapeHtml(labelEmpty)}</option>`;
    }

    selectEl.innerHTML = html;
}

/**
 * Фаза 2.2.4: смена модели через PATCH /api/chats/{id}.
 * Оптимистично обновляет state; при ошибке — откат.
 */
async function onChatModelChanged(e) {
    const selectEl = e.target;
    const newModel = selectEl.value;

    // Защита: во время стрима выбор откатывается
    if (state.isStreaming) {
        selectEl.value = state.activeChat?.model || '';
        return;
    }

    const chatId = state.activeChatId;
    if (!chatId || !newModel) return;

    const oldModel = state.activeChat?.model || '';
    if (newModel === oldModel) return;

    try {
        const res = await fetch(`/api/chats/${chatId}`, {
            method: 'PATCH',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ model: newModel }),
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка смены модели', 'error');
            selectEl.value = oldModel;
            return;
        }

        // Успех — обновляем state
        if (state.activeChat) state.activeChat.model = newModel;
        const chatInState = state.chats.find(c => c.id === chatId);
        if (chatInState) chatInState.model = newModel;

        toast(`Модель: ${newModel}`, 'success');
    } catch (ex) {
        toast(ex.message || 'Ошибка смены модели', 'error');
        selectEl.value = oldModel;
    }
}

function showEmptyState() {
    const headerEl = document.getElementById('chat-header');
    if (headerEl) {
        // KI-079: header остаётся видимым (в нём кнопка «Открыть sidebar»).
        // Скрываем только информационную часть (title + select + delete).
        headerEl.classList.remove('d-none');
        document.getElementById('chat-header-info')?.classList.add('d-none');
    }

    // KI-083 (Шаг 6D): сбросить вложения при отсутствии чата.
    state.attachments = [];
    state.ragChunkCount = 0;
    renderAttachmentsBar();

    // v1.11.0 (KI-126, Шаг 1G.2): сброс кэша дебатов.
    state.debates = {};
    state.feedbackDrafts = {};
    // v1.11.0 (KI-129): сброс маппинга toolCallId → sessionId.
    state.debateSessionByToolCallId = {};

    // Фаза 2.2.4: очистить селект модели (нет активного чата)
    const selectEl = document.getElementById('chat-model-select');
    if (selectEl) selectEl.innerHTML = '';

    const container = document.getElementById('chat-messages');
    if (container) {
        const emptyHint = container.dataset.labelEmptyChatHint
            || 'Выберите чат или создайте новый';
        container.innerHTML = `
            <div class="chat-empty-state">
                <div class="text-muted text-center">
                    <img src="/images/logo-icon.svg"
                         alt="IIChatTools"
                         class="chat-empty-logo"
                         aria-hidden="true" />
                    <p class="mb-0">${escapeHtml(emptyHint)}</p>
                    ${renderSuggestedPrompts()}
                </div>
            </div>`;
    }

    // Кнопка «↓ Вниз» не нужна, если нет активного чата
    const btn = document.getElementById('chat-scroll-down');
    if (btn) btn.hidden = true;

    enableInput(false);
}

// ============ Input ============

function enableInput(enabled) {
    const input = document.getElementById('chat-input');
    const btnSend = document.getElementById('btn-send');
    const btnAttach = document.getElementById('btn-attach-file');
    if (input) input.disabled = !enabled;
    if (btnSend) btnSend.disabled = !enabled;
    if (btnAttach) btnAttach.disabled = !enabled;
}

function autoResizeTextarea(el) {
    el.style.height = 'auto';
    el.style.height = Math.min(el.scrollHeight, 200) + 'px';
}

// ============ URL sync ============

function updateUrl(chatId) {
    const url = new URL(window.location.href);
    if (chatId) url.searchParams.set('chatId', chatId);
    else url.searchParams.delete('chatId');
    window.history.replaceState({}, '', url.toString());
}

function readUrlChatId() {
    const raw = new URL(window.location.href).searchParams.get('chatId');
    if (!raw) return null;
    const id = parseInt(raw, 10);
    return Number.isFinite(id) ? id : null;
}

// ============ Markdown rendering (Фаза 2.1.4) ============

/**
 * Рендерит содержимое user-сообщения как plain text.
 * Экранирует HTML, сохраняет переносы строк. Markdown НЕ применяется —
 * пользователь пишет обычный текст (ChatGPT-style), а если он использует
 * `**bold**` или ` ``` ` — они отображаются как есть.
 * @param {string} text
 * @returns {string} Безопасный HTML
 */
function renderUserContent(text) {
    return escapeHtml(text || '').replace(/\n/g, '<br>');
}

/**
 * v1.6.0 (KI-086): рендерит блок «Источники» под ответом ассистента.
 *
 * @param {Array} sources — массив ChatSourceDto (camelCase от MVC):
 *                          { type, label, url, documentPath, chunkIndex, score, snippet }
 * @returns {string} HTML или '' (если источников нет)
 */
function renderSourcesBlock(sources) {
    if (!Array.isArray(sources) || sources.length === 0) return '';

    const container = document.getElementById('chat-messages');
    const headerLabel = container?.dataset.labelSourcesHeader || 'Sources';
    const chunkMetaTpl = container?.dataset.labelSourceChunkMeta || 'chunk {0}';

    const itemsHtml = sources.map(s => {
        const label = escapeHtml(s.label || s.documentPath || '(unknown)');

        // Meta: (фрагмент N, score 0.87) — N показываем 1-based (user-friendly).
        const metaParts = [];
        if (s.chunkIndex != null) {
            metaParts.push(chunkMetaTpl.replace('{0}', String(s.chunkIndex + 1)));
        }
        if (Number.isFinite(s.score)) {
            metaParts.push(`score ${s.score.toFixed(2)}`);
        }
        const metaHtml = metaParts.length > 0
            ? ` <span class="chat-message-sources-meta">(${escapeHtml(metaParts.join(', '))})</span>`
            : '';

        // Кликабельность: web/wiki — ссылка; rag — простой текст.
        const isLink = s.url && (s.type === 'web' || s.type === 'wiki');
        const labelHtml = isLink
            ? `<a href="${escapeHtml(s.url)}" target="_blank" rel="noopener noreferrer">${label}</a>`
            : label;

        // Tooltip — snippet (если есть).
        const titleAttr = s.snippet ? ` title="${escapeAttr(s.snippet)}"` : '';

        return `<li class="chat-message-sources-item"${titleAttr}>${labelHtml}${metaHtml}</li>`;
    }).join('');

    return `
        <div class="chat-message-sources">
            <div class="chat-message-sources-header">📚 ${escapeHtml(headerLabel)}</div>
            <ol class="chat-message-sources-list">${itemsHtml}</ol>
        </div>`;
}

/**
 * Рендерит Markdown в безопасный HTML через marked + DOMPurify.
 * Если библиотеки не загружены — fallback на plain-text с <br>.
 * @param {string} text
 * @returns {string} Безопасный HTML
 */
function renderMarkdown(text) {
    if (!text) return '';

    // Fallback, если marked/DOMPurify не загрузились
    if (!window.marked || !window.DOMPurify) {
        return escapeHtml(text).replace(/\n/g, '<br>');
    }

    try {
        // KI-232: pre-extract $...$ / $$...$$ до marked (KaTeX-формулы
        // ломают markdown-парсер: `_`, `*`, `{}` трактуются как разметка).
        const { text: textForMarked, katex: katexItems } = preExtractKatex(text);

        // breaks: true — одиночный \n превращается в <br> (удобно для чата).
        const rawHtml = window.marked.parse(textForMarked, { breaks: true, gfm: true });

        // KI-232: расширенный ALLOWED_TAGS — SVG (mermaid) + MathML (katex).
        const sanitized = window.DOMPurify.sanitize(rawHtml, {
            ALLOWED_TAGS: [
                // Базовые
                'p', 'br', 'strong', 'em', 'del', 'code', 'pre',
                'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
                'ul', 'ol', 'li', 'blockquote', 'hr',
                'a', 'img', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
                'span', 'div',
                // KI-232: SVG (mermaid рендерит SVG-графику)
                'svg', 'g', 'path', 'rect', 'circle', 'ellipse', 'line',
                'polygon', 'polyline', 'text', 'tspan', 'defs', 'marker',
                'use', 'title', 'desc',
                // KI-232: MathML (katex рендерит MathML для screen readers)
                'math', 'mrow', 'mi', 'mo', 'mn', 'msup', 'msub', 'msubsup',
                'mfrac', 'msqrt', 'mroot', 'mover', 'munder', 'munderover',
                'mtable', 'mtr', 'mtd', 'mtext', 'mspace', 'mphantom', 'mpadded',
                'annotation', 'semantics',
                // KaTeX HTML-фоллбэк (когда MathML не поддерживается)
                'span'
            ],
            ALLOWED_ATTR: [
                'href', 'title', 'alt', 'src', 'class', 'target', 'rel',
                // SVG
                'd', 'viewBox', 'xmlns', 'transform', 'fill', 'stroke',
                'stroke-width', 'stroke-linecap', 'stroke-linejoin',
                'x', 'y', 'width', 'height',
                'x1', 'y1', 'x2', 'y2',
                'cx', 'cy', 'r', 'rx', 'ry',
                'points', 'marker-end', 'marker-start', 'marker-mid',
                'text-anchor', 'dominant-baseline', 'font-size', 'font-family',
                'id', 'refX', 'refY', 'orient', 'markerWidth', 'markerHeight',
                'markerUnits', 'aria-hidden', 'aria-label', 'role',
                // MathML
                'mathvariant', 'displaystyle', 'scriptlevel', 'stretchy',
                'fence', 'separator', 'lspace', 'rspace',
                // KaTeX
                'aria-hidden', 'style'
            ],
            // KI-232: сохраняем `data-*` (в т.ч. для маркеров mermaid).
            ALLOW_DATA_ATTR: true,
            // Разрешаем `style` в MathML/KaTeX (используется для layout).
            ALLOWED_URI_REGEXP: /^(?:(?:https?|mailto|tel|data):|[^a-z]|[a-z+.\-]+(?:[^a-z+.\-:]|$))/i,
        });

        // KI-232: post-render KaTeX. Async не нужен — katex.renderToString
        // синхронный; loadKatex() — только на случай, если ещё не загружен.
        // Если katex не загрузится — placeholder'ы останутся как `%%KATEX...%%`,
        // что некрасиво, но не сломает чат. Поэтому вызываем fire-and-forget.
        if (katexItems.length > 0) {
            renderKatexPlaceholdersAsync(sanitized, katexItems)
                .then(finalHtml => {
                    // Заменяем innerHTML у ближайшего .chat-message-content
                    // (в F5-режиме или при live-стриме уже отрисован sanitized).
                    replacePlaceholderHtml(sanitized, finalHtml);
                })
                .catch(ex => console.warn('[chat] KaTeX render error:', ex));
        }

        return sanitized;
    } catch (ex) {
        console.warn('[chat] Markdown parse error:', ex);
        return escapeHtml(text).replace(/\n/g, '<br>');
    }
}

/**
 * KI-232: pre-extract KaTeX-формул из текста.
 * Заменяет $...$ и $$...$$ на placeholder'ы %%KATEX..., которые
 * уцелеют при проходе через marked + DOMPurify.
 *
 * @param {string} text
 * @returns {{ text: string, katex: Array<{ id: string, tex: string, display: boolean }> }}
 */
function preExtractKatex(text) {
    const items = [];

    // 1. Блочные $$...$$ (могут быть многострочными).
    let out = text.replace(/\$\$([\s\S]+?)\$\$/g, (_, tex) => {
        const id = `KATEXBLOCK${items.length}`;
        items.push({ id, tex: tex.trim(), display: true });
        return `%%${id}%%`;
    });

    // 2. Inline $...$ — только одиночные $, без \n внутри, без $$.
    //    Используем (?<!\$) / (?!\$) — negative lookbehind/lookahead
    //    (поддерживается в Chrome 62+, Firefox 78+, Safari 16.4+).
    out = out.replace(/(?<!\$)\$(?!\$)([^\n$]+?)\$(?!\$)/g, (_, tex) => {
        const id = `KATEXINLINE${items.length}`;
        items.push({ id, tex: tex.trim(), display: false });
        return `%%${id}%%`;
    });

    return { text: out, katex: items };
}

/**
 * KI-232: рендерит placeholder'ы %%KATEX...%% в HTML через KaTeX.
 * Lazy-load библиотеки, если ещё не загружена.
 * @param {string} html
 * @param {Array<{ id: string, tex: string, display: boolean }>} items
 * @returns {Promise<string>}
 */
async function renderKatexPlaceholdersAsync(html, items) {
    if (items.length === 0) return html;

    try {
        await loadKatex();
    } catch (ex) {
        console.warn('[chat] KaTeX не загрузился, формулы останутся как placeholder:', ex);
        // Fallback: replace %%KATEX...%% на простое <code>$tex$</code>.
        let fb = html;
        for (const item of items) {
            const ph = `%%${item.id}%%`;
            const escaped = escapeHtml(item.tex);
            const wrapped = item.display
                ? `<pre class="chat-katex-fallback">${escaped}</pre>`
                : `<code class="chat-katex-fallback">${escaped}</code>`;
            fb = fb.replace(ph, wrapped);
        }
        return fb;
    }

    let out = html;
    for (const item of items) {
        const ph = `%%${item.id}%%`;
        let rendered;
        try {
            rendered = window.katex.renderToString(item.tex, {
                displayMode: item.display,
                throwOnError: false,
                strict: 'ignore',
                output: 'htmlAndMathml',
                trust: false,
            });
        } catch (ex) {
            console.warn('[chat] KaTeX render error:', ex, 'tex:', item.tex);
            const escaped = escapeHtml(item.tex);
            rendered = item.display
                ? `<pre class="chat-katex-fallback">${escaped}</pre>`
                : `<code class="chat-katex-fallback">${escaped}</code>`;
        }
        out = out.replace(ph, rendered);
    }
    return out;
}

/**
 * KI-232: заменяет HTML в DOM-узле, содержащем placeholder'ы.
 * Нужно, потому что renderMarkdown синхронный (возвращает HTML с placeholder'ами),
 * а KaTeX рендерится асинхронно (после lazy-load).
 *
 * Находим узел по «до»-HTML (после DOMPurify, до KaTeX-рендера).
 * Если не нашли — молча выходим (не критично).
 *
 * @param {string} beforeHtml — sanitized HTML с placeholder'ами
 * @param {string} afterHtml — HTML после KaTeX-рендера
 */
function replacePlaceholderHtml(beforeHtml, afterHtml) {
    // Ищем все .chat-message-content.chat-markdown, у которых innerHTML
    // совпадает с beforeHtml. Обычно это 1 элемент (текущий bubble),
    // но в редких случаях — несколько (если один и тот же текст).
    const nodes = document.querySelectorAll('.chat-message-content.chat-markdown');
    for (const node of nodes) {
        if (node.innerHTML === beforeHtml) {
            node.innerHTML = afterHtml;
            // После KaTeX-рендера — обновляем подсветку code blocks (если понадобится).
            enhanceCodeBlocks(node.parentElement || node);
            return;
        }
    }
}

/**
 * KI-232: lazy-load KaTeX (UMD). Идемпотентно.
 * @returns {Promise<void>}
 */
async function loadKatex() {
    if (window.katex) return;

    await loadStyleOnce('/lib/katex/katex.min.css', 'katex-css');
    await loadScriptOnce('/lib/katex/katex.min.js', 'katex-js');

    if (!window.katex) throw new Error('KaTeX: window.katex не определён');
}

/**
 * KI-232: lazy-load Mermaid (UMD). Идемпотентно.
 * @returns {Promise<void>}
 */
async function loadMermaid() {
    if (window.mermaid) return;

    await loadScriptOnce('/lib/mermaid/mermaid.min.js', 'mermaid-js');
    if (!window.mermaid) throw new Error('Mermaid: window.mermaid не определён');

    // Инициализация (один раз).
    window.mermaid.initialize({
        startOnLoad: false,
        theme: 'default',
        // securityLevel: 'strict' — mermaid не выполняет HTML/JS в метках.
        // Всё равно санитайзим через DOMPurify после render() — defense in depth.
        securityLevel: 'strict',
        fontFamily: 'inherit',
    });
}

// ============ Code blocks (Фаза 2.1.4) ============

/**
 * Оборачивает каждый <pre><code> в контейнер с шапкой (язык + Copy/Download).
 * Идемпотентно: повторный вызов не дублирует шапки.
 * @param {HTMLElement} root
 */
function enhanceCodeBlocks(root) {
    if (!root) return;

    root.querySelectorAll('pre > code').forEach(codeEl => {
        const pre = codeEl.parentElement;
        if (!pre || pre.parentElement?.classList.contains('chat-code-block')) return;

        // Язык из класса "language-xxx" или "lang-xxx"
        const langClass = Array.from(codeEl.classList)
            .find(c => c.startsWith('language-') || c.startsWith('lang-'));
        const lang = langClass ? langClass.replace(/^(language-|lang-)/, '') : '';

        // KI-232: mermaid-блоки рендерим отдельно (не оборачиваем в chat-code-block).
        if (lang === 'mermaid') {
            handleMermaidBlock(pre, codeEl);
            return;
        }

        // Обёртка
        const wrapper = document.createElement('div');
        wrapper.className = 'chat-code-block';

        const header = document.createElement('div');
        header.className = 'chat-code-header';

        const langEl = document.createElement('span');
        langEl.className = 'chat-code-lang';
        langEl.textContent = lang || 'code';

        const actions = document.createElement('div');
        actions.className = 'chat-code-actions';

        const copyBtn = document.createElement('button');
        copyBtn.type = 'button';
        copyBtn.className = 'chat-code-btn chat-code-copy';
        copyBtn.title = 'Скопировать код';
        copyBtn.setAttribute('aria-label', 'Скопировать код');
        copyBtn.textContent = '📋';

        const dlBtn = document.createElement('button');
        dlBtn.type = 'button';
        dlBtn.className = 'chat-code-btn chat-code-download';
        dlBtn.title = 'Скачать как файл';
        dlBtn.setAttribute('aria-label', 'Скачать код');
        dlBtn.textContent = '⬇️';

        const codeText = codeEl.textContent || '';

        copyBtn.addEventListener('click', async (e) => {
            e.preventDefault();
            e.stopPropagation();
            try {
                await copyToClipboard(codeText);
                flashCopied(copyBtn);
            } catch {
                toast('Не удалось скопировать', 'error');
            }
        });

        dlBtn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            downloadCodeFile(codeText, lang);
        });

        actions.appendChild(copyBtn);
        actions.appendChild(dlBtn);
        header.appendChild(langEl);
        header.appendChild(actions);

        // Перемещаем <pre> внутрь wrapper
        pre.parentNode.insertBefore(wrapper, pre);
        wrapper.appendChild(header);
        wrapper.appendChild(pre);

        // Подсветка синтаксиса (highlight.js). Если не загружен — просто пропуск.
        // hljs сам найдёт язык по class="language-xxx" или auto-detect.
        if (window.hljs && typeof window.hljs.highlightElement === 'function') {
            try {
                window.hljs.highlightElement(codeEl);
            } catch (ex) {
                console.warn('[chat] highlight.js error:', ex);
            }
        }
    });
}

/**
 * KI-232: обрабатывает ` ```mermaid ` блок — заменяет <pre><code> на
 * <div class="chat-mermaid"> и рендерит диаграмму через mermaid.render().
 *
 * <para>
 * Рендер ленивый: если блок уже в viewport — рендерим сразу;
 * иначе — откладываем до попадания в viewport (IntersectionObserver).
 * Mermaid-рендер тяжёлый (~100-300 мс на диаграмму) — на чате с 5+
 * диаграммами немедленный рендер = фриз.
 * </para>
 *
 * @param {HTMLElement} pre — <pre> обёртка
 * @param {HTMLElement} codeEl — <code class="language-mermaid">
 */
function handleMermaidBlock(pre, codeEl) {
    const source = (codeEl.textContent || '').trim();
    if (!source) return;

    // Оборачиваем в <div class="chat-mermaid"> с placeholder-состоянием.
    const container = document.createElement('div');
    container.className = 'chat-mermaid';
    container.dataset.mermaidState = 'pending';
    container.dataset.mermaidSource = source;

    // Показываем исходник как placeholder (monospace), пока не отрендерилось.
    container.innerHTML = `<pre class="chat-mermaid-source">${escapeHtml(source)}</pre>`;

    pre.parentNode.insertBefore(container, pre);
    pre.remove();

    // Lazy-render: если блок в viewport — сразу, иначе — по появлению.
    if ('IntersectionObserver' in window) {
        const io = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    io.unobserve(entry.target);
                    renderMermaidBlock(entry.target);
                }
            }
        }, { rootMargin: '200px' });

        io.observe(container);
    } else {
        // Fallback: рендерим сразу.
        renderMermaidBlock(container);
    }
}

/**
 * KI-232: рендерит mermaid-блок. Идемпотентно (проверяет state).
 * @param {HTMLElement} container — .chat-mermaid
 */
async function renderMermaidBlock(container) {
    if (!container || container.dataset.mermaidState !== 'pending') return;

    container.dataset.mermaidState = 'rendering';

    const source = container.dataset.mermaidSource || '';
    if (!source) {
        container.dataset.mermaidState = 'error';
        container.innerHTML = `<div class="chat-mermaid-error">Empty mermaid source</div>`;
        return;
    }

    try {
        await loadMermaid();
    } catch (ex) {
        console.warn('[chat] Mermaid не загрузился:', ex);
        // Fallback: возвращаем как было — <pre> с исходником.
        container.dataset.mermaidState = 'error';
        container.innerHTML = `<pre class="chat-mermaid-source">${escapeHtml(source)}</pre>`;
        return;
    }

    try {
        // mermaid.render() возвращает { svg: string }.
        // Уникальный id для каждого блока — mermaid использует его для defs/marker.
        const renderId = `mermaid-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
        const { svg } = await window.mermaid.render(renderId, source);

        // Defense in depth: санитайзим SVG через DOMPurify (mermaid уже
        // с securityLevel: 'strict', но не доверяем полностью).
        const safeSvg = window.DOMPurify
            ? window.DOMPurify.sanitize(svg, {
                USE_PROFILES: { svg: true, svgFilters: true },
                ADD_TAGS: ['foreignObject', 'style', 'marker', 'defs'],
                ADD_ATTR: ['marker-end', 'marker-start', 'marker-mid',
                    'text-anchor', 'dominant-baseline', 'xmlns', 'viewBox'],
            })
            : svg;

        container.innerHTML = safeSvg;
        container.dataset.mermaidState = 'done';
        container.classList.add('chat-mermaid-rendered');
    } catch (ex) {
        console.warn('[chat] Mermaid render error:', ex, 'source:', source);
        container.dataset.mermaidState = 'error';
        container.innerHTML = `
            <div class="chat-mermaid-error">
                <span class="chat-mermaid-error-icon">⚠️</span>
                <span>Не удалось отрисовать диаграмму</span>
            </div>
            <pre class="chat-mermaid-source">${escapeHtml(source)}</pre>`;
    }
}

/**
 * Скачивает текст как файл с расширением по языку.
 * @param {string} code
 * @param {string} lang
 */
function downloadCodeFile(code, lang) {
    const ext = langToExtension(lang);
    const blob = new Blob([code], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = `code-${Date.now()}.${ext}`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

/**
 * Преобразует название языка в расширение файла.
 * @param {string} lang
 * @returns {string}
 */
function langToExtension(lang) {
    const map = {
        javascript: 'js', js: 'js', typescript: 'ts', ts: 'ts',
        python: 'py', py: 'py',
        csharp: 'cs', cs: 'cs', 'c#': 'cs',
        java: 'java', kotlin: 'kt',
        go: 'go', rust: 'rs', rs: 'rs',
        ruby: 'rb', php: 'php',
        bash: 'sh', sh: 'sh', shell: 'sh', zsh: 'sh',
        powershell: 'ps1', ps1: 'ps1',
        sql: 'sql', json: 'json',
        yaml: 'yml', yml: 'yml', xml: 'xml',
        html: 'html', css: 'css', scss: 'scss',
        markdown: 'md', md: 'md',
        dockerfile: 'Dockerfile',
        text: 'txt', plaintext: 'txt',
        // KI-232: mermaid-блоки не имеют code-block-обёртки (enhanceCodeBlocks
        // их вынес в .chat-mermaid), но на всякий случай — расширение.
        mermaid: 'mmd',
    };
    return map[(lang || '').toLowerCase()] || 'txt';
}

// ============ Действия с сообщениями (Фаза 2.1.1 — Copy) ============

/**
 * Рендерит блок действий сообщения (Copy + Regenerate + Retry).
 * @param {string} text Текст для копирования (пустой у streaming-bubble)
 * @param {object} [opts]
 * @param {boolean} [opts.showEdit]       — показать ли кнопку ✏️ на user-сообщении
 * @param {boolean} [opts.showRegenerate] — показать ли кнопку 🔄 на последнем assistant
 * @param {boolean} [opts.showRetry]      — показать ли кнопку «🔄 Повторить» на последнем user (после Stop)
 * @param {boolean} [opts.showFeedback]   — показать ли 👍/👎 (только assistant, KI-233)
 * @param {string|null} [opts.feedback]   — текущая оценка: "positive" | "negative" | null
 * @returns {string} HTML
 */
function renderMessageActions(text, opts = {}) {
    const attr = escapeAttr(text || '');

    const copyBtn = text
        ? `<button type="button"
                   class="chat-message-action"
                   data-action="copy"
                   title="Скопировать"
                   aria-label="Скопировать сообщение">📋</button>`
        : '';

    const editBtn = opts.showEdit
        ? `<button type="button"
                   class="chat-message-action"
                   data-action="edit"
                   title="Редактировать"
                   aria-label="Редактировать сообщение">✏️</button>`
        : '';

    const regenBtn = opts.showRegenerate
        ? `<button type="button"
                   class="chat-message-action"
                   data-action="regenerate"
                   title="Сгенерировать заново"
                   aria-label="Сгенерировать заново">🔄</button>`
        : '';

    // KI-233: 👍 / 👎 на assistant-сообщениях.
    // `feedback` — текущая оценка: "positive" | "negative" | null.
    const feedback = opts.feedback || null;
    const fbPosActive = feedback === 'positive' ? ' active-positive' : '';
    const fbNegActive = feedback === 'negative' ? ' active-negative' : '';
    const labelFbPositive = '👍';
    const labelFbNegative = '👎';

    const feedbackButtons = opts.showFeedback
        ? `<button type="button"
                   class="chat-message-action chat-message-action-feedback${fbPosActive}"
                   data-action="feedback-positive"
                   title="Хороший ответ"
                   aria-label="Хороший ответ">${labelFbPositive}</button>
           <button type="button"
                   class="chat-message-action chat-message-action-feedback${fbNegActive}"
                   data-action="feedback-negative"
                   title="Плохой ответ"
                   aria-label="Плохой ответ">${labelFbNegative}</button>`
        : '';

    const retryBtn = opts.showRetry
        ? `<button type="button"
                   class="chat-message-action chat-message-action-with-text"
                   data-action="retry"
                   title="Повторить"
                   aria-label="Повторить">🔄 Повторить</button>`
        : '';

    const alwaysVisible = opts.showRetry ? ' chat-message-actions-always-visible' : '';

    return `
        <div class="chat-message-actions${alwaysVisible}" data-copy-text="${attr}">
            ${copyBtn}
            ${editBtn}
            ${feedbackButtons}
            ${regenBtn}
            ${retryBtn}
        </div>`;
}

/**
 * KI-233: устанавливает / снимает оценку assistant-сообщения (👍 / 👎).
 *
 * <para>
 * Optimistic UI: сразу меняем активные классы, потом POST.
 * При ошибке — откат к прежнему состоянию + toast.
 * </para>
 *
 * @param {HTMLElement} bubble — `.chat-message.assistant`
 * @param {number} messageId — id assistant-сообщения
 * @param {string|null} feedback — "positive" / "negative" / null (снять)
 */
async function setMessageFeedback(bubble, messageId, feedback) {
    if (!bubble || !state.activeChatId) return;

    const posBtn = bubble.querySelector('[data-action="feedback-positive"]');
    const negBtn = bubble.querySelector('[data-action="feedback-negative"]');
    if (!posBtn || !negBtn) return;

    // Запоминаем текущее состояние (для отката).
    const prevPos = posBtn.classList.contains('active-positive');
    const prevNeg = negBtn.classList.contains('active-negative');

    // Optimistic UI.
    posBtn.classList.toggle('active-positive', feedback === 'positive');
    negBtn.classList.toggle('active-negative', feedback === 'negative');

    try {
        const response = await fetch(`/api/chat/messages/${messageId}/feedback`, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ feedback }),   // null → сервер снимет оценку
        });

        const res = await response.json();
        if (!res || !res.success) {
            // Откат.
            posBtn.classList.toggle('active-positive', prevPos);
            negBtn.classList.toggle('active-negative', prevNeg);
            toast(res?.message || 'Не удалось сохранить оценку', 'error');
            return;
        }

        // Обновляем `state.activeChat` (для последующего F5 — на всякий случай).
        const msg = state.activeChat?.messages?.find(m => m.id === messageId);
        if (msg) msg.feedback = res.data?.feedback ?? null;
    } catch (ex) {
        // Откат при сетевой ошибке.
        posBtn.classList.toggle('active-positive', prevPos);
        negBtn.classList.toggle('active-negative', prevNeg);
        toast(ex.message || 'Ошибка сохранения оценки', 'error');
    }
}

/**
 * Делегированный обработчик клика по кнопкам действий в ленте сообщений.
 * @param {MouseEvent} e
 */
async function onMessageActionClick(e) {
    // KI-234: Retry из error-bubble.
    const retryErrorBtn = e.target.closest('[data-action="retry-error"]');
    if (retryErrorBtn) {
        e.preventDefault();
        e.stopPropagation();

        if (state.isStreaming) return;
        if (!state.activeChatId) {
            toast('Сначала выберите чат', 'warning');
            return;
        }

        // Удаляем error-bubble (как в ChatGPT): новый стрим = новый bubble.
        retryErrorBtn.closest('.chat-message')?.remove();

        // Реюз существующего regenerate (KI-065) — работает и когда
        // assistant-ответа ещё нет (после Stop / error).
        await regenerateLastMessage({ allowNoAssistant: true });
        return;
    }

    // KI-233: 👍 / 👎 feedback на assistant-сообщении.
    const fbPositive = e.target.closest('[data-action="feedback-positive"]');
    const fbNegative = e.target.closest('[data-action="feedback-negative"]');
    if (fbPositive || fbNegative) {
        e.preventDefault();
        e.stopPropagation();

        const bubble = (fbPositive || fbNegative).closest('.chat-message');
        const messageId = parseInt(
            bubble?.dataset.assistantMessageId
            || bubble?.dataset.messageId
            || '0', 10);
        if (messageId <= 0) {
            toast('Сообщение ещё не сохранено', 'warning');
            return;
        }

        // Toggle: если кнопка уже активна — снимаем оценку (null).
        const target = fbPositive ? 'positive' : 'negative';
        const current = (fbPositive || fbNegative).classList.contains('active-positive') ? 'positive'
            : (fbPositive || fbNegative).classList.contains('active-negative') ? 'negative'
            : null;
        const next = current === target ? null : target;

        await setMessageFeedback(bubble, messageId, next);
        return;
    }

    // KI-230: Continue (продолжение обрезанного ответа).
    const continueBtn = e.target.closest('[data-action="continue"]');
    if (continueBtn) {
        e.preventDefault();
        e.stopPropagation();
        const bubble = continueBtn.closest('.chat-message');
        const messageId = parseInt(
            continueBtn.dataset.assistantMessageId
            || bubble?.dataset.assistantMessageId
            || bubble?.dataset.messageId
            || '0', 10);
        if (messageId > 0) {
            await continueLastMessage(messageId, bubble);
        } else {
            toast('Не удалось определить id сообщения', 'error');
        }
        return;
    }

    // v1.11.0 (KI-126, Шаг 1G.3): Send / Skip feedback между раундами.
    const feedbackSend = e.target.closest('[data-action="feedback-send"]');
    if (feedbackSend) {
        e.preventDefault();
        e.stopPropagation();
        const sid = parseInt(feedbackSend.dataset.sessionId, 10);
        if (Number.isFinite(sid)) submitFeedback(sid);
        return;
    }
    const feedbackSkip = e.target.closest('[data-action="feedback-skip"]');
    if (feedbackSkip) {
        e.preventDefault();
        e.stopPropagation();
        const sid = parseInt(feedbackSkip.dataset.sessionId, 10);
        if (Number.isFinite(sid)) skipFeedback(sid);
        return;
    }

    // Edit user-message (Фаза 2.2.6b)
    const editBtn = e.target.closest('[data-action="edit"]');
    if (editBtn) {
        e.preventDefault();
        e.stopPropagation();

        if (state.isStreaming) {
            toast('Подождите завершения генерации', 'warning');
            return;
        }

        const bubble = editBtn.closest('.chat-message');
        const messageId = parseInt(bubble?.dataset.messageId, 10);

        if (!Number.isFinite(messageId)) {
            toast('Сообщение ещё не сохранено', 'warning');
            return;
        }

        startEditUserMessage(bubble, messageId);
        return;
    }

    // Retry (KI-065: после Stop) — то же, что Regenerate, но assistant может отсутствовать
    const retryBtn = e.target.closest('[data-action="retry"]');
    if (retryBtn) {
        e.preventDefault();
        e.stopPropagation();
        await regenerateLastMessage({ allowNoAssistant: true });
        return;
    }

    // Regenerate
    const regenBtn = e.target.closest('[data-action="regenerate"]');
    if (regenBtn) {
        e.preventDefault();
        e.stopPropagation();
        await regenerateLastMessage();
        return;
    }

    // Copy
    const copyBtn = e.target.closest('[data-action="copy"]');
    if (!copyBtn) return;

    e.preventDefault();
    e.stopPropagation();

    const actionsEl = copyBtn.closest('.chat-message-actions');
    const text = actionsEl?.dataset.copyText || '';

    if (!text) {
        toast('Нечего копировать', 'warning');
        return;
    }

    try {
        await copyToClipboard(text);
        flashCopied(copyBtn);
    } catch (ex) {
        console.error('[chat] Копирование не удалось:', ex);
        toast('Не удалось скопировать', 'error');
    }
}

// ============ Edit user-message (Фаза 2.2.6b) ============

/**
 * Начинает inline-edit user-сообщения.
 * Заменяет `.chat-message-content` на `<textarea>` + кнопки Save/Cancel.
 * @param {HTMLElement} bubble — `.chat-message` элемент
 * @param {number} messageId — идентификатор сообщения
 */
function startEditUserMessage(bubble, messageId) {
    if (!bubble) return;

    // Уже в режиме edit?
    if (bubble.classList.contains('chat-message-editing')) return;

    const contentEl = bubble.querySelector('.chat-message-content');
    const bodyEl = bubble.querySelector('.chat-message-body');
    if (!contentEl || !bodyEl) return;

    // Оригинальный HTML/текст для Cancel
    const originalHtml = contentEl.innerHTML;
    const originalText = contentEl.innerText;

    // Скрываем actions на время edit
    const actionsEl = bubble.querySelector('.chat-message-actions');
    if (actionsEl) actionsEl.style.display = 'none';

    // Заменяем content на textarea
    contentEl.innerHTML = `
        <textarea class="chat-message-edit"
                  rows="3"
                  data-original-text="${escapeAttr(originalText)}"></textarea>
        <div class="chat-message-edit-actions">
            <button type="button" class="btn btn-sm btn-primary" data-edit-action="save">Сохранить</button>
            <button type="button" class="btn btn-sm btn-outline-secondary" data-edit-action="cancel">Отмена</button>
        </div>`;

    const textarea = contentEl.querySelector('.chat-message-edit');
    textarea.value = originalText;
    textarea.focus();

    // Автовысота
    textarea.style.height = 'auto';
    textarea.style.height = Math.min(textarea.scrollHeight, 400) + 'px';

    bubble.classList.add('chat-message-editing');

    // Клавиатура: Enter=Save, Shift+Enter=\n, Esc=Cancel
    textarea.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter' && !ev.shiftKey) {
            ev.preventDefault();
            saveUserMessageEdit(bubble, messageId);
        } else if (ev.key === 'Escape') {
            ev.preventDefault();
            cancelUserMessageEdit(bubble, originalHtml);
        }
    });

    // Автоувеличение при вводе
    textarea.addEventListener('input', () => {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, 400) + 'px';
    });

    // Кнопки
    contentEl.querySelector('[data-edit-action="save"]')
        ?.addEventListener('click', () => saveUserMessageEdit(bubble, messageId));
    contentEl.querySelector('[data-edit-action="cancel"]')
        ?.addEventListener('click', () => cancelUserMessageEdit(bubble, originalHtml));
}

/**
 * Отменяет inline-edit: возвращает оригинальный HTML.
 * @param {HTMLElement} bubble
 * @param {string} originalHtml
 */
function cancelUserMessageEdit(bubble, originalHtml) {
    if (!bubble) return;

    const contentEl = bubble.querySelector('.chat-message-content');
    if (contentEl) contentEl.innerHTML = originalHtml;

    bubble.classList.remove('chat-message-editing');

    const actionsEl = bubble.querySelector('.chat-message-actions');
    if (actionsEl) actionsEl.style.display = '';
}

/**
 * Сохраняет inline-edit: POST /api/chat/messages/{id}/edit,
 * удаляет DOM-сообщения после, вызывает regenerate.
 * @param {HTMLElement} bubble
 * @param {number} messageId
 */
async function saveUserMessageEdit(bubble, messageId) {
    if (!bubble) return;

    const textarea = bubble.querySelector('.chat-message-edit');
    if (!textarea) return;

    const newText = (textarea.value || '').trim();
    if (!newText) {
        toast('Сообщение не может быть пустым', 'warning');
        textarea.focus();
        return;
    }

    // Блокируем textarea на время запроса
    textarea.disabled = true;
    bubble.querySelectorAll('[data-edit-action]').forEach(b => b.disabled = true);

    try {
        const res = await fetch(`/api/chat/messages/${messageId}/edit`, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ content: newText }),
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка редактирования', 'error');
            textarea.disabled = false;
            bubble.querySelectorAll('[data-edit-action]').forEach(b => b.disabled = false);
            return;
        }

        // 1. Обновляем содержимое bubble (новый текст, plain text для user)
        const contentEl = bubble.querySelector('.chat-message-content');
        if (contentEl) {
            contentEl.classList.remove('chat-message-editing');
            contentEl.innerHTML = renderUserContent(newText);
        }

        bubble.classList.remove('chat-message-editing');

        // 2. Восстанавливаем actions (в них ✏️ должен остаться)
        const actionsEl = bubble.querySelector('.chat-message-actions');
        if (actionsEl) {
            actionsEl.style.display = '';
            // Обновляем data-copy-text на новое содержимое
            actionsEl.dataset.copyText = newText;
        }

        // 3. Удаляем все DOM-сообщения после отредактированного
        let next = bubble.nextElementSibling;
        while (next) {
            const toRemove = next;
            next = next.nextElementSibling;
            toRemove.remove();
        }

        // 4. Обновляем счётчик сообщений
        //    (assistant + tool + последующие — уже удалены в БД)
        state.activeChatMessageCount = 1;  // только отредактированное user

        toast('Сообщение обновлено, генерируем новый ответ…', 'success');

        // 5. Запускаем regenerate (создаст новый assistant bubble)
        await regenerateLastMessage({ allowNoAssistant: true });
    } catch (ex) {
        toast(ex.message || 'Ошибка редактирования', 'error');
        textarea.disabled = false;
        bubble.querySelectorAll('[data-edit-action]').forEach(b => b.disabled = false);
    }
}

/**
 * Копирует текст в clipboard. Использует navigator.clipboard,
 * fallback — execCommand('copy') через временный textarea (для HTTP).
 * @param {string} text
 * @returns {Promise<void>}
 */
async function copyToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return;
    }

    // Fallback для HTTP / старых браузеров
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.left = '-9999px';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try {
        document.execCommand('copy');
    } finally {
        document.body.removeChild(ta);
    }
}

/**
 * Кратковременно показывает ✅ на кнопке Copy.
 * @param {HTMLElement} btn
 */
function flashCopied(btn) {
    const original = btn.textContent;
    btn.textContent = '✅';
    btn.classList.add('copied');
    btn.disabled = true;

    setTimeout(() => {
        btn.textContent = original;
        btn.classList.remove('copied');
        btn.disabled = false;
    }, 1500);
}

/**
 * Экранирует текст для использования в HTML-атрибуте.
 * Отличается от escapeHtml тем, что переносы строк кодируются как &#10;
 * (браузер декодирует их обратно в \n при чтении через dataset).
 * @param {string} text
 * @returns {string}
 */
function escapeAttr(text) {
    return escapeHtml(text || '').replace(/\n/g, '&#10;');
}

// ============ Утилиты ============

/**
 * Скроллит ленту вниз.
 * @param {boolean} [force=false] — если true, скроллит принудительно и включает autoScroll;
 *                                  если false, скроллит только при включённом autoScroll.
 */
function scrollToBottom(force = false) {
    if (!force && !state.autoScroll) return;

    const container = document.getElementById('chat-messages');
    if (!container) return;

    container.scrollTop = container.scrollHeight;
    state.autoScroll = true;
    updateScrollDownButton();
}

/**
 * Обработчик скролла: определяет, находится ли пользователь у нижнего края.
 * Порог — 80px (как в ChatGPT).
 */
function onMessagesScroll() {
    const container = document.getElementById('chat-messages');
    if (!container) return;

    const distanceFromBottom = container.scrollHeight - container.scrollTop - container.clientHeight;
    state.autoScroll = distanceFromBottom < 80;
    updateScrollDownButton();
}

/**
 * Показывает/скрывает кнопку «↓ Вниз» в зависимости от autoScroll.
 */
function updateScrollDownButton() {
    const btn = document.getElementById('chat-scroll-down');
    if (!btn) return;
    btn.hidden = state.autoScroll;
}

/**
 * Формирует meta-строку для элемента sidebar: «дата · N сообщ.».
 * @param {object} chat ChatListItemDto
 * @returns {string}
 */
function formatChatMeta(chat) {
    const datePart = formatRelativeDate(chat.updatedAt);
    const count = chat.messageCount || 0;
    if (count <= 0) return datePart;
    return `${datePart} · ${count} сообщ.`;
}

function formatRelativeDate(iso) {
    if (!iso) return '';
    const date = new Date(iso);
    const now = new Date();
    const diffSec = Math.floor((now - date) / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHour = Math.floor(diffMin / 60);

    if (diffSec < 60) return 'только что';
    if (diffMin < 60) return `${diffMin} мин назад`;
    if (diffHour < 24) return `${diffHour} ч назад`;

    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) return 'вчера';

    const dd = String(date.getDate()).padStart(2, '0');
    const mm = String(date.getMonth() + 1).padStart(2, '0');
    return `${dd}.${mm}`;
}

function formatTime(iso) {
    if (!iso) return '';
    const date = new Date(iso);
    const hh = String(date.getHours()).padStart(2, '0');
    const mm = String(date.getMinutes()).padStart(2, '0');
    return `${hh}:${mm}`;
}

/**
 * Локально обновляет UpdatedAt активного чата в sidebar на «только что».
 */
function updateSidebarTimeLocally() {
    if (!state.activeChatId) return;

    // KI-069: если идёт inline-edit — не трогаем sidebar (renderChatList
    // заблокирован флагом editingChatId, но лучше не дёргать зря).
    if (state.editingChatId) return;

    const chat = state.chats.find(c => c.id === state.activeChatId);
    if (chat) {
        chat.updatedAt = new Date().toISOString();
        renderChatList();
    }
}

// ============ KI-083 (Шаг 6D): вложения чата (RAG) ============

/**
 * Загружает список вложений активного чата с сервера.
 * Вызывается при selectChat и после операций (upload / delete / clear).
 */
async function loadAttachments() {
    if (!state.activeChatId) {
        state.attachments = [];
        state.ragChunkCount = 0;
        renderAttachmentsBar();
        return;
    }

    const res = await apiGet(`/api/chat/${state.activeChatId}/attachments`);
    if (!res.success) {
        // Не показываем toast — вложения не критичны для чата.
        state.attachments = [];
        state.ragChunkCount = 0;
    } else {
        state.attachments = res.data || [];
        state.ragChunkCount = state.attachments
            .reduce((sum, a) => sum + (a.chunksCount || 0), 0);
    }
    renderAttachmentsBar();
}

/**
 * Загружает файлы в чат (POST multipart).
 * Параллельные запросы — по одному на файл; ошибка одного не отменяет остальные.
 * @param {File[]} files
 */
async function uploadFiles(files) {
    if (!state.activeChatId) return;

    const bar = document.getElementById('chat-attachments-bar');
    const successTpl = bar?.dataset.labelUploadSuccess
        || 'Файл «{0}» проиндексирован ({1} чанков)';

    // v1.13.x (KI-204): запускаем polling OCR-прогресса **параллельно**
    // с upload-запросами. Сервер создаёт запись ChatAttachment в БД
    // до начала ingestion (OCR), поэтому polling с первой же итерации
    // видит attachment с chunksCount=0 + ocrProgress.isProcessing=true.
    const chatIdAtStart = state.activeChatId;
    startOcrPolling(chatIdAtStart);

    try {
        const tasks = files.map(async (file) => {
            const fd = new FormData();
            fd.append('file', file, file.name);

            try {
                const response = await fetch(
                    `/api/chat/${chatIdAtStart}/attachments`,
                    {
                        method: 'POST',
                        credentials: 'same-origin',
                        body: fd,
                    });

                const res = await response.json();
                if (!res || !res.success) {
                    const msg = res?.message || `HTTP ${response.status}`;
                    toast(`«${file.name}»: ${msg}`, 'error');
                    return null;
                }

                const chunks = res.data?.chunksCount ?? 0;
                const msg = successTpl
                    .replace('{0}', file.name)
                    .replace('{1}', String(chunks));
                toast(msg, 'success');
                return res.data;
            } catch (ex) {
                toast(`«${file.name}»: ${ex.message || 'Ошибка соединения'}`, 'error');
                return null;
            }
        });

        await Promise.all(tasks);
    } finally {
        // Останавливаем polling (в т.ч. если upload упал).
        stopOcrPolling();
        // Перезагружаем список вложений с сервера — единый источник истины.
        await loadAttachments();
    }
}

/**
 * v1.13.x (KI-204): polling OCR-прогресса для указанного чата.
 *
 * <para>
 * Опрашивает GET /api/chat/{chatId}/attachments каждые 500 мс,
 * обновляет чипы через renderAttachmentsBar. Авто-остановка:
 * <list type="bullet">
 *   <item>все attachments имеют ocrProgress.isProcessing = false;</item>
 *   <item>state.activeChatId !== chatId (пользователь переключил чат);</item>
 *   <item>явный вызов stopOcrPolling() (из uploadFiles.finally).</item>
 * </list>
 * </para>
 *
 * @param {number} chatId — id чата, для которого отслеживаем прогресс.
 */
function startOcrPolling(chatId) {
    stopOcrPolling();   // на случай, если предыдущий не был остановлен

    const POLL_MS = 500;
    const TIMEOUT_MS = 120_000;   // 2 мин — потолок polling'а
    const startedAt = Date.now();

    const controller = { stopped: false, timerId: null };
    state.ocrPolling = controller;

    async function tick() {
        if (controller.stopped) return;

        // Общий таймаут — защита от зависшего polling'а.
        if (Date.now() - startedAt > TIMEOUT_MS) {
            console.warn('[chat] OCR-polling превысил таймаут 120 сек — стоп');
            controller.stopped = true;
            return;
        }

        // При переключении чата — прекращаем (UI теперь другой).
        if (state.activeChatId !== chatId) {
            controller.stopped = true;
            return;
        }

        try {
            const res = await apiGet(`/api/chat/${chatId}/attachments`);
            if (controller.stopped) return;

            if (res.success && Array.isArray(res.data)) {
                state.attachments = res.data;
                state.ragChunkCount = state.attachments
                    .reduce((sum, a) => sum + (a.chunksCount || 0), 0);
                renderAttachmentsBar();

                // v1.13.x (KI-204-fix): не останавливаемся при ПУСТОМ списке.
                // Сервер создаёт attachment в БД только после multipart-парсинга
                // (что может занять 1-3 сек). До этого polling видит [] — это НЕ
                // повод остановиться, а повод ждать дальше.
                const anyProcessing = state.attachments.some(
                    a => a.ocrProgress && a.ocrProgress.isProcessing);

                if (state.attachments.length > 0 && !anyProcessing) {
                    // Есть attachments, и все завершены — стоп.
                    controller.stopped = true;
                    return;
                }
            }
        } catch (ex) {
            // Не критично — пропускаем итерацию.
            console.debug('[chat] OCR-polling итерация не удалась:', ex);
        }

        if (!controller.stopped) {
            controller.timerId = setTimeout(tick, POLL_MS);
        }
    }

    // Первый вызов — сразу (не ждём 500 мс).
    tick();
}

/**
 * v1.13.x (KI-204): останавливает polling OCR-прогресса (если активен).
 */
function stopOcrPolling() {
    const c = state.ocrPolling;
    if (!c) return;

    c.stopped = true;
    if (c.timerId) {
        clearTimeout(c.timerId);
        c.timerId = null;
    }
    state.ocrPolling = null;
}

/**
 * Удаляет вложение (файл + чанки + запись в БД) по id.
 * @param {number} attachmentId
 */
async function deleteAttachment(attachmentId) {
    if (!state.activeChatId) return;

    try {
        const response = await fetch(
            `/api/chat/${state.activeChatId}/attachments/${attachmentId}`,
            {
                method: 'DELETE',
                credentials: 'same-origin',
            });

        const res = await response.json();
        if (!res || !res.success) {
            toast(res?.message || 'Ошибка удаления вложения', 'error');
            return;
        }

        await loadAttachments();
    } catch (ex) {
        toast(ex.message || 'Ошибка удаления вложения', 'error');
    }
}

/**
 * Очищает все вложения и RAG-индекс активного чата.
 * Требует подтверждения (операция необратима).
 */
async function clearAttachments() {
    if (!state.activeChatId) return;

    const bar = document.getElementById('chat-attachments-bar');
    const confirmMsg = bar?.dataset.labelClearConfirm
        || 'Удалить все вложения и очистить индекс чата?';
    const successMsg = bar?.dataset.labelClearSuccess
        || 'RAG-индекс чата очищен';

    if (!confirm(confirmMsg)) return;

    try {
        const response = await fetch(
            `/api/chat/${state.activeChatId}/attachments/clear`,
            {
                method: 'POST',
                credentials: 'same-origin',
            });

        const res = await response.json();
        if (!res || !res.success) {
            toast(res?.message || 'Ошибка очистки', 'error');
            return;
        }

        toast(successMsg, 'success');
        await loadAttachments();
    } catch (ex) {
        toast(ex.message || 'Ошибка очистки', 'error');
    }
}

/**
 * Перерисовывает панель вложений (chips + summary).
 * Панель скрыта, если нет активного чата или нет вложений.
 */
function renderAttachmentsBar() {
    const bar = document.getElementById('chat-attachments-bar');
    const chipsEl = document.getElementById('chat-attachment-chips');
    const summaryEl = document.getElementById('chat-attachment-summary');
    if (!bar || !chipsEl || !summaryEl) return;

    if (!state.activeChatId || state.attachments.length === 0) {
        bar.hidden = true;
        chipsEl.innerHTML = '';
        summaryEl.innerHTML = '';
        return;
    }

    bar.hidden = false;

    // Chips: 📄 {name} ({size} · {N чанков}) [×]
    // v1.13.x (KI-204): если идёт OCR — вместо «N чанков» показываем
    // «🔄 OCR: 2/3». Класс .chat-attachment-chip-processing подсвечивает чип.
    chipsEl.innerHTML = state.attachments.map(a => {
        const size = formatFileSize(a.sizeBytes || 0);
        const isProcessing = !!(a.ocrProgress && a.ocrProgress.isProcessing);

        let metaHtml;
        if (isProcessing) {
            const tpl = bar.dataset.labelOcrProgress || 'OCR: {0} / {1}';
            const progressText = tpl
                .replace('{0}', String(a.ocrProgress.currentPage || 0))
                .replace('{1}', String(a.ocrProgress.totalPages || 0));
            metaHtml =
                `<span class="chat-attachment-chip-meta chat-attachment-chip-meta-progress">
                    <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                    <span>${escapeHtml(progressText)}</span>
                </span>`;
        } else {
            const chunks = formatChipChunks(a.chunksCount || 0);
            metaHtml = `<span class="chat-attachment-chip-meta">${escapeHtml(size)} · ${escapeHtml(chunks)}</span>`;
        }

        // v1.13.x (KI-205): кнопка «👁 N» — постраничный просмотр PNG.
        const pagesBtnHtml = (a.pagesAvailable && a.pagesCount > 0)
            ? (() => {
                const labelTpl = bar.dataset.labelPagesButton || 'Открыть просмотр страниц';
                const title = `${labelTpl} (${a.pagesCount})`;
                return `<button type="button"
                        class="chat-attachment-chip-pages"
                        data-pages-open="${a.id}"
                        title="${escapeHtml(title)}"
                        aria-label="${escapeHtml(title)}">👁 ${a.pagesCount}</button>`;
            })()
            : '';

        return `
            <div class="chat-attachment-chip${isProcessing ? ' chat-attachment-chip-processing' : ''}"
                 data-attachment-id="${a.id}">
                <span class="chat-attachment-chip-icon" aria-hidden="true">📄</span>
                <span class="chat-attachment-chip-name"
                      title="${escapeHtml(a.fileName || '')}">${escapeHtml(a.fileName || '')}</span>
                ${metaHtml}
                ${pagesBtnHtml}
                <button type="button"
                        class="chat-attachment-chip-remove"
                        data-attachment-remove="${a.id}"
                        title="${escapeHtml(bar.dataset.labelRemove || 'Удалить')}"
                        aria-label="${escapeHtml(bar.dataset.labelRemove || 'Удалить')}">✕</button>
            </div>`;
    }).join('');

    // Summary: RAG: {N чанков}  [Очистить RAG]
    const totalChunks = state.ragChunkCount;
    const chunksText = formatChipChunks(totalChunks);
    const clearLabel = bar.dataset.labelClear || 'Очистить RAG';

    summaryEl.innerHTML = `
        <span class="chat-attachments-summary-text">RAG: ${escapeHtml(chunksText)}</span>
        <button type="button"
                class="chat-attachments-clear"
                data-action="clear-rag"
                title="${escapeHtml(clearLabel)}"
                aria-label="${escapeHtml(clearLabel)}">${escapeHtml(clearLabel)}</button>`;
}

/**
 * Форматирует размер файла (bytes → «N KB» / «N.N MB»).
 * @param {number} bytes
 * @returns {string}
 */
function formatFileSize(bytes) {
    if (!bytes || bytes <= 0) return '0 KB';
    const kb = bytes / 1024;
    if (kb < 1024) return `${kb.toFixed(0)} KB`;
    const mb = kb / 1024;
    return `${mb.toFixed(1)} MB`;
}

/**
 * Форматирует количество чанков: «1 чанк» / «N чанков».
 * Шаблоны берутся из data-атрибутов панели (локализация).
 * @param {number} count
 * @returns {string}
 */
function formatChipChunks(count) {
    const bar = document.getElementById('chat-attachments-bar');
    if (!bar) return String(count);

    if (count === 1) {
        return bar.dataset.labelChunksOne || '1 chunk';
    }
    const tpl = bar.dataset.labelChunksMany || '{0} chunks';
    return tpl.replace('{0}', String(count));
}

// ============ v1.13.x (KI-205 + KI-206): просмотр PNG-страниц ============

/**
 * Открывает модалку просмотра PNG-страниц для attachment'а.
 */
async function openPagesViewer(attachmentId) {
    if (!state.activeChatId) return;

    const modal = document.getElementById('chat-pages-modal');
    if (!modal) return;

    const att = state.attachments.find(a => a.id === attachmentId);
    if (!att) {
        toast('Вложение не найдено', 'error');
        return;
    }

    // Восстанавливаем состояние thumbs (KI-206).
    let thumbsCollapsed = false;
    try {
        thumbsCollapsed = localStorage.getItem(LS_KEY_PAGES_THUMBS) === 'true';
    } catch { /* приватный режим */ }
    modal.classList.toggle('chat-pages-thumbs-collapsed', thumbsCollapsed);

    const wrap = document.getElementById('chat-pages-image-wrap');
    if (wrap) {
        const loading = modal.dataset.labelLoading || 'Loading…';
        wrap.innerHTML = `<div class="chat-pages-modal-loading">
            <span class="spinner-border spinner-border-sm" role="status"></span>
            <span>${escapeHtml(loading)}</span></div>`;
    }

    modal.hidden = false;
    document.body.classList.add('chat-pages-modal-open');

    state.pagesViewer = {
        attachmentId,
        currentPage: 1,
        totalPages: 0,
        fileName: att.fileName || '',
        zoom: 1.0,
        thumbsCollapsed,
        // KI-207-fix6: состояние поиска.
        searchQuery: '',
        searchHits: [],       // [{ pageNumber, type, wordIndexes, tokens }]
        searchIndex: -1,
        searchPrefetch: null, // { [pageNumber]: dto }
        searchPrefetchAbort: 0
    };

    const titleEl = modal.querySelector('.chat-pages-modal-title');
    if (titleEl) titleEl.textContent = att.fileName || '';

    try {
        const res = await apiGet(
            `/api/chat/${state.activeChatId}/attachments/${attachmentId}/pages`);

        if (!res.success || !res.data || !res.data.count) {
            renderPagesViewerError(modal.dataset.labelNotFound || 'Pages not found');
            return;
        }

        state.pagesViewer.totalPages = res.data.count;
        state.pagesViewer.currentPage = 1;

        renderPagesThumbsList(res.data.pages || []);
        renderPagesViewerPage(1);
    } catch (ex) {
        console.error('[chat] openPagesViewer:', ex);
        renderPagesViewerError('Ошибка загрузки страниц');
    }
}

/**
 * KI-206: рендер списка thumbnails слева.
 * @param {Array} pages — [{ pageNumber, sizeBytes }, ...]
 */
function renderPagesThumbsList(pages) {
    const listEl = document.getElementById('chat-pages-thumbs-list');
    if (!listEl) return;

    const viewer = state.pagesViewer;
    if (!viewer || !state.activeChatId) return;

    listEl.innerHTML = '';

    for (const p of pages) {
        const item = document.createElement('div');
        item.className = 'chat-pages-thumb';
        item.dataset.pageNumber = String(p.pageNumber);

        const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${p.pageNumber}`;
        item.innerHTML = `
            <span class="chat-pages-thumb-num">${p.pageNumber}</span>
            <img class="chat-pages-thumb-img"
                 data-src="${url}"
                 alt="Page ${p.pageNumber}"
                 loading="lazy" />`;

        item.addEventListener('click', () => gotoPagesViewer(p.pageNumber));
        listEl.appendChild(item);
    }

    // KI-206: lazy-load thumbnails через IntersectionObserver.
    const imgs = listEl.querySelectorAll('img[data-src]');
    if ('IntersectionObserver' in window) {
        const io = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    const img = entry.target;
                    if (img.dataset.src && !img.src) {
                        img.src = img.dataset.src;
                    }
                    io.unobserve(img);
                }
            }
        }, { root: listEl, rootMargin: '200px' });

        imgs.forEach(img => io.observe(img));
    } else {
        // Fallback: грузим сразу все.
        imgs.forEach(img => { img.src = img.dataset.src; });
    }
}

/**
 * KI-206: подсветка активного thumbnail.
 */
function highlightActiveThumb(pageNumber) {
    const listEl = document.getElementById('chat-pages-thumbs-list');
    if (!listEl) return;

    listEl.querySelectorAll('.chat-pages-thumb').forEach(el => {
        const active = parseInt(el.dataset.pageNumber, 10) === pageNumber;
        el.classList.toggle('chat-pages-thumb-active', active);
        if (active) {
            el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }
    });
}

/**
 * KI-205 (v2, KI-206): рендер страницы.
 */
function renderPagesViewerPage(pageNumber) {
    const modal = document.getElementById('chat-pages-modal');
    const viewer = state.pagesViewer;
    if (!modal || !viewer || !state.activeChatId) return;

    if (pageNumber < 1) pageNumber = 1;
    if (pageNumber > viewer.totalPages) pageNumber = viewer.totalPages;

    viewer.currentPage = pageNumber;

    const prevLabel = modal.dataset.labelPrev || 'Previous';
    const nextLabel = modal.dataset.labelNext || 'Next';
    const wrap = document.getElementById('chat-pages-image-wrap');
    const viewport = modal.querySelector('.chat-pages-viewport');

    if (wrap) {
        // Сброс inline-стилей (могли остаться от старого applyPagesZoom
        // с transform-подходом). При width/height-подходе wrap вообще
        // не должен иметь inline-размеров.
        wrap.style.width = '';
        wrap.style.height = '';
        wrap.style.transform = '';
        wrap.style.transformOrigin = '';

        const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${pageNumber}`;
        // KI-207-fix3: только img внутри wrap. Nav-кнопки — в viewport
        // (чтобы position:absolute центрировался относительно всего
        // viewport, а не относительно img — иначе при zoom < 100%
        // они «уезжают» вместе с wrap).
        wrap.innerHTML = `
            <img class="chat-pages-modal-image"
                 data-page="${pageNumber}"
                 src="${url}"
                 alt="Page ${pageNumber}" />`;

        const img = wrap.querySelector('.chat-pages-modal-image');
        if (img) {
            const onImgReady = () => {
                if (viewer && viewer.currentPage === pageNumber) {
                    applyPagesZoom();
                    // KI-207: загрузить text layer после img (и подогнать scale).
                    loadPagesTextLayer(pageNumber);
                }
            };

            // KI-207-fix: если PNG уже в кеше браузера, событие `load`
            // НЕ сработает. Проверяем img.complete.
            if (img.complete && img.naturalWidth > 0) {
                onImgReady();
            } else {
                img.addEventListener('load', onImgReady);
            }
        }

        // Прелоад следующей страницы.
        if (pageNumber < viewer.totalPages) {
            const pre = new Image();
            pre.src = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${pageNumber + 1}`;
        }
    }

    // KI-207-fix3: nav-кнопки — в .chat-pages-viewport (не в wrap).
    // Создаём один раз, обновляем disabled.
    if (viewport) {
        let navPrev = viewport.querySelector('.chat-pages-modal-nav-prev');
        let navNext = viewport.querySelector('.chat-pages-modal-nav-next');

        if (!navPrev) {
            viewport.insertAdjacentHTML('beforeend', `
                <button type="button"
                        class="chat-pages-modal-nav chat-pages-modal-nav-prev"
                        data-action="prev"
                        title="${escapeHtml(prevLabel)}"
                        aria-label="${escapeHtml(prevLabel)}">‹</button>`);
            navPrev = viewport.querySelector('.chat-pages-modal-nav-prev');
            navPrev?.addEventListener('click', () => navigatePagesViewer(-1));
        }

        if (!navNext) {
            viewport.insertAdjacentHTML('beforeend', `
                <button type="button"
                        class="chat-pages-modal-nav chat-pages-modal-nav-next"
                        data-action="next"
                        title="${escapeHtml(nextLabel)}"
                        aria-label="${escapeHtml(nextLabel)}">›</button>`);
            navNext = viewport.querySelector('.chat-pages-modal-nav-next');
            navNext?.addEventListener('click', () => navigatePagesViewer(+1));
        }

        if (navPrev) navPrev.disabled = pageNumber <= 1;
        if (navNext) navNext.disabled = pageNumber >= viewer.totalPages;
    }

    // Счётчик «N / M».
    const counterEl = modal.querySelector('.chat-pages-modal-counter');
    if (counterEl) {
        const tpl = modal.dataset.labelCounter || '{0} / {1}';
        counterEl.textContent = tpl
            .replace('{0}', String(pageNumber))
            .replace('{1}', String(viewer.totalPages));
    }

    // Обновление thumbs: активный.
    highlightActiveThumb(pageNumber);

    // Обновление input «Стр. N из M».
    const gotoInput = document.getElementById('chat-pages-goto-input');
    if (gotoInput) {
        gotoInput.value = String(pageNumber);
        gotoInput.max = String(viewer.totalPages);
    }

    // KI-207-fix3: панель поиска НЕ сбрасываем при смене страницы —
    // пользователь ожидает кросс-страничный поиск (как в Acrobat).
    // Реальный сброс — только в closePagesSearch().
}

/**
 * KI-206: переход на конкретную страницу.
 */
function gotoPagesViewer(pageNumber) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    if (pageNumber < 1) pageNumber = 1;
    if (pageNumber > viewer.totalPages) pageNumber = viewer.totalPages;
    if (pageNumber === viewer.currentPage) return;

    renderPagesViewerPage(pageNumber);
}

/**
 * KI-206: навигация.
 */
function navigatePagesViewer(delta) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    const next = viewer.currentPage + delta;
    if (next < 1 || next > viewer.totalPages) return;

    renderPagesViewerPage(next);
}

// ============================================================
// KI-206: зум и fit.
// ============================================================

/** Текущий зум (0.25…4.0). */
function getPagesZoom() {
    return state.pagesViewer?.zoom ?? 1.0;
}

/**
 * Устанавливает зум + применяет его к картинке + обновляет label.
 * @param {number} value — 0.25…4.0
 */
function setPagesZoom(value) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    let v = Number(value);
    if (!Number.isFinite(v) || v <= 0) v = 1.0;
    v = Math.max(PAGES_ZOOM_MIN, Math.min(PAGES_ZOOM_MAX, v));

    viewer.zoom = v;

    // Обновляем числовой label.
    const modal = document.getElementById('chat-pages-modal');
    const label = modal?.querySelector('.chat-pages-zoom-value');
    if (label) {
        label.textContent = `${Math.round(v * 100)}%`;
    }

    applyPagesZoom();
}

/**
 * Применяет viewer.zoom к <img>: задаёт width/height в px напрямую.
 *
 * <para>
 * Раньше использовался transform: scale(), но это давало визуальный размер,
 * не совпадающий с layout — центрирование и скролл работали неправильно
 * (картинка «уезжала» из своего бокса). Теперь layout == visual.
 * </para>
 */
function applyPagesZoom() {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    const img = document.querySelector('#chat-pages-image-wrap .chat-pages-modal-image');
    if (!img || !img.naturalWidth) return;

    const z = viewer.zoom;
    const scaledW = Math.round(img.naturalWidth * z);
    const scaledH = Math.round(img.naturalHeight * z);

    img.style.width = `${scaledW}px`;
    img.style.height = `${scaledH}px`;
    // Сброс на случай, если остался transform от предыдущей версии.
    img.style.transform = '';
    img.style.transformOrigin = '';

    // KI-207: синхронизировать scale text layer с новым размером img.
    applyTextLayerScale();
}

/**
 * KI-206: fit-width / fit-page.
 * @param {"width"|"page"} kind
 */
function fitPagesViewer(kind) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    const img = document.querySelector('#chat-pages-image-wrap .chat-pages-modal-image');
    if (!img || img.naturalWidth <= 0) return;

    const viewport = document.querySelector('.chat-pages-viewport');
    if (!viewport) return;

    // clientWidth/Height включают padding. Вычитаем его, чтобы получить
    // реальную доступную площадь под картинку. +2px запаса, чтобы бордер
    // картинки (1px с каждой стороны) не вызывал лишний скролл.
    const cs = window.getComputedStyle(viewport);
    const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
    const padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);

    const availW = Math.max(1, viewport.clientWidth - padX - 2);
    const availH = Math.max(1, viewport.clientHeight - padY - 2);

    if (kind === 'width') {
        setPagesZoom(availW / img.naturalWidth);
    } else if (kind === 'page') {
        const zW = availW / img.naturalWidth;
        const zH = availH / img.naturalHeight;
        setPagesZoom(Math.min(zW, zH));
    }
}
/**
 * KI-206: toggle панели превью.
 */
function togglePagesThumbs() {
    const modal = document.getElementById('chat-pages-modal');
    if (!modal) return;

    const collapsed = !modal.classList.contains('chat-pages-thumbs-collapsed');
    modal.classList.toggle('chat-pages-thumbs-collapsed', collapsed);

    if (state.pagesViewer) {
        state.pagesViewer.thumbsCollapsed = collapsed;
    }

    try {
        localStorage.setItem(LS_KEY_PAGES_THUMBS, collapsed ? 'true' : 'false');
    } catch { /* приватный режим */ }
}

/**
 * KI-205: ошибка в модалке.
 */
function renderPagesViewerError(message) {
    const modal = document.getElementById('chat-pages-modal');
    if (!modal) return;

    const wrap = document.getElementById('chat-pages-image-wrap');
    if (wrap) {
        wrap.innerHTML = `<div class="chat-pages-modal-loading">${escapeHtml(message)}</div>`;
    }

    const counterEl = modal.querySelector('.chat-pages-modal-counter');
    if (counterEl) counterEl.textContent = '';
}

/**
 * KI-205: закрытие модалки.
 */
function closePagesViewer() {
    const modal = document.getElementById('chat-pages-modal');
    if (!modal) return;

    closePagesSearch();

    modal.hidden = true;
    document.body.classList.remove('chat-pages-modal-open');
    state.pagesViewer = null;

    const wrap = document.getElementById('chat-pages-image-wrap');
    if (wrap) wrap.innerHTML = '';

    const thumbs = document.getElementById('chat-pages-thumbs-list');
    if (thumbs) thumbs.innerHTML = '';
}

// ============================================================
// v1.13.x (KI-207): text layer + поиск.
// ============================================================

/**
 * KI-207: загружает и рендерит text layer для страницы.
 * Тихий fail — если нет page-N.json, просто не рендерим слой.
 * @param {number} pageNumber
 */
async function loadPagesTextLayer(pageNumber) {
    const viewer = state.pagesViewer;
    if (!viewer || !state.activeChatId) return;

    const wrap = document.getElementById('chat-pages-image-wrap');
    if (!wrap) return;

    // Сразу удаляем предыдущий слой (страница сменилась).
    wrap.querySelector('.chat-pages-text-layer')?.remove();
    viewer.textLayer = null;
    viewer.textLayerDto = null;

    try {
        const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${pageNumber}/ocr`;

        // apiGet умеет парсить { success, data }; но endpoint возвращает
        // «сырой» JSON PageTextLayerDto при успехе. Поэтому fetch напрямую.
        const response = await fetch(url, { credentials: 'same-origin' });
        // KI-208-fix3: 404 (нет text layer) / 429 (rate limit) / 500 —
        // тихо пропускаем. Frontend просто не рендерит слой.
        if (!response.ok) {
            if (response.status === 429) {
                console.debug('[chat] text layer: rate limit (429) на page', pageNumber);
            }
            return;
        }

        const contentType = response.headers.get('content-type') || '';
        if (!contentType.includes('application/json')) return;

        let dto;
        try {
            dto = await response.json();
        } catch {
            return;
        }

        // Проверка: если сервер вернул { success: false } — пропускаем.
        if (dto && dto.success === false) return;

        // KI-207-fix: нормализация регистра. Принимаем оба варианта:
        //   • camelCase (новый формат после KI-207-fix);
        //   • PascalCase (старые page-N.json, или если сервер вернёт
        //     сырой JsonConvert.SerializeObject).
        const width = dto.width ?? dto.Width;
        const height = dto.height ?? dto.Height;
        const rawWords = dto.words ?? dto.Words ?? [];
        if (!width || !height || !Array.isArray(rawWords) || rawWords.length === 0) {
            return;
        }

        // Нормализуем слова: { text, x, y, w, h } в camelCase.
        const normalized = {
            width,
            height,
            words: rawWords.map(w => ({
                text: w.text ?? w.Text,
                x: w.x ?? w.X,
                y: w.y ?? w.Y,
                w: w.w ?? w.W,
                h: w.h ?? w.H
            }))
        };

        // Защита от гонки: пользователь мог переключить страницу.
        if (viewer.currentPage !== pageNumber) return;

        renderTextLayer(wrap, normalized);

        // KI-207-fix6: если есть активный поиск — подсвечиваем query
        // локально (все hits на странице) + скроллим к активному.
        if (viewer.searchQuery) {
            applyLocalHighlights(viewer.textLayer, viewer.searchQuery, viewer.textLayerDto);

            // Если текущий hit — на этой странице, активируем его.
            if (viewer.searchHits && viewer.searchHits.length > 0
                && viewer.searchIndex >= 0)
            {
                const hit = viewer.searchHits[viewer.searchIndex];
                if (hit && hit.pageNumber === pageNumber) {
                    markActiveHit(hit);
                }
            }
        }
    } catch (ex) {
        console.debug('[chat] loadPagesTextLayer fail (ignore):', ex);
    }
}

/**
 * KI-207: рендерит прозрачный text layer поверх <img>.
 * @param {HTMLElement} wrap
 * @param {{width:number,height:number,words:Array}} dto
 */
function renderTextLayer(wrap, dto) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    const layer = document.createElement('div');
    layer.className = 'chat-pages-text-layer';
    layer.style.width = `${dto.width}px`;
    layer.style.height = `${dto.height}px`;

    for (let i = 0; i < dto.words.length; i++) {
        const w = dto.words[i];
        if (!w.text || w.w <= 0 || w.h <= 0) continue;

        const span = document.createElement('span');
        span.className = 'chat-pages-text-word';
        span.textContent = w.text;
        span.dataset.wordIndex = String(i);
        span.style.left = `${w.x}px`;
        span.style.top = `${w.y}px`;
        span.style.width = `${w.w}px`;
        span.style.height = `${w.h}px`;
        // font-size ~0.9 от высоты — позиционирование по bbox, не по глифам.
        // Прозрачный цвет делает точность не критичной; selection-follows-bbox.
        span.style.fontSize = `${w.h * 0.9}px`;
        span.style.lineHeight = `${w.h}px`;

        layer.appendChild(span);
    }

    wrap.appendChild(layer);

    viewer.textLayer = layer;
    viewer.textLayerDto = dto;
    // KI-207-fix7: НЕ сбрасываем searchHits/searchIndex — они принадлежат
    // поисковому сеансу, а не конкретной странице. Иначе после перехода
    // на другую страницу кнопки поиска «не работают» (hits = []).

    applyTextLayerScale();
}

/**
 * KI-207: синхронизирует transform text layer с текущим размером <img>.
 */
function applyTextLayerScale() {
    const viewer = state.pagesViewer;
    if (!viewer || !viewer.textLayer || !viewer.textLayerDto) return;

    const naturalW = viewer.textLayerDto.width;
    if (!naturalW) return;

    // Считаем масштаб от фактической ширины <img> в DOM.
    const img = document.querySelector('#chat-pages-image-wrap .chat-pages-modal-image');
    if (!img) return;

    // img.offsetWidth — layout-ширина в CSS px (уже с учётом zoom).
    const displayW = img.offsetWidth;
    if (displayW <= 0) return;

    const scale = displayW / naturalW;
    viewer.textLayer.style.transform = `scale(${scale})`;
    viewer.textLayer.style.transformOrigin = 'top left';
}

/**
 * KI-207: открывает поисковую панель в модалке страниц.
 */
function openPagesSearch() {
    const bar = document.getElementById('chat-pages-search-bar');
    if (!bar) return;

    bar.hidden = false;
    const input = document.getElementById('chat-pages-search-input');
    if (input) {
        input.focus();
        input.select();
    }
}

/**
 * KI-207: закрывает поиск + снимает подсветки.
 */
function closePagesSearch() {
    const bar = document.getElementById('chat-pages-search-bar');
    if (bar) bar.hidden = true;

    // KI-207-fix2: очищаем input — иначе при повторном Ctrl+F панель
    // открывается со старым значением и прежним счётчиком (на скрине
    // «1/4» после смены страницы).
    const input = document.getElementById('chat-pages-search-input');
    if (input) input.value = '';

    const viewer = state.pagesViewer;
    if (viewer?.textLayer) {
        clearPagesSearchHighlights(viewer.textLayer);
    }
    if (viewer) {
        viewer.searchHits = [];
        viewer.searchIndex = -1;
        viewer.searchQuery = '';
        // KI-207-fix4: сбрасываем кеш префетча (освобождаем память).
        viewer.searchPrefetch = null;
        viewer.searchPrefetchAbort = (viewer.searchPrefetchAbort || 0) + 1;
    }

    updatePagesSearchCounter(0, 0);
}

/**
 * KI-207: снимает <mark> со всех слов.
 */function clearPagesSearchHighlights(layer) {
    layer.querySelectorAll('mark.chat-pages-search-hit').forEach(mark => {
        const parent = mark.parentNode;
        if (!parent) return;
        while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
        parent.removeChild(mark);
        parent.normalize();
    });
}

/**
 * KI-207-fix7: находит все вхождения query в text layer'е страницы.
 *
 * <para>
 * Стратегия:
 * <list type="number">
 *   <item>Одно слово → все вхождения (каждое — отдельный hit).</item>
 *   <item>Много слов → сначала пробуем найти <b>фразу целиком</b>
 *         (substring в flat-text). Если нашли — hits по диапазонам.</item>
 *   <item>Если фразы нет → fallback на <b>OR</b> по отдельным словам
 *         (каждое вхождение — отдельный hit). Это ловит случаи
 *         переноса слов между строками/страницами («добровольных
 *         имущественных» разорвано на границе стр. 1/2).</item>
 * </list>
 * </para>
 *
 * @param {object} layer — { width, height, words: [...] }
 * @param {string} query
 * @returns {Array<{ type: 'word'|'phrase', wordIndexes: number[] }>}
 */
function findMatchesInLayer(layer, query) {
    const q = (query || '').trim().toLowerCase();
    if (!q || !layer || !Array.isArray(layer.words)) return [];

    const words = layer.words;

    // 1. Одно слово — все его вхождения.
    if (!q.includes(' ')) {
        const out = [];
        for (let i = 0; i < words.length; i++) {
            const text = (words[i].text || '').toLowerCase();
            if (text.includes(q)) {
                out.push({ type: 'word', wordIndexes: [i] });
            }
        }
        return out;
    }

    // 2. Много слов — пробуем фразу целиком (substring в flat-text).
    //    flat = «word1 word2 ... wordN» (lowercase), starts[i] = смещение i-го слова.
    const starts = new Array(words.length);
    let pos = 0;
    for (let i = 0; i < words.length; i++) {
        starts[i] = pos;
        pos += (words[i].text || '').length + 1;   // +1 за пробел
    }
    const flat = words.map(w => (w.text || '').toLowerCase()).join(' ');

    const phraseHits = [];
    let idx = flat.indexOf(q);
    while (idx >= 0) {
        const end = idx + q.length;
        let startWord = -1;
        let endWord = -1;
        for (let i = 0; i < words.length; i++) {
            const ws = starts[i];
            const we = ws + (words[i].text || '').length;
            if (we <= idx) continue;
            if (ws >= end) break;
            if (startWord < 0) startWord = i;
            endWord = i;
        }
        if (startWord >= 0) {
            const wordIndexes = [];
            for (let i = startWord; i <= endWord; i++) wordIndexes.push(i);
            phraseHits.push({ type: 'phrase', wordIndexes });
        }
        idx = flat.indexOf(q, idx + 1);
    }

    if (phraseHits.length > 0) {
        return phraseHits;
    }

    // 3. Фразы нет — fallback OR по отдельным токенам.
    //    Каждое вхождение каждого токена — отдельный hit.
    const tokens = q.split(/\s+/).filter(Boolean);
    const orHits = [];
    for (let i = 0; i < words.length; i++) {
        const text = (words[i].text || '').toLowerCase();
        for (const t of tokens) {
            if (text.includes(t)) {
                orHits.push({ type: 'word', wordIndexes: [i] });
                break;   // одно слово — один hit
            }
        }
    }
    return orHits;
}

/**
 * KI-207-fix6: подсвечивает конкретный hit (создаёт <mark>).
 *
 * @param {HTMLElement} layer — .chat-pages-text-layer
 * @param {object} hit — { wordIndexes: number[] }
 * @param {boolean} active — пометить active-классом.
 */
function highlightHit(layer, hit, active) {
    if (!layer || !hit || !hit.wordIndexes) return;

    for (const idx of hit.wordIndexes) {
        const span = layer.querySelector(
            `.chat-pages-text-word[data-word-index="${idx}"]`);
        if (!span) continue;

        let mark = span.querySelector('.chat-pages-search-hit');
        if (!mark) {
            // Ещё не обёрнуто — оборачиваем.
            const textNodes = Array.from(span.childNodes);
            if (textNodes.length === 0) continue;
            mark = document.createElement('mark');
            mark.className = 'chat-pages-search-hit';
            while (span.firstChild) mark.appendChild(span.firstChild);
            span.appendChild(mark);
        }

        if (active) mark.classList.add('chat-pages-search-hit-active');
    }
}

/**
 * KI-207-fix6: применяет поиск. Локальная подсветка + async префетч
 * для сквозного счётчика по документу.
 */
function applyPagesSearch(query) {
    const viewer = state.pagesViewer;
    if (!viewer) return;

    // KI-208 (race-fix KI-207): если список страниц ещё не загружен
    // (totalPages === 0), префетч стартует с 0 страниц и находит 0 hits.
    // Симптом — «поиск отвалился, потом заработал». Ждём.
    if (!viewer.totalPages || viewer.totalPages === 0) {
        console.debug('[chat] applyPagesSearch: totalPages ещё не загружен');
        return;
    }

    const q = (query || '').trim().toLowerCase();
    viewer.searchQuery = q;

    // Локальная подсветка (мгновенно).
    if (viewer.textLayer) {
        clearPagesSearchHighlights(viewer.textLayer);
        if (q && viewer.textLayerDto) {
            const hits = findMatchesInLayer(viewer.textLayerDto, q);
            for (const h of hits) {
                highlightHit(viewer.textLayer, h, false);
            }
        }
    }

    if (!q) {
        viewer.searchHits = [];
        viewer.searchIndex = -1;
        updatePagesSearchCounter(0, 0);
        return;
    }

    // Async префетч — счётчик по документу.
    startDocumentSearch(q);
}

/**
 * KI-207-fix6: async-префетч всех page-N.json для сквозного счётчика.
 * При вводе показывает «Поиск… N/M», потом актуальное число.
 */
async function startDocumentSearch(query) {
    const viewer = state.pagesViewer;
    if (!viewer || !query) return;
    if (!viewer.totalPages || viewer.totalPages === 0) return;   // KI-208

    // Отмена предыдущего префетча.
    viewer.searchPrefetchAbort = (viewer.searchPrefetchAbort || 0) + 1;
    const myAbort = viewer.searchPrefetchAbort;
    const qAtStart = query;

    // Кеш слоёв по страницам.
    viewer.searchPrefetch = viewer.searchPrefetch || {};
    const cache = viewer.searchPrefetch;

    // Прогресс.
    let fetched = 0;
    const toFetch = [];
    for (let p = 1; p <= viewer.totalPages; p++) {
        if (cache[p]) fetched++;
        else toFetch.push(p);
    }

    // Прогресс-индикатор.
    if (toFetch.length > 0) {
        updatePagesSearchCounter(0, 0, /*loading*/ true, fetched, viewer.totalPages);
    }

    // Параллельный фетч (лимит 5).
    const CONCURRENCY = 5;
    for (let i = 0; i < toFetch.length; i += CONCURRENCY) {
        if (viewer.searchQuery !== qAtStart) return;
        if (viewer.searchPrefetchAbort !== myAbort) return;

        const batch = toFetch.slice(i, i + CONCURRENCY);
        const results = await Promise.all(batch.map(async (p) => {
            try {
                const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${p}/ocr`;
                const resp = await fetch(url, { credentials: 'same-origin' });
                if (!resp.ok) return null;
                const ct = resp.headers.get('content-type') || '';
                if (!ct.includes('application/json')) return null;
                const dto = await resp.json();
                if (dto && dto.success === false) return null;

                const width = dto.width ?? dto.Width;
                const height = dto.height ?? dto.Height;
                const rawWords = dto.words ?? dto.Words ?? [];
                if (!Array.isArray(rawWords) || !width || !height) return null;

                const norm = {
                    width, height,
                    words: rawWords.map(w => ({
                        text: w.text ?? w.Text,
                        x: w.x ?? w.X,
                        y: w.y ?? w.Y,
                        w: w.w ?? w.W,
                        h: w.h ?? w.H
                    }))
                };
                cache[p] = norm;
                return norm;
            } catch {
                return null;
            }
        }));

        fetched += results.filter(r => r !== null).length;

        if (viewer.searchQuery === qAtStart && viewer.searchPrefetchAbort === myAbort) {
            updatePagesSearchCounter(0, 0, /*loading*/ true, fetched, viewer.totalPages);
        }
    }

    if (viewer.searchQuery !== qAtStart) return;
    if (viewer.searchPrefetchAbort !== myAbort) return;

    // Считаем все hits.
    const allHits = [];
    for (let p = 1; p <= viewer.totalPages; p++) {
        const layer = cache[p];
        if (!layer) continue;
        const hits = findMatchesInLayer(layer, qAtStart);
        for (const h of hits) {
            allHits.push({ pageNumber: p, ...h });
        }
    }

    viewer.searchHits = allHits;
    viewer.searchIndex = allHits.length > 0 ? 0 : -1;

    if (allHits.length === 0) {
        updatePagesSearchCounter(0, 0);
        return;
    }

    // Активный — первый на текущей странице или первый в документе.
    let idx = allHits.findIndex(h => h.pageNumber === viewer.currentPage);
    if (idx < 0) idx = 0;
    viewer.searchIndex = idx;

    updatePagesSearchCounter(idx + 1, allHits.length);

    // Подсвечиваем активный локально (если страница совпадает).
    const hit = allHits[idx];
    if (hit.pageNumber === viewer.currentPage) {
        markActiveHit(hit);
    } else {
        gotoPagesViewer(hit.pageNumber);
    }
}

/**
 * KI-207-fix6: помечает активный hit (снимает класс со всех, ставит на слова hit'а).
 */
function markActiveHit(hit) {
    const viewer = state.pagesViewer;
    if (!viewer?.textLayer || !hit) return;
    if (viewer.currentPage !== hit.pageNumber) return;   // ← fix7: страница не та

    // Снимаем active со всех.
    viewer.textLayer.querySelectorAll('.chat-pages-search-hit-active')
        .forEach(el => el.classList.remove('chat-pages-search-hit-active'));

    // Подсвечиваем активный.
    highlightHit(viewer.textLayer, hit, true);

    // Скролл к первому слову hit'а.
    const firstIdx = hit.wordIndexes[0];
    const firstSpan = viewer.textLayer.querySelector(
        `.chat-pages-text-word[data-word-index="${firstIdx}"]`);
    if (!firstSpan) return;

    const viewport = document.querySelector('.chat-pages-viewport');
    if (!viewport) return;

    const spanRect = firstSpan.getBoundingClientRect();
    const viewRect = viewport.getBoundingClientRect();
    if (spanRect.top < viewRect.top || spanRect.bottom > viewRect.bottom) {
        const targetY = firstSpan.offsetTop * (viewer.zoom || 1)
            - viewport.clientHeight / 2
            + (firstSpan.offsetHeight * (viewer.zoom || 1)) / 2;
        viewport.scrollTo({ top: Math.max(0, targetY), behavior: 'smooth' });
    }
}

/**
 * KI-207-fix6: навигация по hits (по всему документу).
 */
function navigatePagesSearch(delta) {
    const viewer = state.pagesViewer;
    if (!viewer || !viewer.searchHits || viewer.searchHits.length === 0) return;

    const n = viewer.searchHits.length;
    let idx = (viewer.searchIndex ?? 0) + delta;
    idx = ((idx % n) + n) % n;

    viewer.searchIndex = idx;
    updatePagesSearchCounter(idx + 1, n);

    const hit = viewer.searchHits[idx];
    if (hit.pageNumber === viewer.currentPage) {
        // KI-207-fix7: убеждаемся, что text-layer для этой страницы уже загружен.
        // Если нет — markActiveHit no-op, но searchIndex уже переключён.
        // При загрузке loadPagesTextLayer сам активирует нужный hit.
        markActiveHit(hit);
    } else {
        gotoPagesViewer(hit.pageNumber);
    }
}

/**
 * KI-207-fix6: локальная подсветка при смене страницы (без перезапуска префетча).
 */
function applyLocalHighlights(layer, query, dto) {
    if (!layer || !query || !dto) return;

    clearPagesSearchHighlights(layer);
    const hits = findMatchesInLayer(dto, query);
    for (const h of hits) {
        highlightHit(layer, h, false);
    }
}

/**
 * KI-207-fix6: ждёт text-layer (для gotoPagesViewer из navigatePagesSearch).
 * Не используется прямо сейчас (после gotoPagesViewer подсветку применяет
 * loadPagesTextLayer через applyLocalHighlights). Оставлен на будущее.
 */
async function waitForTextLayer(pageNumber, timeoutMs = 5000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        const viewer = state.pagesViewer;
        if (!viewer || viewer.currentPage !== pageNumber) return false;
        if (viewer.textLayer && viewer.textLayerDto) return true;
        await new Promise(r => setTimeout(r, 100));
    }
    return false;
}

/**
 * KI-207: скроллит модалку к указанному совпадению + подсвечивает его как активное.
 */
function scrollToSearchHit(index) {
    const viewer = state.pagesViewer;
    if (!viewer?.searchHits) return;

    // Снимаем активную подсветку со всех.
    viewer.textLayer?.querySelectorAll(
        '.chat-pages-search-hit-active').forEach(el => {
            el.classList.remove('chat-pages-search-hit-active');
        });

    const span = viewer.searchHits[index];
    if (!span) return;

    const mark = span.querySelector('.chat-pages-search-hit');
    if (mark) mark.classList.add('chat-pages-search-hit-active');

    // Скролл внутри viewport (не в body).
    const viewport = document.querySelector('.chat-pages-viewport');
    if (viewport && span) {
        const spanRect = span.getBoundingClientRect();
        const viewRect = viewport.getBoundingClientRect();

        // Если span вне видимой области — центрируем.
        if (spanRect.top < viewRect.top || spanRect.bottom > viewRect.bottom) {
            const targetY = span.offsetTop * (viewer.zoom || 1)
                - viewport.clientHeight / 2
                + (span.offsetHeight * (viewer.zoom || 1)) / 2;
            viewport.scrollTo({ top: Math.max(0, targetY), behavior: 'smooth' });
        }
    }
}

/**
 * KI-207: обновляет счётчик поиска.
 */
function updatePagesSearchCounter(current, total, loading, fetched, totalPages) {
    const counter = document.getElementById('chat-pages-search-counter');
    const bar = document.getElementById('chat-pages-search-bar');
    if (!counter) return;

    // KI-207-fix4: режим префетча — показываем прогресс.
    if (loading) {
        counter.textContent = `Поиск… ${fetched || 0}/${totalPages || 0}`;
        return;
    }

    if (total === 0) {
        const noResults = bar?.dataset.labelNoresults || 'No matches';
        counter.textContent = noResults;
        return;
    }

    const template = bar?.dataset.labelCounter || '{0} / {1}';
    counter.textContent = template
        .replace('{0}', String(current))
        .replace('{1}', String(total));
}

// ============================================================
// v1.13.x (KI-208): экспорт страниц — ZIP, TXT, PDF.
// ============================================================

/**
 * KI-208: скачивает все PNG-страницы одним ZIP-архивом.
 * Endpoint: GET .../pages/zip (сервер собирает архив).
 */
function exportPagesZip() {
    const viewer = state.pagesViewer;
    if (!viewer || !state.activeChatId) return;

    const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/zip`;
    triggerDownload(url);
}

/**
 * KI-208: скачивает распознанный текст всех страниц одним .txt-файлом.
 * Endpoint: GET .../pages/all-text (сервер склеивает page-N.json).
 */
function exportPagesText() {
    const viewer = state.pagesViewer;
    if (!viewer || !state.activeChatId) return;

    const url = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/all-text`;
    triggerDownload(url);
}

/**
 * KI-208: собирает PDF-файл на клиенте через pdf-lib.
 * Накладывает PNG-страницы + невидимый текстовый слой (из page-N.json).
 * Шрифт — Roboto Regular (TTF, с кириллицей), регистрируется через fontkit.
 *
 * <para>
 * Ленивая загрузка:
 *   • /lib/pdf-lib/pdf-lib.min.js (~220 KB);
 *   • /lib/pdf-lib/fontkit.umd.min.js (~120 KB);
 *   • /lib/pdf-lib/Roboto-Regular.ttf (~170 KB).
 * Скачиваются один раз скриптом scripts/setup/download-pdf-lib.ps1.
 * </para>
 */
async function exportPagesPdf() {
    const viewer = state.pagesViewer;
    if (!viewer || !state.activeChatId) return;

    const modal = document.getElementById('chat-pages-modal');
    const progressTpl = modal?.dataset.labelExportPdfProgress || 'Preparing PDF…';
    const successTpl = modal?.dataset.labelExportPdfSuccess || 'PDF ready: {0} pages';
    const errorTpl = modal?.dataset.labelExportPdfError || 'PDF export failed: {0}';

    toast(progressTpl, 'info');

    try {
        // 1. Ленивая загрузка pdf-lib и fontkit.
        const PDFLib = await loadPdfLib();
        const fontkit = await loadFontkit();
        const { PDFDocument, rgb } = PDFLib;

        // 3. Создаём документ.
        const pdfDoc = await PDFDocument.create();
        pdfDoc.registerFontkit(fontkit);

        // KI-208-fix: пробуем загрузить TTF с кириллицей, но не падаем,
        // если его нет — используем встроенный Helvetica (Latin-1).
        // В этом случае text layer не сможет нарисовать кириллицу,
        // но PNG-страницы будут видны, PDF создастся.
        let customFont = null;
        let fontIsCyrillic = false;

        try {
            const fontResp = await fetch('/lib/pdf-lib/Roboto-Regular.ttf');
            if (fontResp.ok) {
                const fontBytes = await fontResp.arrayBuffer();
                customFont = await pdfDoc.embedFont(fontBytes, { subset: true });
                fontIsCyrillic = true;
                console.log('[chat] PDF export: используется Roboto-Regular.ttf');
            } else {
                console.warn('[chat] PDF export: Roboto-Regular.ttf не найден (HTTP ' +
                    fontResp.status + '), fallback на Helvetica (без кириллицы)');
            }
        } catch (ex) {
            console.warn('[chat] PDF export: ошибка загрузки TTF, fallback на Helvetica:', ex);
        }

        if (!customFont) {
            customFont = await pdfDoc.embedFont(PDFLib.StandardFonts.Helvetica);
        }

        // 4. Для каждой страницы: PNG + text layer.
        for (let p = 1; p <= viewer.totalPages; p++) {
            // 4.1. Загружаем PNG.
            // KI-208-fix3: проверка content-type. При 429 / 500 сервер
            // отдаёт JSON, и embedPng падает с невнятной ошибкой
            // «The input is not a PNG file!». Читаем тип + понятный throw.
            const pngUrl = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${p}`;
            const pngResp = await fetch(pngUrl);

            if (!pngResp.ok) {
                throw new Error(
                    `страница ${p}: HTTP ${pngResp.status}` +
                    (pngResp.status === 429 ? ' (превышен лимит запросов, попробуйте через минуту)' : ''));
            }

            const ct = pngResp.headers.get('content-type') || '';
            if (!ct.includes('image/png')) {
                throw new Error(
                    `страница ${p}: ожидался PNG, получен ${ct || 'unknown'}`);
            }

            const pngBuf = await pngResp.arrayBuffer();
            const pngImage = await pdfDoc.embedPng(pngBuf);

            const pageW = pngImage.width;
            const pageH = pngImage.height;
            const page = pdfDoc.addPage([pageW, pageH]);

            // 4.2. Накладываем PNG (страница = PNG 1:1 в pt).
            page.drawImage(pngImage, {
                x: 0, y: 0, width: pageW, height: pageH
            });

            // 4.3. Невидимый text layer (opacity: 0) — выделяемый.
            let layerDto = null;
            try {
                const ocrUrl = `/api/chat/${state.activeChatId}/attachments/${viewer.attachmentId}/pages/${p}/ocr`;
                const ocrResp = await fetch(ocrUrl);
                if (ocrResp.ok) {
                    const ct = ocrResp.headers.get('content-type') || '';
                    if (ct.includes('application/json')) {
                        const j = await ocrResp.json();
                        if (j && j.success !== false) layerDto = j;
                    }
                }
            } catch { /* нет JSON — пропускаем */ }

            if (layerDto) {
                const rawWords = layerDto.words ?? layerDto.Words ?? [];
                for (const w of rawWords) {
                    const text = w.text ?? w.Text;
                    const x = w.x ?? w.X ?? 0;
                    const y = w.y ?? w.Y ?? 0;
                    const h = w.h ?? w.H ?? 10;
                    if (!text || h <= 0) continue;

                    // pdf-lib origin — bottom-left. Наш bbox — top-left.
                    // pdfY_baseline ≈ pngH - y - h*0.9 (baseline чуть выше bottom).
                    const pdfY = pageH - y - h * 0.9;
                    const fontSize = h * 0.9;
                    if (fontSize < 1) continue;

                    try {
                        page.drawText(String(text), {
                            x: x,
                            y: pdfY,
                            size: fontSize,
                            font: customFont,
                            color: rgb(0, 0, 0),
                            opacity: 0,
                        });
                    } catch (ex) {
                        // KI-208-fix: с Helvetica (Latin-1) кириллица не
                        // отрисуется — drawText бросит. Тихо пропускаем.
                        console.debug('[pdf] drawText skip:', text, ex);
                    }
                }
            }
        }

        // 5. Сохраняем и скачиваем.
        const pdfBytes = await pdfDoc.save();
        const blob = new Blob([pdfBytes], { type: 'application/pdf' });
        const url = URL.createObjectURL(blob);

        const baseName = (viewer.fileName || 'document').replace(/\.[^.]+$/, '');
        const a = document.createElement('a');
        a.href = url;
        a.download = `${baseName}.pdf`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);

        toast(successTpl.replace('{0}', String(viewer.totalPages)), 'success');
    } catch (ex) {
        console.error('[chat] PDF export failed:', ex);
        toast(errorTpl.replace('{0}', ex.message || 'unknown'), 'error');
    }
}

/**
 * KI-208: скачивает файл через <a download>.
 * Content-Disposition: attachment — уже задан на сервере.
 */
function triggerDownload(url) {
    const a = document.createElement('a');
    a.href = url;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

/**
 * KI-208: ленивая загрузка pdf-lib (UMD).
 * @returns {Promise<object>} window.PDFLib
 */
async function loadPdfLib() {
    if (window.PDFLib) return window.PDFLib;

    await loadScriptOnce('/lib/pdf-lib/pdf-lib.min.js', 'pdf-lib');
    if (!window.PDFLib) throw new Error('pdf-lib: window.PDFLib не определён');
    return window.PDFLib;
}

/**
 * KI-208: ленивая загрузка @pdf-lib/fontkit (UMD).
 * @returns {Promise<object>} window.fontkit
 */
async function loadFontkit() {
    if (window.fontkit) return window.fontkit;

    await loadScriptOnce('/lib/pdf-lib/fontkit.umd.min.js', 'fontkit');
    if (!window.fontkit) throw new Error('fontkit: window.fontkit не определён');
    return window.fontkit;
}

/**
 * KI-208: idempotent подгрузка <script>. Если уже загружен — resolve.
 */
const _loadedScripts = new Map();
function loadScriptOnce(src, key) {
    if (_loadedScripts.has(key)) return _loadedScripts.get(key);

    const p = new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = src;
        s.async = true;
        s.onload = () => resolve();
        s.onerror = () => reject(new Error(`Не удалось загрузить ${src}`));
        document.head.appendChild(s);
    });

    _loadedScripts.set(key, p);
    return p;
}

/**
 * KI-232: idempotent подгрузка <link rel="stylesheet">.
 * @param {string} href
 * @param {string} key
 * @returns {Promise<void>}
 */
const _loadedStyles = new Map();
function loadStyleOnce(href, key) {
    if (_loadedStyles.has(key)) return _loadedStyles.get(key);

    const p = new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;
        link.onload = () => resolve();
        link.onerror = () => reject(new Error(`Не удалось загрузить CSS ${href}`));
        document.head.appendChild(link);
    });

    _loadedStyles.set(key, p);
    return p;
}

// ============================================================
// v1.13.x (KI-229): lightbox для скриншотов от vision_agent.
// ============================================================

/** Состояние lightbox. null — закрыт. */
state.lightbox = null;

/**
 * KI-229: открывает lightbox с указанным изображением.
 *
 * @param {string} src — URL или data-URL картинки.
 * @param {object} [opts]
 * @param {string} [opts.title] — заголовок (обычно relative path).
 * @param {number} [opts.sizeBytes] — размер в байтах (для tooltip).
 */
function openLightbox(src, opts = {}) {
    if (!src) return;

    const modal = document.getElementById('chat-lightbox');
    if (!modal) return;

    const img = modal.querySelector('.chat-lightbox-img');
    if (!img) return;

    // Сброс состояния.
    state.lightbox = {
        src,
        title: opts.title || '',
        sizeBytes: opts.sizeBytes || 0,
        zoom: 1.0,
        panX: 0,
        panY: 0
    };

    // Заголовок.
    const titleEl = modal.querySelector('.chat-lightbox-title');
    if (titleEl) titleEl.textContent = state.lightbox.title || '';

    // Картинка: сброс размеров (будут установлены после load).
    img.removeAttribute('style');
    img.alt = state.lightbox.title || 'Screenshot';

    // Обработчик load — устанавливает оригинальный размер + snap.
    const onLoad = () => {
        if (!state.lightbox) return;
        // KI-229-fix2: при первом показе snap — если картинка больше viewport'а,
        // видим верхний-левый угол, а не центр с обрезкой.
        applyLightboxZoom({ snap: true });
    };
    img.onload = onLoad;
    img.onerror = () => {
        // Не удалось загрузить (404) — показываем сообщение.
        const viewport = modal.querySelector('.chat-lightbox-viewport');
        if (viewport) {
            viewport.innerHTML = `<div class="chat-lightbox-error">
                Не удалось загрузить изображение (${escapeHtml(src)}).</div>`;
        }
    };

    img.src = src;

    // Показать модалку.
    modal.hidden = false;
    document.body.classList.add('chat-lightbox-open');

    // Обновить label zoom.
    updateLightboxZoomLabel();

    // Фокус на модалку (для Escape).
    modal.focus();
}

/**
 * KI-229: закрывает lightbox.
 */
function closeLightbox() {
    const modal = document.getElementById('chat-lightbox');
    if (!modal) return;

    modal.hidden = true;
    document.body.classList.remove('chat-lightbox-open');
    state.lightbox = null;

    // Сброс viewport (в случае ошибки загрузки).
    const viewport = modal.querySelector('.chat-lightbox-viewport');
    if (viewport) {
        viewport.innerHTML = `
            <img class="chat-lightbox-img" alt="" />
        `;
    }

    // Сброс listener'ов мыши.
    stopLightboxDrag();
}

/**
 * KI-229: применяет zoom/pan к изображению.
 * Устанавливает width/height в px (natural × zoom) — overflow:auto
 * на viewport'е даёт панорамирование через нативный скролл.
 */
function applyLightboxZoom(opts = {}) {
    const lb = state.lightbox;
    if (!lb) return;

    const modal = document.getElementById('chat-lightbox');
    if (!modal) return;

    const img = modal.querySelector('.chat-lightbox-img');
    if (!img || !img.naturalWidth) return;

    const w = Math.round(img.naturalWidth * lb.zoom);
    const h = Math.round(img.naturalHeight * lb.zoom);

    img.style.width = `${w}px`;
    img.style.height = `${h}px`;

    const viewport = modal.querySelector('.chat-lightbox-viewport');
    if (viewport) {
        const vw = viewport.clientWidth;
        const vh = viewport.clientHeight;

        // KI-229-fix2: snap к верхнему-левому углу — если картинка больше
        // viewport'а по оси. Иначе (картинка меньше) — центр (0).
        // Применяется при первом показе / fit-width / fit-page / reset 100%.
        // При zoom-in/out (opts.snap не задан) — сохраняем pan, только clamp.
        if (opts.snap) {
            lb.panX = w > vw ? (w - vw) / 2 : 0;
            lb.panY = h > vh ? (h - vh) / 2 : 0;
        } else {
            // Clamp: минимум 40px картинки остаётся видимым — нельзя утащить полностью.
            const maxPanX = Math.max(0, (w + vw) / 2 - 40);
            const maxPanY = Math.max(0, (h + vh) / 2 - 40);
            lb.panX = Math.max(-maxPanX, Math.min(maxPanX, lb.panX || 0));
            lb.panY = Math.max(-maxPanY, Math.min(maxPanY, lb.panY || 0));
        }
    }

    img.style.transform = `translate(${lb.panX || 0}px, ${lb.panY || 0}px)`;
    img.style.transformOrigin = 'center center';
}

/**
 * KI-229: устанавливает zoom (clamp [0.25, 8.0]).
 */
function setLightboxZoom(value, opts = {}) {
    const lb = state.lightbox;
    if (!lb) return;

    let v = Number(value);
    if (!Number.isFinite(v) || v <= 0) v = 1.0;
    v = Math.max(0.25, Math.min(8.0, v));
    lb.zoom = v;

    applyLightboxZoom(opts);
    updateLightboxZoomLabel();
}

/**
 * KI-229: fit-width / fit-page.
 */
function fitLightbox(kind) {
    const lb = state.lightbox;
    if (!lb) return;

    const modal = document.getElementById('chat-lightbox');
    if (!modal) return;

    const img = modal.querySelector('.chat-lightbox-img');
    const viewport = modal.querySelector('.chat-lightbox-viewport');
    if (!img || !img.naturalWidth || !viewport) return;

    const cs = window.getComputedStyle(viewport);
    const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
    const padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
    const availW = Math.max(1, viewport.clientWidth - padX - 4);
    const availH = Math.max(1, viewport.clientHeight - padY - 4);

    // KI-229-fix2: fit — snap (при большом размере — верхний-левый угол виден,
    // не центр с обрезкой сверху/снизу).
    if (kind === 'width') {
        setLightboxZoom(availW / img.naturalWidth, { snap: true });
    } else if (kind === 'page') {
        const zW = availW / img.naturalWidth;
        const zH = availH / img.naturalHeight;
        setLightboxZoom(Math.min(zW, zH), { snap: true });
    }
}

/**
 * KI-229: обновляет label зума в toolbar.
 */
function updateLightboxZoomLabel() {
    const lb = state.lightbox;
    const modal = document.getElementById('chat-lightbox');
    const label = modal?.querySelector('.chat-lightbox-zoom-value');
    if (!label) return;

    const z = lb?.zoom ?? 1.0;
    label.textContent = `${Math.round(z * 100)}%`;
}

/**
 * KI-229: drag-to-pan.
 */
let _lightboxDrag = null;

function startLightboxDrag(e) {
    if (!state.lightbox) return;
    if (e.button !== 0) return;   // только левая кнопка

    _lightboxDrag = {
        startX: e.clientX,
        startY: e.clientY,
        startPanX: state.lightbox.panX || 0,
        startPanY: state.lightbox.panY || 0
    };

    e.currentTarget.classList.add('chat-lightbox-viewport-dragging');
    e.preventDefault();
}

function moveLightboxDrag(e) {
    if (!_lightboxDrag || !state.lightbox) return;

    const dx = e.clientX - _lightboxDrag.startX;
    const dy = e.clientY - _lightboxDrag.startY;

    state.lightbox.panX = _lightboxDrag.startPanX + dx;
    state.lightbox.panY = _lightboxDrag.startPanY + dy;

    applyLightboxZoom();
}

function stopLightboxDrag() {
    if (!_lightboxDrag) return;
    document.querySelector('.chat-lightbox-viewport')
        ?.classList.remove('chat-lightbox-viewport-dragging');
    _lightboxDrag = null;
}

/**
 * KI-229: copy path из lightbox в clipboard.
 */
async function copyLightboxPath() {
    const lb = state.lightbox;
    if (!lb || !lb.title) return;

    try {
        await copyToClipboard(lb.title);
        const modal = document.getElementById('chat-lightbox');
        const msg = modal?.dataset.labelLightboxCopySuccess || 'Path copied';
        toast(msg, 'success');
    } catch {
        toast('Не удалось скопировать', 'error');
    }
}

/**
 * KI-229: скачать картинку.
 * Для URL-src — просто <a download>. Для data-URL — конвертируем в blob.
 */
function downloadLightboxImage() {
    const lb = state.lightbox;
    if (!lb) return;

    const a = document.createElement('a');
    a.href = lb.src;
    a.download = extractFileName(lb.title) || 'screenshot.png';
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

/**
 * KI-229: извлекает последний сегмент из path.
 */
function extractFileName(path) {
    if (!path) return '';
    const parts = String(path).split(/[/\\]/);
    return parts[parts.length - 1] || '';
}

/**
 * KI-229: навешивает обработчики на модалку lightbox. Вызывается один раз
 * из initChatPage().
 */
function initLightboxHandlers() {
    const modal = document.getElementById('chat-lightbox');
    if (!modal) return;

    // Закрытие: ✕ / backdrop.
    modal.querySelectorAll('[data-lightbox="close"]').forEach(el => {
        el.addEventListener('click', closeLightbox);
    });

    // Zoom: − / + — сохраняют pan (только clamp).
    modal.querySelector('[data-lightbox="zoom-out"]')
        ?.addEventListener('click', () => {
            const z = state.lightbox?.zoom ?? 1.0;
            setLightboxZoom(z - 0.25);
        });
    modal.querySelector('[data-lightbox="zoom-in"]')
        ?.addEventListener('click', () => {
            const z = state.lightbox?.zoom ?? 1.0;
            setLightboxZoom(z + 0.25);
        });
    // Reset 100% — snap (верхний-левый угол картинки, если она больше viewport'а).
    modal.querySelector('[data-lightbox="zoom-reset"]')
        ?.addEventListener('click', () => setLightboxZoom(1.0, { snap: true }));

    // Fit.
    modal.querySelector('[data-lightbox="fit-width"]')
        ?.addEventListener('click', () => fitLightbox('width'));
    modal.querySelector('[data-lightbox="fit-page"]')
        ?.addEventListener('click', () => fitLightbox('page'));

    // Actions.
    modal.querySelector('[data-lightbox="download"]')
        ?.addEventListener('click', downloadLightboxImage);
    modal.querySelector('[data-lightbox="copy-path"]')
        ?.addEventListener('click', copyLightboxPath);

    // Drag-to-pan на viewport.
    const viewport = modal.querySelector('.chat-lightbox-viewport');
    if (viewport) {
        viewport.addEventListener('mousedown', startLightboxDrag);
        window.addEventListener('mousemove', moveLightboxDrag);
        window.addEventListener('mouseup', stopLightboxDrag);

        // KI-229-fix: блокируем нативный HTML5 drag у <img> — иначе браузер
        // запускает «drag-and-drop картинки», mousemove перестаёт приходить
        // и drag-to-pan не работает.
        viewport.addEventListener('dragstart', (e) => e.preventDefault());
    }

    // Wheel-zoom — snap (при увеличении зума сразу видно верх картинки).
    viewport?.addEventListener('wheel', (e) => {
        e.preventDefault();
        const delta = e.deltaY < 0 ? +0.15 : -0.15;
        const z = state.lightbox?.zoom ?? 1.0;
        setLightboxZoom(z + delta, { snap: true });
    }, { passive: false });

    // Клавиатура: только когда lightbox открыт.
    window.addEventListener('keydown', (e) => {
        if (!state.lightbox) return;

        switch (e.key) {
            case 'Escape':
                e.preventDefault();
                e.stopPropagation();
                closeLightbox();
                return;
            case '+':
            case '=':
                e.preventDefault();
                setLightboxZoom((state.lightbox?.zoom ?? 1.0) + 0.25, { snap: true });
                return;
            case '-':
            case '_':
                e.preventDefault();
                setLightboxZoom((state.lightbox?.zoom ?? 1.0) - 0.25, { snap: true });
                return;
            case '0':
                e.preventDefault();
                setLightboxZoom(1.0, { snap: true });
                return;
            case 'ArrowLeft':
                state.lightbox.panX = (state.lightbox.panX || 0) - 60;
                applyLightboxZoom();
                e.preventDefault();
                return;
            case 'ArrowRight':
                state.lightbox.panX = (state.lightbox.panX || 0) + 60;
                applyLightboxZoom();
                e.preventDefault();
                return;
            case 'ArrowUp':
                state.lightbox.panY = (state.lightbox.panY || 0) - 60;
                applyLightboxZoom();
                e.preventDefault();
                return;
            case 'ArrowDown':
                state.lightbox.panY = (state.lightbox.panY || 0) + 60;
                applyLightboxZoom();
                e.preventDefault();
                return;
        }
    });
}

/**
 * KI-229: делегированный click — открыть lightbox из inline <img>
 * (live-режим) или placeholder-кнопки (F5-fallback).
 */
async function onLightboxTriggerClick(e) {
    // (A) inline <img data-lightbox-src>
    const img = e.target.closest('img[data-lightbox-src]');
    if (img) {
        e.preventDefault();
        e.stopPropagation();
        openLightbox(img.dataset.lightboxSrc, {
            title: img.dataset.lightboxTitle || ''
        });
        return;
    }

    // (B) placeholder-кнопка data-open-screenshot
    const btn = e.target.closest('[data-open-screenshot]');
    if (btn) {
        e.preventDefault();
        e.stopPropagation();

        const taskId = btn.dataset.taskId;
        const fileName = btn.dataset.fileName;
        if (!taskId || !fileName || !state.activeChatId) return;

        const url = `/api/chat/${state.activeChatId}/screenshots/${encodeURIComponent(taskId)}/${encodeURIComponent(fileName)}`;
        const title = `screenshots/${taskId}/${fileName}`;
        openLightbox(url, { title });
        return;
    }
}
