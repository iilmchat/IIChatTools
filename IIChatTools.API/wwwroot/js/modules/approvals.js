/**
 * Модуль подтверждения действий агента.
 * Отображает модальное окно, ожидает решения пользователя, отправляет решение на сервер.
 *
 * Используется в двух сценариях:
 *   1. /test  — старый flow через PendingActions: /api/approvals/{actionId}/...
 *   2. /chat  — новый flow через IChatApprovalCoordinator: /api/chat/approvals/{callId}/...
 *
 * Особенности UX:
 *   - Модалку можно перетаскивать за заголовок (drag-and-drop).
 *   - Закрытие крестиком (X) = Reject: автоматически отправляется reject на сервер.
 *   - При Reject в /chat причина не запрашивается (см. Q6 Фазы 1.7).
 *
 * © 2026 RuChating (iilmchat) · IIChatTools v1.3
 */
import { apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';

/**
 * Запрашивает подтверждение действия у пользователя (страница /test).
 * @param {number} actionId Идентификатор ожидающего действия
 * @param {string} toolName Имя инструмента
 * @param {string} parametersJson Параметры в виде JSON-строки
 * @param {number} expiresAt Время истечения в UTC (миллисекунды)
 * @returns {Promise<{decision: 'approved'|'rejected'|'expired', reason?: string}>}
 */
export function requestApproval(actionId, toolName, parametersJson, expiresAt) {
    return _showApprovalModal({
        toolName,
        parametersJson,
        expiresAt,
        approveUrl: `/api/approvals/${actionId}/approve`,
        rejectUrl: `/api/approvals/${actionId}/reject`,
        askReason: true,
    });
}

/**
 * Запрашивает подтверждение вызова инструмента в чате (страница /chat).
 * Отличие от requestApproval: другой endpoint (IChatApprovalCoordinator) и
 * отсутствие запроса причины отклонения (см. Q6 Фазы 1.7).
 *
 * @param {string} callId Идентификатор вызова инструмента (от LM Studio)
 * @param {string} toolName Имя инструмента
 * @param {string} parametersJson Параметры в виде JSON-строки
 * @param {number} expiresAt Время истечения в UTC (миллисекунды)
 * @returns {Promise<{decision: 'approved'|'rejected'|'expired'}>}
 */
export function requestChatApproval(callId, toolName, parametersJson, expiresAt) {
    return _showApprovalModal({
        toolName,
        parametersJson,
        expiresAt,
        approveUrl: `/api/chat/approvals/${callId}/approve`,
        rejectUrl: `/api/chat/approvals/${callId}/reject`,
        askReason: false,
    });
}

/**
 * Внутренняя реализация модалки approve/reject.
 * Параметризована URL-ами для поддержки двух flow (/test и /chat).
 *
 * @param {object} opts
 * @param {string} opts.toolName
 * @param {string} opts.parametersJson
 * @param {number} opts.expiresAt — миллисекунды UTC
 * @param {string} opts.approveUrl
 * @param {string} opts.rejectUrl
 * @param {boolean} opts.askReason — спрашивать ли причину отклонения через prompt()
 * @returns {Promise<{decision: 'approved'|'rejected'|'expired', reason?: string}>}
 */
function _showApprovalModal({ toolName, parametersJson, expiresAt, approveUrl, rejectUrl, askReason }) {
    return new Promise(resolve => {
        const modalEl = document.getElementById('approvalModal');
        if (!modalEl) {
            toast('Модальное окно подтверждения не найдено', 'error');
            resolve({ decision: 'rejected', reason: 'Модальное окно не найдено' });
            return;
        }

        document.getElementById('approval-tool-name').textContent = toolName;

        // KI-249 (v2.0.1): рендерим параметры человекочитаемо (tool-specific).
        const paramsEl = document.getElementById('approval-params');
        if (paramsEl) {
            paramsEl.innerHTML = renderToolParams(toolName, parametersJson);

            // Подсветка hljs для code-блоков (если библиотека загружена).
            if (window.hljs) {
                paramsEl.querySelectorAll('pre code').forEach(el => {
                    try { window.hljs.highlightElement(el); } catch { /* ignore */ }
                });
            }
        }

        const countdownEl = document.getElementById('approval-countdown');
        const approveBtn = document.getElementById('approval-approve');
        const rejectBtn = document.getElementById('approval-reject');

        // backdrop: static — клик по фону не закрывает; keyboard: false — Escape не закрывает.
        // Крестик остаётся как единственный способ закрыть без решения (но см. ниже: X = Reject).
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl, {
            backdrop: 'static',
            keyboard: false,
        });

        let settled = false;
        let timer = null;

        const cleanup = () => {
            if (timer) {
                clearInterval(timer);
                timer = null;
            }
            approveBtn.removeEventListener('click', onApprove);
            rejectBtn.removeEventListener('click', onReject);
        };

        const updateCountdown = () => {
            const left = Math.max(0, expiresAt - Date.now());
            const sec = Math.floor(left / 1000);
            const mm = Math.floor(sec / 60);
            const ss = sec % 60;
            countdownEl.textContent = `Осталось: ${mm}:${ss.toString().padStart(2, '0')}`;

            if (left <= 0 && !settled) {
                settled = true;
                cleanup();
                modal.hide();
                toast('Время подтверждения истекло', 'warning');
                resolve({ decision: 'expired' });
            }
        };
        timer = setInterval(updateCountdown, 1000);
        updateCountdown();

        /**
         * Отправляет решение на сервер и резолвит promise.
         * @param {'approve'|'reject'} action
         * @param {string} [reason] — только для reject (в /test)
         */
        const sendDecision = async (action, reason = '') => {
            const url = action === 'approve' ? approveUrl : rejectUrl;
            const body = action === 'reject' && askReason ? { reason } : {};
            try {
                const res = await apiPost(url, body);
                modal.hide();
                if (!res.success) {
                    const msg = res.message || 'Ошибка';
                    toast(msg, 'error');
                    resolve({ decision: 'rejected', reason: msg });
                    return;
                }
                resolve(action === 'approve'
                    ? { decision: 'approved' }
                    : { decision: 'rejected', reason });
            } catch (ex) {
                modal.hide();
                const msg = ex.message || 'Ошибка соединения';
                toast(msg, 'error');
                resolve({ decision: 'rejected', reason: msg });
            }
        };

        const onApprove = async () => {
            if (settled) return;
            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;
            await sendDecision('approve');
        };

        const onReject = async () => {
            if (settled) return;

            // Причина — только если askReason === true (flow /test).
            // В /chat — без причины (Q6 Фазы 1.7).
            let reason = '';
            if (askReason) {
                const raw = prompt('Причина отклонения (необязательно):', '');
                if (raw === null) return;   // отмена диалога — модалка остаётся, settled = false
                reason = raw.trim();
            }

            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;
            await sendDecision('reject', reason);
        };

        approveBtn.addEventListener('click', onApprove);
        rejectBtn.addEventListener('click', onReject);

        // При закрытии модалки (в т.ч. крестиком) — финализируем promise.
        // Если решение уже принято (settled) — просто восстанавливаем UI.
        // Если закрыли без решения (X) — считаем это Reject и отправляем на сервер,
        // чтобы не оставить SSE-стрим висеть 5 минут.
        const onHidden = () => {
            // Восстанавливаем кнопки для следующего открытия
            approveBtn.disabled = rejectBtn.disabled = false;

            if (settled) return;

            settled = true;
            cleanup();
            // Огонь-и-забыли: reject на сервер, стрим продолжается, LLM получит ToolResult.Fail.
            const reason = askReason ? 'Закрыто пользователем' : '';
            sendDecision('reject', reason);
        };
        // once: true — снимается после первого закрытия; при повторном modal.show() добавляется заново.
        modalEl.addEventListener('hidden.bs.modal', onHidden, { once: true });

        // Drag-and-drop по заголовку модалки
        _makeDraggable(modalEl);

        modal.show();
    });
}

