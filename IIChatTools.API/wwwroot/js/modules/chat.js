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

    const contentHtml = msg.content
        ? `<div class="chat-message-content">${escapeHtml(msg.content).replace(/\n/g, '<br>')}</div>`
        : '';

    return `
        <div class="chat-message ${isUser ? 'user' : 'assistant'}">
            <div class="chat-message-avatar">${avatar}</div>
            <div class="chat-message-body">
                <div class="chat-message-meta">${roleLabel} · ${escapeHtml(formatTime(msg.createdAt))}</div>
                ${toolCallsHtml}
                ${contentHtml}
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
                <div class="chat-message-content">${escapeHtml(text).replace(/\n/g, '<br>')}</div>
            </div>
        </div>`;
    container.insertAdjacentHTML('beforeend', html);
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