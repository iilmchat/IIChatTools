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

        let pretty = parametersJson;
        try { pretty = JSON.stringify(JSON.parse(parametersJson), null, 2); } catch { /* оставляем как есть */ }
        document.getElementById('approval-params').textContent = pretty;

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