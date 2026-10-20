/**
 * Модуль вкладки «Оркестратор» в /admin (v2.2.2, KI-245).
 * Управление runtime-лимитами Orchestrator: MaxSubtasks, MaxParallel,
 * TokenBudget, HumanApproval и белым списком worker-агентов.
 * Изменения сохраняются в AppSettings и применяются в runtime без перезапуска.
 *
 * Endpoints:
 *   GET  /api/admin/orchestrator/settings
 *   PUT  /api/admin/orchestrator/settings
 *   POST /api/admin/orchestrator/reset
 *
 * Все видимые пользователю строки берутся из data-атрибутов #pane-orchestrator
 * (RULES 4.17) — хардкод RU-текста в JS запрещён.
 * © 2026 RuChating (iilmchat) · IIChatTools v2.2.2
 */
import { apiGet } from './api.js';
import { escapeHtml, toast } from './ui.js';

// ---------- Константы ----------

const SETTINGS_ENDPOINT = '/api/admin/orchestrator/settings';
const RESET_ENDPOINT = '/api/admin/orchestrator/reset';
const APPROVAL_VALUES = ['Never', 'BetweenSubtasks', 'AtEnd'];
const DEFAULT_APPROVAL = 'Never';

// ---------- Инициализация ----------

/**
 * Инициализирует вкладку «Оркестратор»: ленивая загрузка при первом показе
 * вкладки (shown.bs.tab) и подключение кнопок Save/Reset.
 */
export function initOrchestratorTab() {
    const tab = document.getElementById('tab-orchestrator');
    if (!tab) return;

    tab.addEventListener('shown.bs.tab', () => loadSettings(), { once: true });

    // Форма без submit-кнопки: Enter в поле не должен перезагружать страницу.
    document.getElementById('orch-form')
        ?.addEventListener('submit', e => e.preventDefault());

    document.getElementById('btn-save-orchestrator')
        ?.addEventListener('click', saveSettings);
    document.getElementById('btn-reset-orchestrator')
        ?.addEventListener('click', resetSettings);
}

/**
 * Возвращает объект локализованных ярлыков из data-атрибутов #pane-orchestrator.
 * Английские значения — fallback на случай отсутствия data-атрибутов.
 * @returns {object}
 */
function paneLabels() {
    const pane = document.getElementById('pane-orchestrator');
    return {
        intro: pane?.dataset.labelIntro
            || 'Runtime limits and worker whitelist for the multi-agent orchestrator.',
        maxSubtasks: pane?.dataset.labelMaxSubtasks || 'Max subtasks (1–10)',
        maxParallel: pane?.dataset.labelMaxParallel || 'Max parallel subtasks (1–10)',
        tokenBudget: pane?.dataset.labelTokenBudget || 'Token budget (1000–1000000)',
        humanApproval: pane?.dataset.labelHumanApproval || 'Human approval',
        allowedWorkers: pane?.dataset.labelAllowedWorkers || 'Allowed worker agents (one per line)',
        save: pane?.dataset.labelSave || 'Save',
        reset: pane?.dataset.labelReset || 'Reset to defaults',
        saved: pane?.dataset.labelSaved || 'Orchestrator settings saved',
        resetDone: pane?.dataset.labelResetDone || 'Orchestrator settings reset to defaults',
        resetConfirm: pane?.dataset.labelResetConfirm || 'Reset orchestrator settings to defaults?',
        maxSubtasksValidation: pane?.dataset.labelMaxSubtasksValidation || 'Max subtasks must be 1–10',
        maxParallelValidation: pane?.dataset.labelMaxParallelValidation || 'Max parallel subtasks must be 1–10',
        tokenBudgetValidation: pane?.dataset.labelTokenBudgetValidation || 'Token budget must be 1000–1000000',
        workersRequired: pane?.dataset.labelWorkersRequired || 'At least one worker agent is required',
        overriddenBadge: pane?.dataset.labelOverriddenBadge || 'modified',
        approvalNever: pane?.dataset.labelApprovalNever || 'Never',
        approvalBetweenSubtasks: pane?.dataset.labelApprovalBetweenSubtasks || 'Between subtasks',
        approvalAtEnd: pane?.dataset.labelApprovalAtEnd || 'At end',
        loadError: pane?.dataset.labelLoadError || 'Failed to load orchestrator settings'
    };
}

// ---------- Загрузка настроек ----------

/**
 * Загружает настройки Orchestrator и заполняет форму.
 */
