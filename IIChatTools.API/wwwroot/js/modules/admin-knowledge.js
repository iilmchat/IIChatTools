/**
 * Модуль вкладки «База знаний» в /admin (v1.5.0, KI-083, Шаг 7B).
 * Управление RAG Knowledge Base: 4 индекса, переиндексация project_docs,
 * просмотр/удаление чанков, редактирование настроек RAG.
 *
 * Endpoints:
 *   GET    /api/admin/knowledge/indexes
 *   POST   /api/admin/knowledge/indexes/project-docs/reindex
 *   GET    /api/admin/knowledge/chunks?index=&page=&pageSize=
 *   DELETE /api/admin/knowledge/chunks/{id}
 *   GET    /api/admin/knowledge/settings
 *   PUT    /api/admin/knowledge/settings
 *
 * Использует универсальную модалку из admin.js (showModal).
 * © 2026 RuChating (iilmchat) · IIChatTools v1.5.0
 */
import { apiGet } from './api.js';
import { escapeHtml, toast } from './ui.js';
import { showModal } from './admin.js';

// ---------- Состояние ----------
const state = {
    indexes: [],
    chunks: {
        indexName: '',
        page: 1,
        pageSize: 20,
        totalPages: 0,
        totalCount: 0
    }
};

/**
 * Инициализирует вкладку «База знаний».
 * Ленивая загрузка при первом открытии таба + кнопка «Обновить».
 */
export function initKnowledgeTab() {
    const tab = document.getElementById('tab-knowledge');
    if (!tab) return;

    tab.addEventListener('shown.bs.tab', () => loadIndexes(), { once: true });

    document.getElementById('btn-refresh-knowledge')
        ?.addEventListener('click', loadIndexes);

    document.getElementById('btn-reindex-project-docs')
        ?.addEventListener('click', reindexProjectDocs);

    document.getElementById('btn-rag-settings')
        ?.addEventListener('click', openSettingsModal);

    // Делегированные обработчики (устойчивы к перерисовке tbody).
    document.getElementById('knowledge-indexes-tbody')
        ?.addEventListener('click', onIndexesTbodyClick);

    document.getElementById('rag-chunks-tbody')
        ?.addEventListener('click', onChunksTbodyClick);

    document.getElementById('rag-chunks-pagination')
        ?.addEventListener('click', onChunksPaginationClick);
}

/**
 * Возвращает объект локализованных ярлыков из data-атрибутов pane-knowledge.
 * @returns {object}
 */
function paneLabels() {
    const pane = document.getElementById('pane-knowledge');
    return {
        reindexInProgress: pane?.dataset.labelReindexInProgress || 'Indexing…',
        reindexSuccess: pane?.dataset.labelReindexSuccess || 'Indexed {0} chunks in {1} s',
        noIndexes: pane?.dataset.labelNoIndexes || 'No indexes',
        viewChunks: pane?.dataset.labelViewChunks || 'View',
        chunksTitle: pane?.dataset.labelChunksTitle || 'Chunks viewer',
        chunksEmpty: pane?.dataset.labelChunksEmpty || 'No chunks',
        chunksTotal: pane?.dataset.labelChunksTotal || 'Total: {0}',
        deleteChunkConfirm: pane?.dataset.labelDeleteChunkConfirm || 'Delete this chunk?',
        deleteChunkSuccess: pane?.dataset.labelDeleteChunkSuccess || 'Chunk deleted',
        settingsTitle: pane?.dataset.labelSettingsTitle || 'RAG settings',
        settingsSaved: pane?.dataset.labelSettingsSaved || 'RAG settings saved',
        settingsChunkStrategy: pane?.dataset.labelSettingsChunkStrategy || 'Chunking strategy',
        settingsChunkSize: pane?.dataset.labelSettingsChunkSize || 'Chunk size (tokens)',
        settingsChunkOverlap: pane?.dataset.labelSettingsChunkOverlap || 'Overlap (tokens)',
        settingsMinChunkSize: pane?.dataset.labelSettingsMinChunkSize || 'Min chunk size',
        settingsDefaultTopK: pane?.dataset.labelSettingsDefaultTopk || 'Default TopK',
        settingsMinScore: pane?.dataset.labelSettingsMinScore || 'Min score',
        settingsEmbeddingModel: pane?.dataset.labelSettingsEmbeddingModel || 'Embedding model',
        settingsAutoIndex: pane?.dataset.labelSettingsAutoIndex || 'Auto-index on startup'
    };
}

