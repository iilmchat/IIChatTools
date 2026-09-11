/**
 * Модуль страницы /test: выбор инструмента, ввод параметров, выполнение с подтверждением.
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

    // Формируем шаблон параметров
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
        // Первый вызов — возможно, получим requiresApproval
        const first = await apiPost('/api/tools/execute', { toolName: name, arguments: args });

        if (first.success && first.data && first.data.requiresApproval) {
            // Ожидаем подтверждения
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

            // Повторный вызов с approvalId
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

    const payload = {
        success: response.success,
        message: response.message || null,
        data: response.data || null
    };

    resultEl.textContent = JSON.stringify(payload, null, 2);
}