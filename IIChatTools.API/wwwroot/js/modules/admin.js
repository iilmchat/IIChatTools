/**
 * Модуль страницы /admin: CRUD пользователей, настроек, белый список, аудит.
 * © 2026 RuChating (iilmchat) · IIChatTools v1.0
 */
import { apiGet, apiPost } from './api.js';
import { escapeHtml, toast } from './ui.js';

// ---------- Состояние ----------
const state = {
    users: [],
    roles: [],
    settings: [],
    whitelist: [],
    tools: [],
    audit: { page: 1, pageSize: 20, totalPages: 1, toolFilter: '' }
};

/**
 * Инициализирует страницу админки.
 */
export function initAdminPage() {
    bindToolbarButtons();
    loadUsers();
    loadRoles();
    loadSettings();
    loadWhitelist();
    loadAllTools();
    loadAudit(1);

    // Ленивая инициализация вкладок
    document.querySelector('#tab-settings').addEventListener('shown.bs.tab', loadSettings, { once: true });
    document.querySelector('#tab-whitelist').addEventListener('shown.bs.tab', () => {
        loadWhitelist();
        loadAllTools();
    }, { once: true });
    document.querySelector('#tab-audit').addEventListener('shown.bs.tab', () => loadAudit(1), { once: true });
}

// ---------- Пользователи ----------