// ---------- Таблица индексов ----------

async function loadIndexes() {
    const tbody = document.getElementById('knowledge-indexes-tbody');
    if (!tbody) return;

    tbody.innerHTML = '<tr><td colspan="5" class="text-center text-muted">…</td></tr>';

    const res = await apiGet('/api/admin/knowledge/indexes');
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="5" class="text-danger text-center">${escapeHtml(res.message || 'Error')}</td></tr>`;
        return;
    }

    state.indexes = res.data || [];
    renderIndexesTable();
}

function renderIndexesTable() {
    const tbody = document.getElementById('knowledge-indexes-tbody');
    if (!tbody) return;

    const labels = paneLabels();

    if (state.indexes.length === 0) {
        tbody.innerHTML = `<tr><td colspan="5" class="text-muted text-center">${escapeHtml(labels.noIndexes)}</td></tr>`;
        return;
    }

    tbody.innerHTML = state.indexes.map(idx => `
        <tr>
            <td><code>${escapeHtml(idx.name || '')}</code></td>
            <td>${idx.documentCount ?? 0}</td>
            <td>${idx.chunkCount ?? 0}</td>
            <td>${formatIndexedAt(idx.lastIndexedAt)}</td>
            <td>
                <button class="btn btn-sm btn-outline-secondary"
                        type="button"
                        data-action="view-chunks"
                        data-index="${escapeHtml(idx.name || '')}">${escapeHtml(labels.viewChunks)}</button>
            </td>
        </tr>
    `).join('');
}

/**
 * Форматирует дату последней индексации.
 * @param {string|null} iso
 * @returns {string}
 */
function formatIndexedAt(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleString();
}

// ---------- Reindex project_docs ----------

async function reindexProjectDocs() {
    const btn = document.getElementById('btn-reindex-project-docs');
    if (!btn) return;

    const labels = paneLabels();
    const originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = labels.reindexInProgress;

    try {
        const res = await fetch('/api/admin/knowledge/indexes/project-docs/reindex', {
            method: 'POST',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res || !res.success) {
            toast(res?.message || 'Error', 'error');
            return;
        }

        const data = res.data || {};
        const chunks = data.documentChunksCreated ?? 0;
        const durationSec = ((data.durationMs ?? 0) / 1000).toFixed(1);
        const msg = labels.reindexSuccess
            .replace('{0}', String(chunks))
            .replace('{1}', durationSec);
        toast(msg, 'success');

        await loadIndexes();
    } catch (ex) {
        toast(ex.message || 'Error', 'error');
    } finally {
        btn.disabled = false;
        btn.textContent = originalText;
    }
}

// ---------- Модалка просмотра чанков ----------

function onIndexesTbodyClick(e) {
    const btn = e.target.closest('[data-action="view-chunks"]');
    if (!btn) return;
    e.preventDefault();
    openChunksModal(btn.dataset.index || '');
}

async function openChunksModal(indexName) {
    if (!indexName) return;

    state.chunks.indexName = indexName;
    state.chunks.page = 1;

    const modalEl = document.getElementById('ragChunksModal');
    if (!modalEl) return;

    const titleEl = document.getElementById('ragChunksModalTitle');
    if (titleEl) {
        titleEl.textContent = `${paneLabels().chunksTitle}: ${indexName}`;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
    modal.show();

    await loadChunks();
}

async function loadChunks() {
    const tbody = document.getElementById('rag-chunks-tbody');
    if (!tbody) return;

    tbody.innerHTML = '<tr><td colspan="6" class="text-muted text-center">…</td></tr>';

    const params = new URLSearchParams({
        index: state.chunks.indexName,
        page: String(state.chunks.page),
        pageSize: String(state.chunks.pageSize)
    });

    const res = await apiGet(`/api/admin/knowledge/chunks?${params.toString()}`);
    if (!res.success) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-danger text-center">${escapeHtml(res.message || 'Error')}</td></tr>`;
        return;
    }

    const data = res.data || {};
    state.chunks.totalCount = data.totalCount || 0;
    state.chunks.totalPages = data.totalPages || 0;
    state.chunks.page = data.page || 1;

    renderChunksTable(data.items || []);
    renderChunksPagination();
    renderChunksTotal();
}

