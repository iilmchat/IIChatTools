/**
 * Модуль страницы /chat: sidebar (список чатов) + лента сообщений.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.3
 *
 * Шаг 2.0.2b — sidebar: загрузка, создание, удаление, переключение чатов,
 * отрисовка истории, синхронизация активного чата с URL (?chatId=N).
 *
 * Логика SSE-стриминга и отправки сообщений — Шаг 2.0.3.
 * Интеграция approval-модалки — Шаг 2.0.4.
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';

// ============ Состояние ============

const state = {
    chats: [],          // ChatListItemDto[]
    models: [],         // ModelInfoDto[]
    defaultModel: null, // строка
    activeChatId: null, // number | null
    activeChat: null,   // ChatDetailDto | null
};

// ============ Инициализация ============

/**
 * Инициализирует страницу чата.
 */
export async function initChatPage() {
    bindEvents();

    await loadModels();
    await loadChats();

    // Восстановить чат из URL или выбрать первый
    const urlChatId = readUrlChatId();
    if (urlChatId && state.chats.some(c => c.id === urlChatId)) {
        await selectChat(urlChatId);
    } else if (state.chats.length > 0) {
        await selectChat(state.chats[0].id);
    } else {
        showEmptyState();
    }

    console.log('[chat] Шаг 2.0.2b готов: sidebar загружен');
}

// ============ Привязка событий ============

function bindEvents() {
    document.getElementById('btn-new-chat')
        ?.addEventListener('click', createChat);

    document.getElementById('btn-delete-chat')
        ?.addEventListener('click', () => {
            if (state.activeChatId) {
                deleteChat(state.activeChatId);
            }
        });
}

// ============ Загрузка моделей ============

/**
 * Загружает список моделей LM Studio (кэшируется).
 */
async function loadModels() {
    const res = await apiGet('/api/models');
    if (!res.success) {
        console.warn('[chat] Не удалось загрузить модели:', res.message);
        return;
    }

    state.models = res.data || [];
    state.defaultModel = state.models.find(m => m.isDefault)?.id
        || state.models[0]?.id
        || null;
}

// ============ Загрузка списка чатов ============

/**
 * Загружает список чатов и рендерит sidebar.
 */
async function loadChats() {
    const listEl = document.getElementById('chat-list');
    if (listEl) {
        listEl.innerHTML = '<div class="text-muted text-center p-3 small">Загрузка…</div>';
    }

    const res = await apiGet('/api/chats');
    if (!res.success) {
        toast('Не удалось загрузить чаты', 'error');
        if (listEl) {
            listEl.innerHTML = `<div class="text-danger text-center p-3 small">${escapeHtml(res.message || 'Ошибка')}</div>`;
        }
        return;
    }

    state.chats = res.data || [];
    renderChatList();
}

// ============ Рендер sidebar ============

/**
 * Отрисовывает список чатов в sidebar.
 * Каждый элемент — div с role="button" (позволяет вкладывать кнопку удаления).
 * Кнопка удаления появляется при hover / на активном чате.
 */
function renderChatList() {
    const listEl = document.getElementById('chat-list');
    if (!listEl) return;

    if (state.chats.length === 0) {
        listEl.innerHTML = `
            <div class="text-muted text-center p-3 small">
                Нет чатов. Создайте первый.
            </div>`;
        return;
    }

    listEl.innerHTML = state.chats.map(chat => {
        const isActive = chat.id === state.activeChatId;
        const title = escapeHtml(chat.title || 'Без названия');
        return `
            <div class="chat-list-item ${isActive ? 'active' : ''}"
                 data-chat-id="${chat.id}"
                 role="button"
                 tabindex="0">
                <div class="chat-list-item-body">
                    <div class="chat-list-item-title">${title}</div>
                    <div class="chat-list-item-meta">${escapeHtml(formatRelativeDate(chat.updatedAt))}</div>
                </div>
                <button type="button"
                        class="chat-list-item-delete"
                        data-delete-id="${chat.id}"
                        title="Удалить чат"
                        aria-label="Удалить чат">🗑</button>
            </div>`;
    }).join('');

    // Клик по элементу → выбрать чат (кроме клика по кнопке удаления)
    listEl.querySelectorAll('[data-chat-id]').forEach(el => {
        el.addEventListener('click', (e) => {
            if (e.target.closest('[data-delete-id]')) return;
            e.preventDefault();
            const id = parseInt(el.dataset.chatId, 10);
            selectChat(id);
        });
    });

    // Клик по кнопке удаления → удалить чат
    listEl.querySelectorAll('[data-delete-id]').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            const id = parseInt(btn.dataset.deleteId, 10);
            deleteChat(id);
        });
    });
}

// ============ Создание чата ============

/**
 * Создаёт новый чат и делает его активным.
 */
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
            title: 'Новый чат',
        });

        if (!res.success) {
            toast(res.message || 'Ошибка создания чата', 'error');
            return;
        }

        const newChat = res.data;
        // Добавляем в начало списка (сортировка UpdatedAt desc)
        state.chats.unshift(newChat);
        renderChatList();

        toast('Чат создан', 'success');
        await selectChat(newChat.id);
    } finally {
        if (btn) btn.disabled = false;
    }
}

// ============ Выбор чата ============

/**
 * Загружает детали чата и рендерит историю сообщений.
 * @param {number} chatId Идентификатор чата
 */