async function loadUsers() {
    const res = await apiGet('/api/admin/users');
    const tbody = document.getElementById('users-tbody');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="7" class="text-danger text-center">${escapeHtml(res.message)}</td></tr>`;
        return;
    }
    state.users = res.data || [];
    tbody.innerHTML = state.users.map(u => `
        <tr>
            <td>${u.id}</td>
            <td>${escapeHtml(u.email)}</td>
            <td>${escapeHtml(u.fullName || '')}</td>
            <td>${(u.roles || []).map(r => `<span class="badge bg-secondary me-1">${escapeHtml(r)}</span>`).join('')}</td>
            <td>${u.isActive ? '<span class="badge bg-success">Активен</span>' : '<span class="badge bg-secondary">Неактивен</span>'}</td>
            <td>${new Date(u.registeredAt).toLocaleString()}</td>
            <td>
                <button class="btn btn-sm btn-outline-primary" data-action="edit-user" data-id="${u.id}">Изменить</button>
                <button class="btn btn-sm btn-outline-danger" data-action="delete-user" data-id="${u.id}">Удалить</button>
            </td>
        </tr>
    `).join('') || '<tr><td colspan="7" class="text-muted text-center">—</td></tr>';

    tbody.querySelectorAll('[data-action="edit-user"]').forEach(btn => {
        btn.addEventListener('click', () => openUserModal(parseInt(btn.dataset.id, 10)));
    });
    tbody.querySelectorAll('[data-action="delete-user"]').forEach(btn => {
        btn.addEventListener('click', () => deleteUser(parseInt(btn.dataset.id, 10)));
    });
}

async function loadRoles() {
    const res = await apiGet('/api/admin/roles');
    if (res.success) state.roles = res.data || [];
}

function openUserModal(userId) {
    const isNew = !userId;
    const user = isNew ? { id: 0, email: '', fullName: '', isActive: true, role: 'User' } : state.users.find(u => u.id === userId);
    if (!user) return;

    const title = isNew ? 'Добавить пользователя' : 'Изменить пользователя';
    const roleOptions = state.roles.map(r => `<option value="${r}" ${r === (user.roles?.[0] || user.role) ? 'selected' : ''}>${escapeHtml(r)}</option>`).join('');

    showModal(title, `
        <div class="mb-3">
            <label class="form-label">Электронная почта</label>
            <input type="email" class="form-control" id="m-user-email" value="${escapeHtml(user.email || '')}" required>
        </div>
        <div class="mb-3">
            <label class="form-label">Полное имя</label>
            <input type="text" class="form-control" id="m-user-fullname" value="${escapeHtml(user.fullName || '')}">
        </div>
        <div class="mb-3">
            <label class="form-label">Роль</label>
            <select class="form-select" id="m-user-role">${roleOptions}</select>
        </div>
        <div class="form-check mb-3">
            <input type="checkbox" class="form-check-input" id="m-user-active" ${user.isActive ? 'checked' : ''}>
            <label class="form-check-label" for="m-user-active">Активен</label>
        </div>
        ${isNew ? `
            <div class="mb-3">
                <label class="form-label">Пароль</label>
                <input type="password" class="form-control" id="m-user-password" autocomplete="new-password">
            </div>` : ''}
        ${!isNew ? `
            <div class="mb-3">
                <label class="form-label">Новый пароль (оставьте пустым, чтобы не менять)</label>
                <input type="password" class="form-control" id="m-user-newpassword" autocomplete="new-password">
            </div>` : ''}
    `, async () => {
        const body = {
            id: user.id || 0,
            email: document.getElementById('m-user-email').value.trim(),
            fullName: document.getElementById('m-user-fullname').value.trim(),
            role: document.getElementById('m-user-role').value,
            isActive: document.getElementById('m-user-active').checked
        };
        if (isNew) {
            body.password = document.getElementById('m-user-password').value;
            const res = await apiPost('/api/admin/users', body);
            if (!res.success) return { ok: false, message: res.message };
            toast('Пользователь создан', 'success');
        } else {
            const res = await fetch(`/api/admin/users/${user.id}`, {
                method: 'PUT',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            }).then(r => r.json());
            if (!res.success) return { ok: false, message: res.message };

            const newPwd = document.getElementById('m-user-newpassword').value;
            if (newPwd) {
                const rr = await apiPost(`/api/admin/users/${user.id}/reset-password`, { newPassword: newPwd });
                if (!rr.success) return { ok: false, message: rr.message };
            }
            toast('Пользователь обновлён', 'success');
        }
        await loadUsers();
        return { ok: true };
    });
}

async function deleteUser(id) {
    if (!confirm('Вы действительно хотите удалить пользователя?')) return;
    const res = await fetch(`/api/admin/users/${id}`, {
        method: 'DELETE',
        credentials: 'same-origin'
    }).then(r => r.json());
    if (!res.success) { toast(res.message || 'Ошибка', 'error'); return; }
    toast('Пользователь удалён', 'success');
    await loadUsers();
}

// ---------- Настройки ----------

async function loadSettings() {
    const res = await apiGet('/api/admin/settings');
    const tbody = document.getElementById('settings-tbody');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-danger text-center">${escapeHtml(res.message)}</td></tr>`;
        return;
    }
    state.settings = res.data || [];
    tbody.innerHTML = state.settings.map(s => `
        <tr>
            <td><span class="badge bg-light text-dark border">${escapeHtml(s.category)}</span></td>
            <td><code>${escapeHtml(s.key)}</code></td>
            <td>${escapeHtml(s.value)}</td>
            <td>${escapeHtml(s.type)}</td>
            <td>${s.isDefault ? '<span class="badge bg-secondary">Да</span>' : ''}</td>
            <td>
                <button class="btn btn-sm btn-outline-primary" data-action="edit-setting" data-id="${s.id}">Изменить</button>
                ${!s.isDefault ? `<button class="btn btn-sm btn-outline-warning" data-action="reset-setting" data-id="${s.id}">Сбросить</button>` : ''}
                <button class="btn btn-sm btn-outline-danger" data-action="delete-setting" data-id="${s.id}">Удалить</button>
            </td>
        </tr>
    `).join('') || '<tr><td colspan="6" class="text-muted text-center">—</td></tr>';

    tbody.querySelectorAll('[data-action="edit-setting"]').forEach(btn => {
        btn.addEventListener('click', () => openSettingModal(parseInt(btn.dataset.id, 10)));
    });
    tbody.querySelectorAll('[data-action="reset-setting"]').forEach(btn => {
        btn.addEventListener('click', () => resetSetting(parseInt(btn.dataset.id, 10)));
    });
    tbody.querySelectorAll('[data-action="delete-setting"]').forEach(btn => {
        btn.addEventListener('click', () => deleteSetting(parseInt(btn.dataset.id, 10)));
    });
}

