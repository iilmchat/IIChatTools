/**
 * Модуль страницы /chat: sidebar + лента сообщений + SSE-стриминг.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.3
 *
 * Шаг 2.0.3 — SSE: отправка сообщений, стриминг ответа, обработка tool_call/tool_result.
 * Интеграция approval-модалки — Шаг 2.0.4.
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { requestChatApproval } from './approvals.js';

// ============ Состояние ============

const state = {
    chats: [],
    models: [],
    defaultModel: null,
    activeChatId: null,
    activeChat: null,
    isStreaming: false,   // блокировка отправки во время стрима
    autoScroll: true,     // включён, пока пользователь у нижнего края
};

// ============ Инициализация ============

/**
 * Инициализирует страницу чата.
 */
export async function initChatPage() {
    bindEvents();

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

    console.log('[chat] Шаг 2.0.3 готов: SSE-стриминг');
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
    }

    if (btnSend) {
        btnSend.addEventListener('click', sendMessage);
    }

    // === Скроллинг (Фаза 2.0.5b) ===

    // Слушатель скролла на ленте сообщений — отслеживает позицию пользователя
    const messagesEl = document.getElementById('chat-messages');
    if (messagesEl) {
        messagesEl.addEventListener('scroll', onMessagesScroll);
        // Делегированный обработчик кнопок действий с сообщением (Copy)
        messagesEl.addEventListener('click', onMessageActionClick);
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
    if (listEl) listEl.innerHTML = '<div class="text-muted text-center p-3 small">Загрузка…</div>';

    const res = await apiGet('/api/chats');
    if (!res.success) {
        toast('Не удалось загрузить чаты', 'error');
        return;
    }

    state.chats = res.data || [];
    renderChatList();
}

function renderChatList() {
    const listEl = document.getElementById('chat-list');
    if (!listEl) return;

    if (state.chats.length === 0) {
        listEl.innerHTML = '<div class="text-muted text-center p-3 small">Нет чатов. Создайте первый.</div>';
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
                    <div class="chat-list-item-title">${title}</div>
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
            // Игнорируем клики по кнопкам действий
            if (e.target.closest('[data-delete-id]') || e.target.closest('[data-edit-id]')) return;
            e.preventDefault();
            selectChat(parseInt(el.dataset.chatId, 10));
        });
    });

    listEl.querySelectorAll('[data-delete-id]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            deleteChat(parseInt(btn.dataset.deleteId, 10));
        });
    });

    listEl.querySelectorAll('[data-edit-id]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            renameChat(parseInt(btn.dataset.editId, 10));
        });
    });
}