/**
 * Делает модалку перетаскиваемой за заголовок (только для текущего экземпляра).
 * При закрытии — стили сбрасываются (см. _resetModalDrag).
 * @param {HTMLElement} modalEl
 */
function _makeDraggable(modalEl) {
    const dialog = modalEl.querySelector('.modal-dialog');
    const header = modalEl.querySelector('.modal-header');
    if (!dialog || !header) return;

    // Помечаем заголовок как «перетаскиваемый» (курсор move)
    header.classList.add('approval-modal-draggable');

    let dragging = false;
    let startX = 0, startY = 0, startLeft = 0, startTop = 0;

    const onMouseDown = (e) => {
        // Не перетаскивать, если пользователь кликнул по кнопке (X, Закрыть) или ссылке
        if (e.target.closest('button, a, input, select, textarea')) return;
        e.preventDefault();

        const rect = dialog.getBoundingClientRect();
        startX = e.clientX;
        startY = e.clientY;
        startLeft = rect.left;
        startTop = rect.top;

        // Переводим dialog в position:fixed (иначе Bootstrap transform:translate сломает координаты)
        dialog.style.position = 'fixed';
        dialog.style.margin = '0';
        dialog.style.left = `${startLeft}px`;
        dialog.style.top = `${startTop}px`;
        dialog.style.width = `${rect.width}px`;
        dialog.style.transform = 'none';
        dialog.style.maxWidth = `${rect.width}px`;

        dragging = true;
        document.body.style.userSelect = 'none';
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    };

    const onMouseMove = (e) => {
        if (!dragging) return;
        const dx = e.clientX - startX;
        const dy = e.clientY - startY;
        dialog.style.left = `${startLeft + dx}px`;
        dialog.style.top = `${startTop + dy}px`;
    };

    const onMouseUp = () => {
        dragging = false;
        document.body.style.userSelect = '';
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
    };

    // Сбрасываем drag-стили при закрытии модалки
    const resetDrag = () => {
        dialog.style.position = '';
        dialog.style.margin = '';
        dialog.style.left = '';
        dialog.style.top = '';
        dialog.style.width = '';
        dialog.style.transform = '';
        dialog.style.maxWidth = '';
        header.classList.remove('approval-modal-draggable');
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        document.body.style.userSelect = '';
    };
    modalEl.addEventListener('hidden.bs.modal', resetDrag, { once: true });

    header.addEventListener('mousedown', onMouseDown);
}


