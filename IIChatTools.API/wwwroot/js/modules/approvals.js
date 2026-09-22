/**
 * Модуль подтверждения действий агента.
 * Отображает модальное окно, ожидает решения пользователя, отправляет решение на сервер.
 *
 * Используется в двух сценариях:
 *   1. /test         — старый flow через PendingActions: /api/approvals/{actionId}/...
 *   2. /chat         — новый flow через IChatApprovalCoordinator: /api/chat/approvals/{callId}/...
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

        const modal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });
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

        const onApprove = async () => {
            if (settled) return;
            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;

            try {
                const res = await apiPost(approveUrl, {});
                modal.hide();
                if (!res.success) {
                    toast(res.message || 'Ошибка подтверждения', 'error');
                    resolve({ decision: 'rejected', reason: res.message || 'Ошибка подтверждения' });
                    return;
                }
                resolve({ decision: 'approved' });
            } catch (ex) {
                modal.hide();
                toast(ex.message || 'Ошибка подтверждения', 'error');
                resolve({ decision: 'rejected', reason: ex.message || 'Ошибка подтверждения' });
            }
        };

        const onReject = async () => {
            if (settled) return;

            // Причина — только если askReason === true (flow /test).
            // В /chat — без причины (Q6 Фазы 1.7).
            let reason = '';
            if (askReason) {
                const raw = prompt('Причина отклонения (необязательно):', '');
                if (raw === null) return;   // отмена диалога — модалка остаётся
                reason = raw.trim();
            }

            settled = true;
            cleanup();
            approveBtn.disabled = rejectBtn.disabled = true;

            try {
                const res = await apiPost(rejectUrl, askReason ? { reason } : {});
                modal.hide();
                if (!res.success) {
                    toast(res.message || 'Ошибка отклонения', 'error');
                    resolve({ decision: 'rejected', reason: res.message || 'Ошибка отклонения' });
                    return;
                }
                resolve({ decision: 'rejected', reason });
            } catch (ex) {
                modal.hide();
                toast(ex.message || 'Ошибка отклонения', 'error');
                resolve({ decision: 'rejected', reason: ex.message || 'Ошибка отклонения' });
            }
        };

        approveBtn.addEventListener('click', onApprove);
        rejectBtn.addEventListener('click', onReject);

        // Восстанавливаем кнопки при следующем открытии модалки
        modalEl.addEventListener('hidden.bs.modal', () => {
            approveBtn.disabled = rejectBtn.disabled = false;
        });

        modal.show();
    });
}