function openSettingModal(id) {
    const isNew = !id;
    const s = isNew
        ? { id: 0, key: '', value: '', type: 'string', category: 'Общие' }
        : state.settings.find(x => x.id === id);
    if (!s) return;

    showModal(isNew ? 'Создать настройку' : 'Изменить настройку', `
        <div class="mb-3">
            <label class="form-label">Ключ</label>
            <input type="text" class="form-control" id="m-set-key" value="${escapeHtml(s.key)}" ${!isNew ? 'readonly' : ''}>
        </div>
        <div class="mb-3">
            <label class="form-label">Значение</label>
            <textarea class="form-control font-monospace" id="m-set-value" rows="3">${escapeHtml(s.value)}</textarea>
        </div>
        <div class="mb-3">
            <label class="form-label">Тип</label>
            <select class="form-select" id="m-set-type">
                <option value="string" ${s.type === 'string' ? 'selected' : ''}>string</option>
                <option value="int" ${s.type === 'int' ? 'selected' : ''}>int</option>
                <option value="bool" ${s.type === 'bool' ? 'selected' : ''}>bool</option>
                <option value="json" ${s.type === 'json' ? 'selected' : ''}>json</option>
            </select>
        </div>
        <div class="mb-3">
            <label class="form-label">Категория</label>
            <input type="text" class="form-control" id="m-set-category" value="${escapeHtml(s.category)}">
        </div>
    `, async () => {
        const body = {
            id: s.id || 0,
            key: document.getElementById('m-set-key').value.trim(),
            value: document.getElementById('m-set-value').value,
            type: document.getElementById('m-set-type').value,
            category: document.getElementById('m-set-category').value.trim()
        };

        let res;
        if (isNew) {
            res = await apiPost('/api/admin/settings', body);
        } else {
            res = await fetch(`/api/admin/settings/${s.id}`, {
                method: 'PUT',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            }).then(r => r.json());
        }

        if (!res.success) return { ok: false, message: res.message };
        toast('Настройка сохранена', 'success');
        await loadSettings();
        return { ok: true };
    });
}

async function resetSetting(id) {
    if (!confirm('Сбросить значение к значению по умолчанию?')) return;
    const res = await apiPost(`/api/admin/settings/${id}/reset`);
    if (!res.success) { toast(res.message || 'Ошибка', 'error'); return; }
    toast('Значение сброшено', 'success');
    await loadSettings();
}

async function deleteSetting(id) {
    if (!confirm('Удалить настройку?')) return;
    const res = await fetch(`/api/admin/settings/${id}`, {
        method: 'DELETE',
        credentials: 'same-origin'
    }).then(r => r.json());
    if (!res.success) { toast(res.message || 'Ошибка', 'error'); return; }
    toast('Настройка удалена', 'success');
    await loadSettings();
}

// ---------- Белый список ----------

async function loadWhitelist() {
    const res = await apiGet('/api/admin/whitelist');
    const ul = document.getElementById('whitelist-list');
    if (!res.success) {
        ul.innerHTML = `<li class="list-group-item text-danger">${escapeHtml(res.message)}</li>`;
        return;
    }
    state.whitelist = res.data || [];
    ul.innerHTML = state.whitelist.map(w => `
        <li class="list-group-item d-flex justify-content-between align-items-center">
            <div>
                <code>${escapeHtml(w.toolName)}</code>
                <div class="small text-muted">${escapeHtml(w.description || '')}</div>
            </div>
            <button class="btn btn-sm btn-outline-danger" data-tool="${escapeHtml(w.toolName)}">@Localizer["Убрать из белого списка"]</button>
        </li>
    `).join('') || '<li class="list-group-item text-muted">—</li>';

    ul.querySelectorAll('button[data-tool]').forEach(btn => {
        btn.addEventListener('click', () => removeFromWhitelist(btn.dataset.tool));
    });
}

async function loadAllTools() {
    const res = await apiGet('/api/tools');
    const select = document.getElementById('whitelist-tool-select');
    if (!res.success) return;
    state.tools = res.data || [];

    const whitelisted = new Set(state.whitelist.map(w => w.toolName));
    const options = state.tools
        .filter(t => !whitelisted.has(t.name))
        .map(t => `<option value="${escapeHtml(t.name)}">${escapeHtml(t.name)}</option>`)
        .join('');
    select.innerHTML = `<option value="">— Выбор инструмента —</option>${options}`;
}

async function addToWhitelist() {
    const select = document.getElementById('whitelist-tool-select');
    const toolName = select.value;
    if (!toolName) { toast('Инструмент не выбран', 'warning'); return; }

    const res = await apiPost('/api/admin/whitelist', { toolName });
    if (!res.success) { toast(res.message || 'Ошибка', 'error'); return; }
    toast('Инструмент добавлен в белый список', 'success');
    await loadWhitelist();
    await loadAllTools();
}