// ============================================================
// KI-249 (v2.0.1): Tool-специфичный рендер параметров approval.
// ============================================================

/**
 * Читает локализованные метки из data-label-* на #approval-params.
 * RULES § 4.17: JS-строки — через data-* (не хардкодить).
 * @returns {object}
 */
function approvalLabels() {
    const el = document.getElementById('approval-params');
    return {
        task:         el?.dataset.labelTask         || 'Task',
        context:      el?.dataset.labelContext      || 'Context',
        to:           el?.dataset.labelTo           || 'To',
        cc:           el?.dataset.labelCc           || 'CC',
        subject:      el?.dataset.labelSubject      || 'Subject',
        body:         el?.dataset.labelBody         || 'Body',
        action:       el?.dataset.labelAction       || 'Action',
        target:       el?.dataset.labelTarget       || 'Target',
        sql:          el?.dataset.labelSql          || 'SQL',
        code:         el?.dataset.labelCode         || 'Code',
        language:     el?.dataset.labelLanguage     || 'Language',
        parallel:     el?.dataset.labelParallel     || 'Parallel',
        maxSubtasks:  el?.dataset.labelMaxSubtasks  || 'Max subtasks',
        otherFields:  el?.dataset.labelOtherFields  || 'Other fields',
        rawJson:      el?.dataset.labelRawJson      || 'JSON'
    };
}

/**
 * Список агентов-обёрток с полем `task` (для единого рендера).
 * KI-249 (v2.0.1).
 */
const AGENT_TASK_TOOLS = new Set([
    'file_system_agent', 'code_agent', 'code_reviewer_agent',
    'code_agent_with_review', 'web_agent', 'git_agent', 'github_agent',
    'planner_agent', 'planner_pro_agent', 'planner_contra_agent',
    'planner_judge_agent', 'planner_with_debate', 'mail_agent',
    'external_llm_agent', 'browser_agent', 'vision_agent',
    'orchestrator_planner_agent', 'consult_secondary_agent'
]);