function renderChunksTable(items) {
    const tbody = document.getElementById('rag-chunks-tbody');
    if (!tbody) return;

    const labels = paneLabels();

    if (items.length === 0) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-muted text-center">${escapeHtml(labels.chunksEmpty)}</td></tr>`;
        return;
    }

    tbody.innerHTML = items.map(c => `
        <tr>
            <td>${c.id}</td>
            <td><code class="rag-chunk-path">${escapeHtml(c.documentPath || '—')}</code></td>
            <td>${c.chunkIndex ?? 0}</td>
            <td>${c.tokens ?? 0}</td>
            <td class="rag-chunk-preview" title="${escapeHtml(c.textPreview || '')}">${escapeHtml(c.textPreview || '')}</td>
            <td>
                <button class="btn btn-sm btn-outline-danger"
                        type="button"
                        data-action="delete-chunk"
                        data-chunk-id="${c.id}"
                        title="${escapeHtml(labels.deleteChunkConfirm)}"
                        aria-label="${escapeHtml(labels.deleteChunkConfirm)}">✕</button>
            </td>
        </tr>
    `).join('');
}

function renderChunksPagination() {
    const nav = document.getElementById('rag-chunks-pagination');
    if (!nav) return;

    const total = state.chunks.totalPages;
    const current = state.chunks.page;

    if (total <= 1) {
        nav.innerHTML = '';
        return;
    }

    const parts = [];
    parts.push(`<li class="page-item ${current === 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${current - 1}">«</a></li>`);

    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) {
        parts.push(`<li class="page-item ${i === current ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`);
    }

    parts.push(`<li class="page-item ${current === total ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${current + 1}">»</a></li>`);
    nav.innerHTML = parts.join('');
}

function renderChunksTotal() {
    const el = document.getElementById('rag-chunks-total');
    if (!el) return;
    const tpl = paneLabels().chunksTotal;
    el.textContent = tpl.replace('{0}', String(state.chunks.totalCount));
}

function onChunksPaginationClick(e) {
    const link = e.target.closest('a[data-page]');
    if (!link) return;
    e.preventDefault();

    const p = parseInt(link.dataset.page, 10);
    if (!Number.isFinite(p) || p < 1 || p > state.chunks.totalPages) return;

    state.chunks.page = p;
    loadChunks();
}

async function onChunksTbodyClick(e) {
    const btn = e.target.closest('[data-action="delete-chunk"]');
    if (!btn) return;
    e.preventDefault();

    const chunkId = parseInt(btn.dataset.chunkId, 10);
    if (!Number.isFinite(chunkId)) return;

    const labels = paneLabels();
    if (!confirm(labels.deleteChunkConfirm)) return;

    try {
        const res = await fetch(`/api/admin/knowledge/chunks/${chunkId}`, {
            method: 'DELETE',
            credentials: 'same-origin'
        }).then(r => r.json());

        if (!res || !res.success) {
            toast(res?.message || 'Error', 'error');
            return;
        }

        toast(labels.deleteChunkSuccess, 'success');

        // Обновляем и чанки, и таблицу индексов (счётчики).
        await loadChunks();
        await loadIndexes();
    } catch (ex) {
        toast(ex.message || 'Error', 'error');
    }
}

// ---------- Модалка настроек RAG ----------