async function removeFromWhitelist(toolName) {
    if (!confirm(`Убрать "${toolName}" из белого списка?`)) return;
    const res = await fetch(`/api/admin/whitelist/${encodeURIComponent(toolName)}`, {
        method: 'DELETE',
        credentials: 'same-origin'
    }).then(r => r.json());
    if (!res.success) { toast(res.message || 'Ошибка', 'error'); return; }
    toast('Инструмент убран из белого списка', 'success');
    await loadWhitelist();
    await loadAllTools();
}

// ---------- Аудит ----------

async function loadAudit(page) {
    state.audit.page = page;
    const params = new URLSearchParams({
        page: page,
        pageSize: state.audit.pageSize
    });
    if (state.audit.toolFilter) params.set('toolName', state.audit.toolFilter);

    const res = await apiGet(`/api/admin/audit?${params.toString()}`);
    const tbody = document.getElementById('audit-tbody');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-danger text-center">${escapeHtml(res.message)}</td></tr>`;
        return;
    }

    const data = res.data;
    state.audit.totalPages = data.totalPages;

    tbody.innerHTML = (data.items || []).map(a => `
        <tr>
            <td>${a.id}</td>
            <td>${escapeHtml(a.userName)}</td>
            <td><code>${escapeHtml(a.toolName)}</code></td>
            <td>${statusBadge(a.status)}</td>
            <td>${a.durationMs} ms</td>
            <td>${new Date(a.createdAt).toLocaleString()}</td>
        </tr>
    `).join('') || '<tr><td colspan="6" class="text-muted text-center">—</td></tr>';

    renderPagination();
}

function renderPagination() {
    const nav = document.getElementById('audit-pagination');
    const total = state.audit.totalPages;
    const current = state.audit.page;
    if (total <= 1) { nav.innerHTML = ''; return; }

    const parts = [];
    parts.push(`<li class="page-item ${current === 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${current - 1}">«</a></li>`);

    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) {
        parts.push(`<li class="page-item ${i === current ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`);
    }

    parts.push(`<li class="page-item ${current === total ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${current + 1}">»</a></li>`);
    nav.innerHTML = parts.join('');

    nav.querySelectorAll('a[data-page]').forEach(a => {
        a.addEventListener('click', e => {
            e.preventDefault();
            const p = parseInt(a.dataset.page, 10);
            if (p >= 1 && p <= total) loadAudit(p);
        });
    });
}

function statusBadge(status) {
    const map = { Success: 'bg-success', Error: 'bg-danger', Pending: 'bg-warning text-dark', Cancelled: 'bg-secondary' };
    return `<span class="badge ${map[status] || 'bg-secondary'}">${escapeHtml(status)}</span>`;
}

// ---------- Модальное окно ----------

let _modalSaveHandler = null;

/**
 * Показывает универсальную модалку /admin.
 * Экспортируется для модуля admin-agents.js (v1.4.0 Фаза 6.5-6.6, KI-052).
 * @param {string} title Заголовок
 * @param {string} bodyHtml HTML тела
 * @param {Function} onSave Колбэк сохранения (возвращает { ok: boolean, message?: string })
 */
export function showModal(title, bodyHtml, onSave) {
    document.getElementById('adminModalTitle').textContent = title;
    document.getElementById('adminModalBody').innerHTML = bodyHtml;
    _modalSaveHandler = onSave;

    const modal = new bootstrap.Modal(document.getElementById('adminModal'));
    modal.show();
}

// ---------- Привязка кнопок ----------

function bindToolbarButtons() {
    document.getElementById('btn-refresh-users').addEventListener('click', loadUsers);
    document.getElementById('btn-add-user').addEventListener('click', () => openUserModal(0));

    document.getElementById('btn-refresh-settings').addEventListener('click', loadSettings);
    document.getElementById('btn-add-setting').addEventListener('click', () => openSettingModal(0));

    document.getElementById('btn-add-whitelist').addEventListener('click', addToWhitelist);

    document.getElementById('btn-refresh-audit').addEventListener('click', () => loadAudit(1));
    document.getElementById('btn-audit-filter').addEventListener('click', () => {
        state.audit.toolFilter = document.getElementById('audit-tool-filter').value.trim();
        loadAudit(1);
    });

    // Сохранение в универсальной модалке
    document.getElementById('adminModalSave').addEventListener('click', async () => {
        if (!_modalSaveHandler) return;
        const result = await _modalSaveHandler();
        if (result && result.ok === false) {
            toast(result.message || 'Ошибка', 'error');
            return;
        }
        bootstrap.Modal.getInstance(document.getElementById('adminModal')).hide();
    });
}