async function createChat() {
    if (!state.defaultModel) {
        toast('Нет доступных моделей LM Studio', 'error');
        return;
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
    const res = await apiGet(`/api/chats/${chatId}`);
    if (!res.success) {
        toast(res.message || 'Не удалось загрузить чат', 'error');
        return;
    }

    state.activeChatId = chatId;
    state.activeChat = res.data;
    state.autoScroll = true;   // при переключении чата — прилипаем к низу

    updateUrl(chatId);
    renderChatList();
    renderChatHeader(res.data);
    renderMessages(res.data.messages || []);
    enableInput(true);
    updateScrollDownButton();
}

async function deleteChat(chatId) {
    if (!confirm('Вы уверены, что хотите удалить чат?')) return;

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

// ============ Переименование чата (Фаза 2.0.5a) ============

/**
 * Переименовывает чат через prompt(). Пустое имя — отклоняется.
 * @param {number} chatId Идентификатор чата
 */
async function renameChat(chatId) {
    const chat = state.chats.find(c => c.id === chatId);
    if (!chat) return;

    const currentTitle = chat.title || '';
    const raw = prompt('Новое имя чата:', currentTitle);
    if (raw === null) return;   // отмена

    const newTitle = raw.trim();
    if (!newTitle) {
        toast('Имя не может быть пустым', 'warning');
        return;
    }
    if (newTitle === currentTitle) return;   // без изменений

    try {
        const res = await fetch(`/api/chats/${chatId}`, {
            method: 'PATCH',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: newTitle }),
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка переименования', 'error');
            return;
        }

        // Локальное обновление
        chat.title = newTitle;
        renderChatList();

        // Синхронизация header, если это активный чат
        if (state.activeChatId === chatId && state.activeChat) {
            state.activeChat.title = newTitle;
            renderChatHeader(state.activeChat);
        }

        toast('Чат переименован', 'success');
    } catch (ex) {
        toast(ex.message || 'Ошибка переименования', 'error');
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

    // 1. Сразу добавляем user-пузырь (оптимистично) и очищаем input
    appendUserMessage(message);
    if (input) { input.value = ''; autoResizeTextarea(input); }
    enableInput(false);
    state.isStreaming = true;

    // 2. Создаём пузырь ассистента с индикатором «Печатает…»
    const assistantBubble = appendAssistantBubble();
    showTypingIndicator(assistantBubble, true);

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
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        await readSseStream(response, assistantBubble);
    } catch (ex) {
        showTypingIndicator(assistantBubble, false);
        appendAssistantError(assistantBubble, ex.message || 'Ошибка соединения');
        toast(ex.message || 'Ошибка отправки сообщения', 'error');
    } finally {
        state.isStreaming = false;
        enableInput(true);
        input?.focus();
        updateSidebarTimeLocally();
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
            // userMessageId известен, но user-пузырь уже добавлен — ничего не делаем
            break;

        case 'delta':
            showTypingIndicator(assistantBubble, false);
            appendDelta(assistantBubble, data.text || '');
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

        case 'done':
            showTypingIndicator(assistantBubble, false);
            finalizeAssistantBubble(assistantBubble, data);
            break;

        case 'error':
            showTypingIndicator(assistantBubble, false);
            appendAssistantError(assistantBubble, data.message || 'Неизвестная ошибка');
            break;

        default:
            console.debug('[chat] Неизвестное SSE-событие:', name, data);
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

// ============ Рендер сообщений ============

function renderMessages(messages) {
    const container = document.getElementById('chat-messages');
    if (!container) return;

    if (!messages || messages.length === 0) {
        container.innerHTML = `
            <div class="chat-empty-state">
                <div class="text-muted text-center">
                    <div style="font-size: 3rem;">💬</div>
                    <p class="mb-0">История пуста. Начните диалог.</p>
                </div>
            </div>`;
        return;
    }

    container.innerHTML = messages.map(renderMessage).join('');
    enhanceCodeBlocks(container);
    scrollToBottom(true);
}

function renderMessage(msg) {
    if (msg.role === 'system') return '';
    if (msg.role === 'tool') return renderToolResultBlock(msg);

    const isUser = msg.role === 'user';
    const avatar = isUser ? '👤' : '🤖';
    const roleLabel = isUser ? 'Вы' : 'Ассистент';

    let toolCallsHtml = '';
    if (msg.toolCallsJson) {
        toolCallsHtml = renderToolCallsFromJson(msg.toolCallsJson);
    }

    // User-сообщения — plain text (пользователь пишет текст, а не Markdown).
    // Assistant — полноценный Markdown-рендер.
    const contentHtml = msg.content
        ? (isUser
            ? `<div class="chat-message-content">${renderUserContent(msg.content)}</div>`
            : `<div class="chat-message-content chat-markdown">${renderMarkdown(msg.content)}</div>`)
        : '';

    // Кнопка Copy — только если есть текстовый контент
    const actionsHtml = msg.content
        ? renderMessageActions(msg.content)
        : '';

    return `
        <div class="chat-message ${isUser ? 'user' : 'assistant'}">
            <div class="chat-message-avatar">${avatar}</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">${roleLabel} · ${escapeHtml(formatTime(msg.createdAt))}</div>
                ${toolCallsHtml}
                ${contentHtml}
                ${actionsHtml}
            </div>
        </div>`;
}

function renderToolCallsFromJson(toolCallsJson) {
    let calls = [];
    try { calls = JSON.parse(toolCallsJson); } catch { return ''; }
    if (!Array.isArray(calls) || calls.length === 0) return '';

    return calls.map(call => {
        const name = call?.function?.name || 'unknown';
        return `
            <div class="chat-tool-block">
                <span class="chat-tool-icon">🛠</span>
                <span class="chat-tool-name">${escapeHtml(name)}</span>
            </div>`;
    }).join('');
}

function renderToolResultBlock(msg) {
    const toolName = msg.toolName || 'tool';
    const preview = (msg.content || '').length > 120
        ? msg.content.substring(0, 120) + '…'
        : (msg.content || '');

    return `
        <div class="chat-tool-result">
            <span class="chat-tool-icon">🛠</span>
            <span class="chat-tool-name">${escapeHtml(toolName)}</span>
            <span class="chat-tool-preview">${escapeHtml(preview)}</span>
        </div>`;
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
    const html = `
        <div class="chat-message user">
            <div class="chat-message-avatar">👤</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">Вы · ${escapeHtml(formatTime(now))}</div>
                <div class="chat-message-content">${renderUserContent(text)}</div>
                ${renderMessageActions(text)}
            </div>
        </div>`;
    container.insertAdjacentHTML('beforeend', html);
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
    const html = `
        <div class="chat-message assistant chat-message-streaming">
            <div class="chat-message-avatar">🤖</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">Ассистент · ${escapeHtml(formatTime(now))}</div>
                <div class="chat-message-tools"></div>
                <div class="chat-message-content" data-stream-content></div>
                <div class="chat-typing-indicator" data-typing hidden>
                    <span></span><span></span><span></span>
                </div>
                ${renderMessageActions('')}
            </div>
        </div>`;

    container.insertAdjacentHTML('beforeend', html);
    scrollToBottom();
    return container.lastElementChild;
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
            <span class="chat-tool-icon">🛠</span>
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

    const ok = result.success;
    const preview = (result.message || '').substring(0, 100);

    toolsEl.insertAdjacentHTML('beforeend', `
        <div class="chat-tool-result ${ok ? 'success' : 'error'}">
            <span class="chat-tool-icon">${ok ? '✅' : '❌'}</span>
            <span class="chat-tool-name">${escapeHtml(result.name || 'tool')}</span>
            <span class="chat-tool-preview">${escapeHtml(preview)}</span>
        </div>`);
    scrollToBottom();
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

            // Синхронизация текста для Copy
            const actionsEl = bubble.querySelector('.chat-message-actions');
            if (actionsEl) actionsEl.dataset.copyText = raw;
        }
    }
}

/**
 * Показывает ошибку в пузыре ассистента.
 * @param {HTMLElement} bubble
 * @param {string} message
 */
function appendAssistantError(bubble, message) {
    if (!bubble) return;
    const contentEl = bubble.querySelector('[data-stream-content]');
    if (!contentEl) return;

    contentEl.classList.add('chat-message-error');
    contentEl.innerHTML = `⚠️ ${escapeHtml(message)}`;
    scrollToBottom();
}

// ============ Header ============

function renderChatHeader(chat) {
    const headerEl = document.getElementById('chat-header');
    const titleEl = document.getElementById('chat-title');
    const modelEl = document.getElementById('chat-model');

    if (headerEl) headerEl.classList.remove('d-none');
    if (titleEl) titleEl.textContent = chat.title || 'Без названия';
    if (modelEl) modelEl.textContent = chat.model || '—';
}

function showEmptyState() {
    const headerEl = document.getElementById('chat-header');
    if (headerEl) headerEl.classList.add('d-none');

    const container = document.getElementById('chat-messages');
    if (container) {
        container.innerHTML = `
            <div class="chat-empty-state">
                <div class="text-muted text-center">
                    <div style="font-size: 3rem;">💬</div>
                    <p class="mb-0">Выберите чат или создайте новый</p>
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
    const btn = document.getElementById('btn-send');
    if (input) input.disabled = !enabled;
    if (btn) btn.disabled = !enabled;
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
        // breaks: true — одиночный \n превращается в <br> (удобно для чата).
        const rawHtml = window.marked.parse(text, { breaks: true, gfm: true });
        return window.DOMPurify.sanitize(rawHtml, {
            ALLOWED_TAGS: [
                'p', 'br', 'strong', 'em', 'del', 'code', 'pre',
                'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
                'ul', 'ol', 'li', 'blockquote', 'hr',
                'a', 'img', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
                'span', 'div'
            ],
            ALLOWED_ATTR: ['href', 'title', 'alt', 'src', 'class', 'target', 'rel'],
        });
    } catch (ex) {
        console.warn('[chat] Markdown parse error:', ex);
        return escapeHtml(text).replace(/\n/g, '<br>');
    }
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
    };
    return map[(lang || '').toLowerCase()] || 'txt';
}

// ============ Действия с сообщениями (Фаза 2.1.1 — Copy) ============

/**
 * Рендерит блок действий сообщения (Copy).
 * @param {string} text Текст для копирования (пустой у streaming-bubble)
 * @returns {string} HTML
 */
function renderMessageActions(text) {
    const attr = escapeAttr(text || '');
    return `
        <div class="chat-message-actions" data-copy-text="${attr}">
            <button type="button"
                    class="chat-message-action"
                    data-action="copy"
                    title="Скопировать"
                    aria-label="Скопировать сообщение">📋</button>
        </div>`;
}

/**
 * Делегированный обработчик клика по кнопкам действий в ленте сообщений.
 * @param {MouseEvent} e
 */
async function onMessageActionClick(e) {
    const btn = e.target.closest('[data-action="copy"]');
    if (!btn) return;

    e.preventDefault();
    e.stopPropagation();

    const actionsEl = btn.closest('.chat-message-actions');
    const text = actionsEl?.dataset.copyText || '';

    if (!text) {
        toast('Нечего копировать', 'warning');
        return;
    }

    try {
        await copyToClipboard(text);
        flashCopied(btn);
    } catch (ex) {
        console.error('[chat] Копирование не удалось:', ex);
        toast('Не удалось скопировать', 'error');
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
    const chat = state.chats.find(c => c.id === state.activeChatId);
    if (chat) {
        chat.updatedAt = new Date().toISOString();
        renderChatList();
    }
}