async function openSettingsModal() {
    const res = await apiGet('/api/admin/knowledge/settings');
    if (!res.success) {
        toast(res.message || 'Error', 'error');
        return;
    }

    const dto = res.data || {};
    const labels = paneLabels();

    showModal(labels.settingsTitle, `
        <div class="mb-2">
            <label class="form-label small" for="rag-set-strategy">${escapeHtml(labels.settingsChunkStrategy)}</label>
            <select class="form-select form-select-sm" id="rag-set-strategy">
                <option value="recursive">recursive</option>
                <option value="sentence">sentence</option>
                <option value="fixed">fixed</option>
            </select>
        </div>
        <div class="row g-2 mb-2">
            <div class="col-4">
                <label class="form-label small" for="rag-set-chunksize">${escapeHtml(labels.settingsChunkSize)}</label>
                <input type="number" class="form-control form-control-sm" id="rag-set-chunksize" min="100" max="2000">
            </div>
            <div class="col-4">
                <label class="form-label small" for="rag-set-overlap">${escapeHtml(labels.settingsChunkOverlap)}</label>
                <input type="number" class="form-control form-control-sm" id="rag-set-overlap" min="0" max="500">
            </div>
            <div class="col-4">
                <label class="form-label small" for="rag-set-minsize">${escapeHtml(labels.settingsMinChunkSize)}</label>
                <input type="number" class="form-control form-control-sm" id="rag-set-minsize" min="0" max="2000">
            </div>
        </div>
        <div class="row g-2 mb-2">
            <div class="col-6">
                <label class="form-label small" for="rag-set-topk">${escapeHtml(labels.settingsDefaultTopK)}</label>
                <input type="number" class="form-control form-control-sm" id="rag-set-topk" min="1" max="20">
            </div>
            <div class="col-6">
                <label class="form-label small" for="rag-set-minscore">${escapeHtml(labels.settingsMinScore)}</label>
                <input type="number" step="0.05" class="form-control form-control-sm" id="rag-set-minscore" min="0" max="1">
            </div>
        </div>
        <div class="mb-2">
            <label class="form-label small" for="rag-set-embedding">${escapeHtml(labels.settingsEmbeddingModel)}</label>
            <input type="text" class="form-control form-control-sm font-monospace" id="rag-set-embedding">
        </div>
        <div class="form-check">
            <input type="checkbox" class="form-check-input" id="rag-set-autoindex">
            <label class="form-check-label small" for="rag-set-autoindex">${escapeHtml(labels.settingsAutoIndex)}</label>
        </div>
    `, async () => {
        const body = {
            chunkingStrategy: document.getElementById('rag-set-strategy').value,
            chunkSize: parseInt(document.getElementById('rag-set-chunksize').value, 10),
            chunkOverlap: parseInt(document.getElementById('rag-set-overlap').value, 10),
            minChunkSize: parseInt(document.getElementById('rag-set-minsize').value, 10),
            defaultTopK: parseInt(document.getElementById('rag-set-topk').value, 10),
            minScore: parseFloat(document.getElementById('rag-set-minscore').value),
            embeddingModel: document.getElementById('rag-set-embedding').value.trim(),
            autoIndexProjectDocs: document.getElementById('rag-set-autoindex').checked
        };

        const r = await fetch('/api/admin/knowledge/settings', {
            method: 'PUT',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(x => x.json());

        if (!r || !r.success) {
            return { ok: false, message: r?.message || 'Error' };
        }

        toast(labels.settingsSaved, 'success');
        return { ok: true };
    });

    // Заполняем поля после отображения модалки (showModal вставляет HTML синхронно,
    // но поля становятся доступны только после следующего tick).
    setTimeout(() => {
        setValue('rag-set-strategy', dto.chunkingStrategy || 'recursive');
        setValue('rag-set-chunksize', dto.chunkSize ?? 500);
        setValue('rag-set-overlap', dto.chunkOverlap ?? 64);
        setValue('rag-set-minsize', dto.minChunkSize ?? 100);
        setValue('rag-set-topk', dto.defaultTopK ?? 5);
        setValue('rag-set-minscore', dto.minScore ?? 0.3);
        setValue('rag-set-embedding', dto.embeddingModel || '');
        const cb = document.getElementById('rag-set-autoindex');
        if (cb) cb.checked = !!dto.autoIndexProjectDocs;
    }, 0);
}

/**
 * Устанавливает value элемента по id (защита от null).
 * @param {string} id
 * @param {*} value
 */
function setValue(id, value) {
    const el = document.getElementById(id);
    if (el) el.value = value;
}