async function selectChat(chatId) {
    const res = await apiGet(`/api/chats/${chatId}`);
    if (!res.success) {
        toast(res.message || 'Не удалось загрузить чат', 'error');
        return;
    }

    state.activeChatId = chatId;
    state.activeChat = res.data;

    // Обновляем URL без перезагрузки
    updateUrl(chatId);

    // Обновляем активный элемент в sidebar
    renderChatList();

    // Header
    renderChatHeader(res.data);

    // History
    renderMessages(res.data.messages || []);
}

// ============ Удаление чата ============

/**
 * Удаляет чат (с подтверждением).
 * @param {number} chatId Идентификатор чата
 */
async function deleteChat(chatId) {
    if (!confirm('Вы уверены, что хотите удалить чат?')) {
        return;
    }

    try {
        const res = await fetch(`/api/chats/${chatId}`, {
            method: 'DELETE',
            credentials: 'same-origin',
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || 'Ошибка удаления чата', 'error');
            return;
        }

        toast('Чат удалён', 'success');

        // Удаляем из локального списка
        state.chats = state.chats.filter(c => c.id !== chatId);

        // Выбираем следующий или показываем пустое состояние
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

// ============ Рендер header ============

/**
 * Отрисовывает заголовок и модель чата.
 * @param {object} chat ChatDetailDto
 */
function renderChatHeader(chat) {
    const headerEl = document.getElementById('chat-header');
    const titleEl = document.getElementById('chat-title');
    const modelEl = document.getElementById('chat-model');

    if (headerEl) headerEl.classList.remove('d-none');
    if (titleEl) titleEl.textContent = chat.title || 'Без названия';
    if (modelEl) modelEl.textContent = chat.model || '—';
}

// ============ Рендер сообщений ============

/**
 * Отрисовывает ленту сообщений.
 * @param {Array} messages ChatMessageDto[]
 */
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

    container.innerHTML = messages
        .map(renderMessage)
        .join('');

    // Автоскролл к последнему сообщению
    scrollToBottom();
}

/**
 * Рендерит одно сообщение.
 * @param {object} msg ChatMessageDto
 * @returns {string} HTML
 */
function renderMessage(msg) {
    if (msg.role === 'system') {
        // Системные сообщения в UI не показываем
        return '';
    }

    if (msg.role === 'tool') {
        // Результат вызова инструмента — компактный блок
        return renderToolResult(msg);
    }

    const isUser = msg.role === 'user';
    const avatar = isUser ? '👤' : '🤖';
    const roleLabel = isUser ? 'Вы' : 'Ассистент';

    let toolCallsHtml = '';
    if (msg.toolCallsJson) {
        toolCallsHtml = renderToolCallsBlock(msg.toolCallsJson);
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

/**
 * Рендерит блок с tool_calls над сообщением ассистента (минимальный вариант).
 * @param {string} toolCallsJson JSON-строка
 * @returns {string} HTML
 */
function renderToolCallsBlock(toolCallsJson) {
    let calls = [];
    try {
        calls = JSON.parse(toolCallsJson);
    } catch {
        return '';
    }

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

/**
 * Рендерит результат вызова инструмента (компактный блок).
 * @param {object} msg ChatMessageDto
 * @returns {string} HTML
 */
function renderToolResult(msg) {
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

// ============ Пустое состояние ============

/**
 * Показывает пустое состояние (нет выбранного чата).
 */
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
}

// ============ URL sync ============

/**
 * Обновляет URL (?chatId=N) без перезагрузки.
 * @param {number|null} chatId
 */
function updateUrl(chatId) {
    const url = new URL(window.location.href);
    if (chatId) {
        url.searchParams.set('chatId', chatId);
    } else {
        url.searchParams.delete('chatId');
    }
    window.history.replaceState({}, '', url.toString());
}

/**
 * Читает chatId из URL.
 * @returns {number|null}
 */
function readUrlChatId() {
    const raw = new URL(window.location.href).searchParams.get('chatId');
    if (!raw) return null;
    const id = parseInt(raw, 10);
    return Number.isFinite(id) ? id : null;
}

// ============ Утилиты ============

/**
 * Автоскролл к последнему сообщению.
 */
function scrollToBottom() {
    const container = document.getElementById('chat-messages');
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
}

/**
 * Форматирует дату в относительный вид.
 * @param {string} iso ISO-дата
 * @returns {string}
 */
function formatRelativeDate(iso) {
    if (!iso) return '';
    const date = new Date(iso);
    const now = new Date();
    const diffMs = now - date;
    const diffSec = Math.floor(diffMs / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHour = Math.floor(diffMin / 60);

    if (diffSec < 60) return 'только что';
    if (diffMin < 60) return `${diffMin} мин назад`;
    if (diffHour < 24) return `${diffHour} ч назад`;

    // Вчера?
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    if (date.toDateString() === yesterday.toDateString()) return 'вчера';

    // Иначе — DD.MM
    const dd = String(date.getDate()).padStart(2, '0');
    const mm = String(date.getMonth() + 1).padStart(2, '0');
    return `${dd}.${mm}`;
}

/**
 * Форматирует время сообщения (HH:MM).
 * @param {string} iso ISO-дата
 * @returns {string}
 */
function formatTime(iso) {
    if (!iso) return '';
    const date = new Date(iso);
    const hh = String(date.getHours()).padStart(2, '0');
    const mm = String(date.getMinutes()).padStart(2, '0');
    return `${hh}:${mm}`;
}