async function loadSettings() {
    const labels = paneLabels();
    const loading = document.getElementById('orch-loading');
    if (loading) loading.hidden = false;

    const res = await apiGet(SETTINGS_ENDPOINT);

    if (loading) loading.hidden = true;

    if (!res.success) {
        toast(res.message || labels.loadError, 'error');
        return;
    }

    fillForm(res.data || {});
}

/**
 * Заполняет поля формы значениями из ответа API.
 * @param {object} data Настройки Orchestrator
 */
function fillForm(data) {
    const labels = paneLabels();

    setInputValue('orch-max-subtasks', data.maxSubtasks);
    setInputValue('orch-max-parallel', data.maxParallel);
    setInputValue('orch-token-budget', data.tokenBudget);

    const approval = document.getElementById('orch-human-approval');
    if (approval) {
        approval.value = APPROVAL_VALUES.includes(data.humanApproval)
            ? data.humanApproval
            : DEFAULT_APPROVAL;
    }

    const workers = document.getElementById('orch-allowed-workers');
    if (workers) {
        const list = Array.isArray(data.allowedWorkerAgents) ? data.allowedWorkerAgents : [];
        workers.value = list.join('\n');
    }

    const badge = document.getElementById('orch-overridden-badge');
    if (badge) {
        badge.hidden = data.isOverridden !== true;
        if (badge.hidden === false) {
            badge.innerHTML = escapeHtml(labels.overriddenBadge);
        }
    }
}

/**
 * Устанавливает значение числового поля.
 * @param {string} id Идентификатор элемента
 * @param {*} value Значение
 */
function setInputValue(id, value) {
    const el = document.getElementById(id);
    if (!el) return;
    el.value = (value === null || value === undefined) ? '' : String(value);
}

// ---------- Сохранение ----------

/**
 * Валидирует форму и отправляет настройки на сервер (PUT).
 */
async function saveSettings() {
    const labels = paneLabels();

    const maxSubtasks = readInteger('orch-max-subtasks');
    if (!Number.isInteger(maxSubtasks) || maxSubtasks < 1 || maxSubtasks > 10) {
        toast(labels.maxSubtasksValidation, 'error');
        return;
    }

    const maxParallel = readInteger('orch-max-parallel');
    if (!Number.isInteger(maxParallel) || maxParallel < 1 || maxParallel > 10) {
        toast(labels.maxParallelValidation, 'error');
        return;
    }

    const tokenBudget = readInteger('orch-token-budget');
    if (!Number.isInteger(tokenBudget) || tokenBudget < 1000 || tokenBudget > 1000000) {
        toast(labels.tokenBudgetValidation, 'error');
        return;
    }

    const approval = document.getElementById('orch-human-approval');
    const humanApproval = approval && APPROVAL_VALUES.includes(approval.value)
        ? approval.value
        : DEFAULT_APPROVAL;

    const workers = readWorkers();
    if (workers.length === 0) {
        toast(labels.workersRequired, 'error');
        return;
    }

    const body = {
        maxSubtasks: maxSubtasks,
        maxParallel: maxParallel,
        tokenBudget: tokenBudget,
        humanApproval: humanApproval,
        allowedWorkerAgents: workers
    };

    try {
        const res = await fetch(SETTINGS_ENDPOINT, {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || labels.loadError, 'error');
            return;
        }

        toast(labels.saved, 'success');
        await loadSettings();
    } catch (ex) {
        toast(ex.message || labels.loadError, 'error');
    }
}

/**
 * Читает строго целое число из поля ввода.
 * @param {string} id Идентификатор элемента
 * @returns {number} Целое число или NaN
 */
function readInteger(id) {
    const el = document.getElementById(id);
    if (!el) return NaN;

    const raw = el.value.trim();
    if (!/^-?\d+$/.test(raw)) return NaN;

    return parseInt(raw, 10);
}

/**
 * Читает список worker-агентов из textarea: по одному на строку.
 * @returns {string[]} Очищенный от пустых строк список
 */
function readWorkers() {
    const el = document.getElementById('orch-allowed-workers');
    if (!el) return [];

    return el.value
        .split(/\r?\n/)
        .map(x => x.trim())
        .filter(x => x.length > 0);
}

// ---------- Сброс к умолчаниям ----------

/**
 * Сбрасывает настройки Orchestrator к базовым значениям (POST reset).
 */
async function resetSettings() {
    const labels = paneLabels();
    if (!confirm(labels.resetConfirm)) return;

    try {
        const res = await fetch(RESET_ENDPOINT, {
            method: 'POST',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res.success) {
            toast(res.message || labels.loadError, 'error');
            return;
        }

        toast(labels.resetDone, 'success');
        await loadSettings();
    } catch (ex) {
        toast(ex.message || labels.loadError, 'error');
    }
}