/**
 * KI-249 (v2.0.1): рендерит параметры вызова в HTML.
 * Диспетчер по имени tool'а. Default — pretty-print JSON.
 *
 * @param {string} toolName
 * @param {string} parametersJson
 * @returns {string} HTML
 */
function renderToolParams(toolName, parametersJson) {
    let params;
    try { params = JSON.parse(parametersJson); }
    catch { params = null; }

    if (!params || typeof params !== 'object' || Array.isArray(params)) {
        // Не JSON-объект — показываем как есть.
        const L = approvalLabels();
        return renderRawJson(parametersJson, L);
    }

    const L = approvalLabels();

    // Tool-специфичные рендеры.
    if (toolName === 'orchestrate_task') return renderOrchestrateTaskParams(params, L);
    if (toolName === 'send_email') return renderSendEmailParams(params, L);
    if (toolName === 'database_agent') return renderDatabaseAgentParams(params, L);
    if (toolName === 'run_python' || toolName === 'run_javascript') {
        return renderCodeParams(params, toolName, L);
    }

    // Все агенты-обёртки с полем task.
    if (AGENT_TASK_TOOLS.has(toolName)) return renderAgentTaskParams(params, L);

    // Default — pretty JSON.
    return renderDefaultJson(params, L);
}

/**
 * Общий рендер: карточка с полем label + value.
 * @param {string} label
 * @param {string} value
 * @param {object} [opts] — { mono: bool, maxHeight: '200px' }
 * @returns {string}
 */
function renderField(label, value, opts = {}) {
    const mono = opts.mono ? ' chat-approval-field-value-mono' : '';
    return `
        <div class="chat-approval-field">
            <div class="chat-approval-field-label">${escapeHtml(label)}:</div>
            <div class="chat-approval-field-value${mono}">${escapeHtml(value)}</div>
        </div>`;
}

/**
 * Рендер: Orchestrator-Worker (top-level task + badges).
 */
function renderOrchestrateTaskParams(params, L) {
    const parts = [];
    if (params.task) parts.push(renderField(L.task, params.task));

    const badges = [];
    if (typeof params.parallel === 'boolean') {
        const icon = params.parallel ? '✅' : '❌';
        const word = params.parallel ? 'Да' : 'Нет';
        badges.push(`<span class="chat-approval-badge">${escapeHtml(L.parallel)}: ${icon} ${escapeHtml(word)}</span>`);
    }
    if (Number.isFinite(params.maxSubtasks)) {
        badges.push(`<span class="chat-approval-badge">${escapeHtml(L.maxSubtasks)}: ${params.maxSubtasks}</span>`);
    }

    const otherFields = Object.keys(params)
        .filter(k => k !== 'task' && k !== 'parallel' && k !== 'maxSubtasks');

    let otherHtml = '';
    if (otherFields.length > 0) {
        const json = JSON.stringify(
            Object.fromEntries(otherFields.map(k => [k, params[k]])), null, 2);
        otherHtml = renderOtherFields(otherFields.length, json, L);
    }

    const badgesHtml = badges.length > 0
        ? `<div class="chat-approval-badges">${badges.join('')}</div>`
        : '';

    return `<div class="chat-approval-content">${parts.join('')}${badgesHtml}${otherHtml}</div>`;
}

/**
 * Рендер: агенты-обёртки с полем task.
 */
function renderAgentTaskParams(params, L) {
    const parts = [];
    if (params.task) parts.push(renderField(L.task, params.task));
    if (params.context) parts.push(renderField(L.context, params.context));

    const otherFields = Object.keys(params)
        .filter(k => k !== 'task' && k !== 'context');

    let otherHtml = '';
    if (otherFields.length > 0) {
        const json = JSON.stringify(
            Object.fromEntries(otherFields.map(k => [k, params[k]])), null, 2);
        otherHtml = renderOtherFields(otherFields.length, json, L);
    }

    return `<div class="chat-approval-content">${parts.join('')}${otherHtml}</div>`;
}

