/**
 * Модуль страницы /test: выбор инструмента, ввод параметров, выполнение с подтверждением.
 * Поддерживает визуализацию PNG-скриншотов с кнопками «Открыть» и «Скачать».
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { requestApproval } from './approvals.js';

let _tools = [];
let _currentDescriptor = null;

/**
 * Инициализирует страницу тестирования.
 */
export function initTestPage() {
    document.getElementById('tool-select').addEventListener('change', onToolSelected);
    document.getElementById('btn-execute').addEventListener('click', executeTool);

    loadTools();
}

/**
 * Загружает список инструментов с сервера.
 */
async function loadTools() {
    const select = document.getElementById('tool-select');
    const res = await apiGet('/api/tools');
    if (!res.success) {
        toast(res.message || 'Не удалось загрузить список инструментов', 'error');
        return;
    }
    _tools = res.data || [];

    select.innerHTML = `<option value="">— Инструмент не выбран —</option>` +
        _tools.map(t => `<option value="${escapeHtml(t.name)}">${escapeHtml(t.name)}</option>`).join('');
}

/**
 * Обработчик выбора инструмента — заполняет шаблон параметров.
 */
function onToolSelected() {
    const name = document.getElementById('tool-select').value;
    const descEl = document.getElementById('tool-description');
    const paramsEl = document.getElementById('tool-params');

    if (!name) {
        _currentDescriptor = null;
        descEl.textContent = '';
        paramsEl.value = '{}';
        return;
    }

    _currentDescriptor = _tools.find(t => t.name === name);
    if (!_currentDescriptor) return;

    descEl.textContent = _currentDescriptor.description || '';

    const template = {};
    (_currentDescriptor.parameters || []).forEach(p => {
        if (p.default !== undefined && p.default !== null) {
            template[p.name] = p.default;
        } else if (p.type === 'string') {
            template[p.name] = '';
        } else if (p.type === 'integer' || p.type === 'int') {
            template[p.name] = 0;
        } else if (p.type === 'bool' || p.type === 'boolean') {
            template[p.name] = false;
        }
    });
    paramsEl.value = JSON.stringify(template, null, 2);
}

/**
 * Выполняет выбранный инструмент с учётом системы подтверждений.
 */
async function executeTool() {
    const name = document.getElementById('tool-select').value;
    if (!name) { toast('Инструмент не выбран', 'warning'); return; }

    let args;
    try {
        args = JSON.parse(document.getElementById('tool-params').value || '{}');
    } catch {
        toast('Некорректный JSON в параметрах', 'error');
        return;
    }

    const statusEl = document.getElementById('exec-status');
    const resultEl = document.getElementById('tool-result');
    const btn = document.getElementById('btn-execute');

    statusEl.textContent = 'Выполняется…';
    statusEl.className = 'badge bg-warning text-dark';
    resultEl.textContent = '…';
    btn.disabled = true;

    try {
        const first = await apiPost('/api/tools/execute', { toolName: name, arguments: args });

        if (first.success && first.data && first.data.requiresApproval) {
            const decision = await requestApproval(
                first.data.actionId,
                first.data.toolName,
                JSON.stringify(args),
                new Date(first.data.expiresAt).getTime()
            );

            if (decision !== 'approved') {
                statusEl.textContent = decision;
                statusEl.className = 'badge bg-secondary';
                resultEl.textContent = `Действие: ${decision}`;
                return;
            }

            const second = await apiPost('/api/tools/execute', {
                toolName: name,
                arguments: args,
                approvalId: first.data.actionId
            });
            renderResult(second);
        } else {
            renderResult(first);
        }
    } catch (ex) {
        statusEl.textContent = 'Ошибка';
        statusEl.className = 'badge bg-danger';
        resultEl.textContent = ex.message || 'Неизвестная ошибка';
    } finally {
        btn.disabled = false;
    }
}

/**
 * Отрисовывает результат выполнения инструмента.
 * Если результат содержит PNG (screenshot) — отображает картинку с кнопками.
 * @param {object} response Ответ API { success, data, message }
 */
function renderResult(response) {
    const statusEl = document.getElementById('exec-status');
    const resultEl = document.getElementById('tool-result');

    if (response.success) {
        statusEl.textContent = 'Успех';
        statusEl.className = 'badge bg-success';
    } else {
        statusEl.textContent = 'Ошибка';
        statusEl.className = 'badge bg-danger';
    }

    // Особый случай: скриншот (PNG в base64)
    if (response.success && response.data && response.data.base64 && response.data.format === 'png') {
        resultEl.innerHTML = `
            <div style="margin-bottom:10px;">
                <strong>PNG, ${response.data.sizeBytes} байт</strong>
                <button class="btn btn-sm btn-outline-primary ms-2" id="btn-show-screenshot">
                    Открыть картинку
                </button>
                <button class="btn btn-sm btn-outline-secondary ms-1" id="btn-save-screenshot">
                    Скачать PNG
                </button>
            </div>
            <img src="data:image/png;base64,${response.data.base64}"
                 style="max-width:100%; border:1px solid #ccc; border-radius:4px;" />
        `;

        // Открыть в новом окне
        document.getElementById('btn-show-screenshot').addEventListener('click', () => {
            const win = window.open('', '_blank');
            win.document.write(`
                <!DOCTYPE html>
                <html>
                <head><title>Screenshot</title></head>
                <body style="margin:0; background:#333;">
                    <img src="data:image/png;base64,${response.data.base64}" 
                         style="max-width:100%; display:block; margin:0 auto;" />
                </body>
                </html>
            `);
            win.document.close();
        });

        // Скачать PNG
        document.getElementById('btn-save-screenshot').addEventListener('click', () => {
            const a = document.createElement('a');
            a.href = 'data:image/png;base64,' + response.data.base64;
            a.download = 'screenshot-' + Date.now() + '.png';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        });

        return;
    }

    // Обычный JSON-результат
    const payload = {
        success: response.success,
        message: response.message || null,
        data: response.data || null
    };
    resultEl.textContent = JSON.stringify(payload, null, 2);
}