/**
 * Рендер: send_email.
 */
function renderSendEmailParams(params, L) {
    const parts = [];
    if (params.to) parts.push(renderField(L.to, String(params.to)));
    if (params.cc) parts.push(renderField(L.cc, String(params.cc)));
    if (params.subject) parts.push(renderField(L.subject, params.subject));
    if (params.body) parts.push(renderField(L.body, params.body));

    const otherFields = Object.keys(params)
        .filter(k => !['to', 'cc', 'subject', 'body'].includes(k));

    let otherHtml = '';
    if (otherFields.length > 0) {
        const json = JSON.stringify(
            Object.fromEntries(otherFields.map(k => [k, params[k]])), null, 2);
        otherHtml = renderOtherFields(otherFields.length, json, L);
    }

    return `<div class="chat-approval-content">${parts.join('')}${otherHtml}</div>`;
}

/**
 * Рендер: database_agent (action + sql + другие).
 */
function renderDatabaseAgentParams(params, L) {
    const parts = [];
    if (params.action) parts.push(renderField(L.action, params.action));
    if (params.connection) parts.push(renderField('Connection', params.connection));
    if (params.table) parts.push(renderField('Table', params.table));

    let sqlHtml = '';
    if (params.sql) {
        sqlHtml = `
            <div class="chat-approval-field">
                <div class="chat-approval-field-label">${escapeHtml(L.sql)}:</div>
                <pre class="chat-approval-code"><code class="language-sql">${escapeHtml(params.sql)}</code></pre>
            </div>`;
    }

    const otherFields = Object.keys(params)
        .filter(k => !['action', 'connection', 'table', 'sql'].includes(k));

    let otherHtml = '';
    if (otherFields.length > 0) {
        const json = JSON.stringify(
            Object.fromEntries(otherFields.map(k => [k, params[k]])), null, 2);
        otherHtml = renderOtherFields(otherFields.length, json, L);
    }

    return `<div class="chat-approval-content">${parts.join('')}${sqlHtml}${otherHtml}</div>`;
}

/**
 * Рендер: run_python / run_javascript.
 */
function renderCodeParams(params, toolName, L) {
    const lang = toolName === 'run_python' ? 'python' : 'javascript';
    const code = params.code || params.script || params.source || '';

    let codeHtml = '';
    if (code) {
        codeHtml = `
            <div class="chat-approval-field">
                <div class="chat-approval-field-label">${escapeHtml(L.code)} (${escapeHtml(lang)}):</div>
                <pre class="chat-approval-code"><code class="language-${lang}">${escapeHtml(code)}</code></pre>
            </div>`;
    }

    const otherFields = Object.keys(params)
        .filter(k => k !== 'code' && k !== 'script' && k !== 'source');

    let otherHtml = '';
    if (otherFields.length > 0) {
        const json = JSON.stringify(
            Object.fromEntries(otherFields.map(k => [k, params[k]])), null, 2);
        otherHtml = renderOtherFields(otherFields.length, json, L);
    }

    return `<div class="chat-approval-content">${codeHtml}${otherHtml}</div>`;
}

/**
 * Рендер: default (pretty JSON).
 */
function renderDefaultJson(params, L) {
    const json = JSON.stringify(params, null, 2);
    return `<div class="chat-approval-content">
        <pre class="chat-approval-code"><code class="language-json">${escapeHtml(json)}</code></pre>
    </div>`;
}

/**
 * Рендер: сырой JSON (когда paramsJson не парсится).
 */
function renderRawJson(raw, L) {
    return `<div class="chat-approval-content">
        <pre class="chat-approval-raw">${escapeHtml(raw || '')}</pre>
    </div>`;
}

/**
 * Рендер: свёрнутый блок «Другие поля (N)».
 */
function renderOtherFields(count, json, L) {
    const label = L.otherFields.replace('{0}', String(count));
    return `
        <details class="chat-approval-other">
            <summary class="chat-approval-other-summary">▸ ${escapeHtml(label)}</summary>
            <pre class="chat-approval-code"><code class="language-json">${escapeHtml(json)}</code></pre>
        </details>`;
}
