(function() {
    const basePath = window.location.pathname.replace(/\/$/, '');
    const api = (endpoint) => fetch(`${basePath}/api/${endpoint}`).then(r => r.json());
    const $ = (sel) => document.querySelector(sel);
    const content = $('#content');
    let currentView = 'resources';

    // Navigation
    document.querySelectorAll('nav a').forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            document.querySelectorAll('nav a').forEach(a => a.classList.remove('active'));
            link.classList.add('active');
            currentView = link.dataset.view;
            loadView(currentView);
        });
    });

    // Load stats
    async function loadStats() {
        const stats = await api('stats');
        $('#stats').innerHTML = Object.entries(stats).map(([k, v]) =>
            `<div class="stat-card"><div class="label">${k}</div><div class="value">${v}</div></div>`
        ).join('');
    }

    // Load views
    async function loadView(view) {
        content.innerHTML = '<div class="loading">Loading...</div>';
        switch(view) {
            case 'resources': return loadResources();
            case 'principals': return loadPrincipals();
            case 'grants': return loadGrants();
            case 'roles': return loadRoles();
            case 'permissions': return loadPermissions();
            case 'access-tester': return loadAccessTester();
        }
    }

    // --- Shared pagination helpers ---

    function renderPagination(page, totalPages, totalCount) {
        if (totalPages <= 1) return `<div class="pagination-info">${totalCount} item${totalCount !== 1 ? 's' : ''}</div>`;
        let html = '<div class="pagination">';
        html += `<button class="pg-btn" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>Prev</button>`;
        // Show page numbers with ellipsis
        const pages = buildPageNumbers(page, totalPages);
        pages.forEach(p => {
            if (p === '...') {
                html += '<span class="pg-ellipsis">...</span>';
            } else {
                html += `<button class="pg-btn ${p === page ? 'pg-active' : ''}" data-page="${p}">${p}</button>`;
            }
        });
        html += `<button class="pg-btn" data-page="${page + 1}" ${page >= totalPages ? 'disabled' : ''}>Next</button>`;
        html += `<span class="pg-info">${totalCount} item${totalCount !== 1 ? 's' : ''}</span>`;
        html += '</div>';
        return html;
    }

    function buildPageNumbers(current, total) {
        if (total <= 7) return Array.from({length: total}, (_, i) => i + 1);
        const pages = [];
        pages.push(1);
        if (current > 3) pages.push('...');
        for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) pages.push(i);
        if (current < total - 2) pages.push('...');
        pages.push(total);
        return pages;
    }

    function bindPagination(containerSel, callback) {
        document.querySelectorAll(`${containerSel} .pg-btn:not([disabled])`).forEach(btn => {
            btn.addEventListener('click', () => callback(parseInt(btn.dataset.page)));
        });
    }

    function renderSearchBox(id, placeholder) {
        return `<div class="search-box"><input type="text" placeholder="${placeholder}" id="${id}"></div>`;
    }

    // --- Resource Tree (lazy-loading) ---

    let treeNodes = new Map(); // id -> node state
    let treeRootIds = [];

    async function loadResources() {
        const result = await api('resources/tree?maxDepth=2');
        treeNodes = new Map();
        treeRootIds = result.rootIds;

        // Build node state from initial load
        result.nodes.forEach(n => {
            treeNodes.set(n.id, {
                ...n,
                expanded: false,
                childrenLoaded: false,
                childrenPage: 1,
                hasMoreChildren: false,
                isLoading: false
            });
        });

        // Mark nodes whose children were included in initial load as loaded
        result.nodes.forEach(n => {
            if (n.parentId && treeNodes.has(n.parentId)) {
                const parent = treeNodes.get(n.parentId);
                if (!parent.childrenLoaded) {
                    parent.childrenLoaded = true;
                    // Check if all children were loaded
                    const loadedChildCount = result.nodes.filter(c => c.parentId === n.parentId).length;
                    parent.hasMoreChildren = loadedChildCount < parent.childCount;
                }
            }
        });

        // Auto-expand roots
        treeRootIds.forEach(id => {
            if (treeNodes.has(id)) treeNodes.get(id).expanded = true;
        });

        renderResourceTree();
    }

    function renderResourceTree() {
        content.innerHTML = `<div class="card"><h3 style="margin-bottom:1rem">Resource Hierarchy</h3><div id="tree"></div></div>`;
        $('#tree').innerHTML = treeRootIds.map(id => renderTreeNode(id)).join('');
        bindTreeEvents();
    }

    function renderTreeNode(nodeId) {
        const n = treeNodes.get(nodeId);
        if (!n) return '';
        const hasChildren = n.childCount > 0;
        const toggleIcon = !hasChildren ? '&nbsp;' : (n.expanded ? '&#9662;' : '&#9656;');
        const childIds = getChildIds(nodeId);

        let html = `<div class="tree-node" data-id="${esc(n.id)}">
            <span class="toggle" data-id="${esc(n.id)}">${toggleIcon}</span>
            <strong>${esc(n.name)}</strong>
            <span class="badge badge-blue">${esc(n.resourceType)}</span>`;
        if (n.childCount > 0) html += `<span class="badge badge-gray">${n.childCount} children</span>`;
        if (n.grantsCount > 0) html += `<span class="badge badge-green">${n.grantsCount} grants</span>`;
        html += `<span class="tree-id">${esc(n.id)}</span>`;

        if (hasChildren && n.expanded) {
            html += '<div class="tree-children">';
            if (n.isLoading && childIds.length === 0) {
                html += '<div class="tree-loading">Loading...</div>';
            } else {
                html += childIds.map(cid => renderTreeNode(cid)).join('');
                if (n.isLoading) html += '<div class="tree-loading">Loading more...</div>';
                if (n.hasMoreChildren && !n.isLoading) {
                    const loaded = childIds.length;
                    const remaining = n.childCount - loaded;
                    html += `<button class="tree-load-more" data-id="${esc(n.id)}">Load more (${remaining} remaining)</button>`;
                }
            }
            html += '</div>';
        }
        html += '</div>';
        return html;
    }

    function getChildIds(parentId) {
        const ids = [];
        treeNodes.forEach((node, id) => {
            if (node.parentId === parentId) ids.push(id);
        });
        // Sort by name
        ids.sort((a, b) => (treeNodes.get(a).name || '').localeCompare(treeNodes.get(b).name || ''));
        return ids;
    }

    function bindTreeEvents() {
        document.querySelectorAll('.toggle[data-id]').forEach(el => {
            el.addEventListener('click', () => handleToggle(el.dataset.id));
        });
        document.querySelectorAll('.tree-load-more[data-id]').forEach(el => {
            el.addEventListener('click', () => handleLoadMore(el.dataset.id));
        });
    }

    async function handleToggle(nodeId) {
        const node = treeNodes.get(nodeId);
        if (!node || node.childCount === 0) return;

        node.expanded = !node.expanded;

        // If expanding and children not loaded, fetch them
        if (node.expanded && !node.childrenLoaded) {
            node.isLoading = true;
            renderResourceTree();
            try {
                const result = await api(`resources/${encodeURIComponent(nodeId)}/children?page=1&pageSize=50`);
                result.data.forEach(child => {
                    if (!treeNodes.has(child.id)) {
                        treeNodes.set(child.id, {
                            ...child,
                            expanded: false,
                            childrenLoaded: false,
                            childrenPage: 1,
                            hasMoreChildren: false,
                            isLoading: false
                        });
                    }
                });
                node.childrenLoaded = true;
                node.childrenPage = 1;
                node.hasMoreChildren = result.hasNextPage;
            } catch (e) {
                console.error('Failed to load children:', e);
            }
            node.isLoading = false;
        }

        renderResourceTree();
    }

    async function handleLoadMore(nodeId) {
        const node = treeNodes.get(nodeId);
        if (!node || !node.hasMoreChildren) return;

        node.isLoading = true;
        renderResourceTree();

        try {
            const nextPage = node.childrenPage + 1;
            const result = await api(`resources/${encodeURIComponent(nodeId)}/children?page=${nextPage}&pageSize=50`);
            result.data.forEach(child => {
                if (!treeNodes.has(child.id)) {
                    treeNodes.set(child.id, {
                        ...child,
                        expanded: false,
                        childrenLoaded: false,
                        childrenPage: 1,
                        hasMoreChildren: false,
                        isLoading: false
                    });
                }
            });
            node.childrenPage = nextPage;
            node.hasMoreChildren = result.hasNextPage;
        } catch (e) {
            console.error('Failed to load more children:', e);
        }
        node.isLoading = false;
        renderResourceTree();
    }

    // --- Paginated table views ---

    async function loadPrincipals(type, page, search) {
        type = type || 'user';
        page = page || 1;
        search = search || '';
        const params = `type=${type}&page=${page}&pageSize=25${search ? `&search=${encodeURIComponent(search)}` : ''}`;
        const result = await api(`principals?${params}`);

        content.innerHTML = `<div class="card">
            <div class="tabs">
                <button data-type="user" class="${type==='user'?'active':''}">Users</button>
                <button data-type="group" class="${type==='group'?'active':''}">Groups</button>
                <button data-type="service_account" class="${type==='service_account'?'active':''}">Service Accounts</button>
            </div>
            ${renderSearchBox('principals-search', 'Search principals...')}
            <table><thead><tr><th>Display Name</th><th>ID</th><th>Type</th><th>Created</th></tr></thead>
            <tbody>${result.data.map(p => `<tr class="principal-row" data-id="${esc(p.id)}" style="cursor:pointer">
                <td>${esc(p.displayName)}</td><td style="font-size:0.8rem;color:#888">${esc(p.id)}</td>
                <td><span class="badge badge-green">${esc(p.principalType)}</span></td>
                <td>${new Date(p.createdAt).toLocaleDateString()}</td>
            </tr>`).join('')}</tbody></table>
            <div id="principals-pagination">${renderPagination(result.page, result.totalPages, result.totalCount)}</div>
        </div>`;

        document.querySelectorAll('.tabs button').forEach(b => {
            b.addEventListener('click', () => loadPrincipals(b.dataset.type, 1, ''));
        });
        document.querySelectorAll('.principal-row').forEach(row => {
            row.addEventListener('click', () => loadPrincipalDetail(row.dataset.id));
        });
        bindPagination('#principals-pagination', (p) => loadPrincipals(type, p, search));
        const searchInput = $('#principals-search');
        if (searchInput) {
            searchInput.value = search;
            let debounce;
            searchInput.addEventListener('input', (e) => {
                clearTimeout(debounce);
                debounce = setTimeout(() => loadPrincipals(type, 1, e.target.value), 300);
            });
        }
    }

    // --- Principal Detail ---

    async function loadPrincipalDetail(principalId) {
        content.innerHTML = '<div class="loading">Loading...</div>';
        const [detail, grantsResult] = await Promise.all([
            api(`principals/${encodeURIComponent(principalId)}`),
            api(`principals/${encodeURIComponent(principalId)}/grants?page=1&pageSize=25`)
        ]);
        renderPrincipalDetail(detail, grantsResult);
    }

    function renderPrincipalDetail(detail, grantsResult) {
        const p = detail.principal;
        let html = `<div class="detail-header">
            <button class="detail-back" id="back-to-principals">&larr; Back to Principals</button>
            <h2>${esc(p.displayName)}</h2>
            <span class="badge badge-green">${esc(p.principalType)}</span>
        </div>`;

        // Info card
        html += `<div class="detail-grid">
            <div class="card detail-info">
                <h3>Principal Info</h3>
                <dl class="detail-dl">
                    <dt>ID</dt><dd><code>${esc(p.id)}</code></dd>
                    <dt>Display Name</dt><dd>${esc(p.displayName)}</dd>
                    <dt>Type</dt><dd>${esc(p.principalType)}</dd>
                    ${p.organizationId ? `<dt>Organization</dt><dd>${esc(p.organizationId)}</dd>` : ''}
                    ${p.externalRef ? `<dt>External Ref</dt><dd>${esc(p.externalRef)}</dd>` : ''}
                    <dt>Created</dt><dd>${new Date(p.createdAt).toLocaleString()}</dd>
                    <dt>Updated</dt><dd>${new Date(p.updatedAt).toLocaleString()}</dd>
                </dl>
            </div>`;

        // Groups / Members sidebar
        html += `<div class="card detail-sidebar">`;
        if (detail.groups.length > 0) {
            html += `<h3>Member Of</h3>
                <div class="detail-tags">
                    ${detail.groups.map(g => `<span class="badge badge-blue" style="margin:2px;cursor:pointer" data-group-principal="${esc(g.principalId)}">${esc(g.name)}${g.groupType ? ` (${esc(g.groupType)})` : ''}</span>`).join('')}
                </div>`;
        }
        if (detail.members.length > 0) {
            html += `<h3 ${detail.groups.length > 0 ? 'style="margin-top:1rem"' : ''}>Group Members</h3>
                <table><thead><tr><th>Name</th><th>Type</th></tr></thead>
                <tbody>${detail.members.map(m => `<tr class="member-row" data-id="${esc(m.id)}" style="cursor:pointer">
                    <td>${esc(m.displayName)}</td>
                    <td><span class="badge badge-green">${esc(m.principalTypeId)}</span></td>
                </tr>`).join('')}</tbody></table>`;
        }
        if (detail.groups.length === 0 && detail.members.length === 0) {
            html += `<h3>Groups</h3><p style="color:#888;font-size:0.9rem">Not a member of any group.</p>`;
        }
        html += `</div></div>`;

        // Grants table
        html += `<div class="card" style="margin-top:1rem">
            <h3 style="margin-bottom:1rem">Role Grants</h3>
            ${renderPrincipalGrantsTable(grantsResult)}
        </div>`;

        content.innerHTML = html;
        bindPrincipalDetailEvents(p.id);
    }

    function renderPrincipalGrantsTable(result) {
        if (result.data.length === 0) {
            return '<p style="color:#888;font-size:0.9rem">No grants found for this principal.</p>';
        }
        let html = `<table><thead><tr>
            <th>Role</th><th>Resource</th><th>Effective From</th><th>Effective To</th><th>Created</th>
        </tr></thead><tbody>`;
        result.data.forEach(g => {
            html += `<tr>
                <td><span class="badge badge-blue">${esc(g.roleName)}</span></td>
                <td>${esc(g.resourceName)}<span class="tree-id">${esc(g.resourceId)}</span></td>
                <td>${g.effectiveFrom ? new Date(g.effectiveFrom).toLocaleDateString() : '-'}</td>
                <td>${g.effectiveTo ? new Date(g.effectiveTo).toLocaleDateString() : '-'}</td>
                <td>${new Date(g.createdAt).toLocaleDateString()}</td>
            </tr>`;
        });
        html += `</tbody></table>`;
        html += `<div id="principal-grants-pagination">${renderPagination(result.page, result.totalPages, result.totalCount)}</div>`;
        return html;
    }

    function bindPrincipalDetailEvents(principalId) {
        $('#back-to-principals').addEventListener('click', () => loadPrincipals());
        // Paginate grants
        bindPagination('#principal-grants-pagination', async (page) => {
            const grantsResult = await api(`principals/${encodeURIComponent(principalId)}/grants?page=${page}&pageSize=25`);
            const grantsCard = document.querySelector('#principal-grants-pagination').closest('.card');
            grantsCard.innerHTML = '<h3 style="margin-bottom:1rem">Role Grants</h3>' + renderPrincipalGrantsTable(grantsResult);
            bindPagination('#principal-grants-pagination', async (p) => {
                const r = await api(`principals/${encodeURIComponent(principalId)}/grants?page=${p}&pageSize=25`);
                const card = document.querySelector('#principal-grants-pagination').closest('.card');
                card.innerHTML = '<h3 style="margin-bottom:1rem">Role Grants</h3>' + renderPrincipalGrantsTable(r);
                bindPrincipalDetailEvents(principalId);
            });
        });
        // Click group badges to navigate to that group's detail
        document.querySelectorAll('[data-group-principal]').forEach(el => {
            el.addEventListener('click', () => loadPrincipalDetail(el.dataset.groupPrincipal));
        });
        // Click member rows to navigate to that member's detail
        document.querySelectorAll('.member-row').forEach(row => {
            row.addEventListener('click', () => loadPrincipalDetail(row.dataset.id));
        });
    }

    async function loadGrants(page, search) {
        page = page || 1;
        search = search || '';
        const params = `page=${page}&pageSize=25${search ? `&search=${encodeURIComponent(search)}` : ''}`;
        const result = await api(`grants?${params}`);

        content.innerHTML = `<div class="card">
            ${renderSearchBox('grants-search', 'Search grants...')}
            <table><thead><tr>
                <th>Principal</th><th>Role</th><th>Resource</th><th>Effective From</th><th>Effective To</th><th>Created</th>
            </tr></thead><tbody>${result.data.map(g => `<tr>
                <td>${esc(g.principalName)}</td><td><span class="badge badge-blue">${esc(g.roleName)}</span></td>
                <td>${esc(g.resourceName)}</td>
                <td>${g.effectiveFrom ? new Date(g.effectiveFrom).toLocaleDateString() : '-'}</td>
                <td>${g.effectiveTo ? new Date(g.effectiveTo).toLocaleDateString() : '-'}</td>
                <td>${new Date(g.createdAt).toLocaleDateString()}</td>
            </tr>`).join('')}</tbody></table>
            <div id="grants-pagination">${renderPagination(result.page, result.totalPages, result.totalCount)}</div>
        </div>`;

        bindPagination('#grants-pagination', (p) => loadGrants(p, search));
        const searchInput = $('#grants-search');
        if (searchInput) {
            searchInput.value = search;
            let debounce;
            searchInput.addEventListener('input', (e) => {
                clearTimeout(debounce);
                debounce = setTimeout(() => loadGrants(1, e.target.value), 300);
            });
        }
    }

    async function loadRoles(page, search) {
        page = page || 1;
        search = search || '';
        const params = `page=${page}&pageSize=25${search ? `&search=${encodeURIComponent(search)}` : ''}`;
        const result = await api(`roles?${params}`);

        content.innerHTML = `<div class="card">
            ${renderSearchBox('roles-search', 'Search roles...')}
            <table><thead><tr>
                <th>Name</th><th>Key</th><th>Permissions</th><th>Virtual</th>
            </tr></thead><tbody>${result.data.map(r => `<tr class="role-row" data-id="${esc(r.id)}" style="cursor:pointer">
                <td>${esc(r.name)}</td><td><code>${esc(r.key)}</code></td>
                <td><span class="badge badge-gray">${r.permissionCount}</span></td>
                <td>${r.isVirtual ? 'Yes' : 'No'}</td>
            </tr><tr class="role-perms" data-for="${esc(r.id)}" style="display:none"><td colspan="4" style="padding:0.5rem 2rem;background:#fafafa"></td></tr>`).join('')}</tbody></table>
            <div id="roles-pagination">${renderPagination(result.page, result.totalPages, result.totalCount)}</div>
        </div>`;

        document.querySelectorAll('.role-row').forEach(row => {
            row.addEventListener('click', async () => {
                const permsRow = document.querySelector(`.role-perms[data-for="${row.dataset.id}"]`);
                if (permsRow.style.display === 'none') {
                    const perms = await api(`roles/${row.dataset.id}/permissions`);
                    permsRow.querySelector('td').innerHTML = perms.length
                        ? perms.map(p => `<span class="badge badge-blue" style="margin:2px">${esc(p.key)}</span>`).join(' ')
                        : '<em>No permissions</em>';
                    permsRow.style.display = '';
                } else {
                    permsRow.style.display = 'none';
                }
            });
        });
        bindPagination('#roles-pagination', (p) => loadRoles(p, search));
        const searchInput = $('#roles-search');
        if (searchInput) {
            searchInput.value = search;
            let debounce;
            searchInput.addEventListener('input', (e) => {
                clearTimeout(debounce);
                debounce = setTimeout(() => loadRoles(1, e.target.value), 300);
            });
        }
    }

    async function loadPermissions(page, search) {
        page = page || 1;
        search = search || '';
        const params = `page=${page}&pageSize=25${search ? `&search=${encodeURIComponent(search)}` : ''}`;
        const result = await api(`permissions?${params}`);

        content.innerHTML = `<div class="card">
            ${renderSearchBox('perm-search', 'Search permissions...')}
            <table><thead><tr><th>Key</th><th>Name</th><th>Resource Type</th></tr></thead>
            <tbody>${result.data.map(p => `<tr>
                <td><code>${esc(p.key)}</code></td><td>${esc(p.name)}</td>
                <td>${p.resourceType ? `<span class="badge badge-green">${esc(p.resourceType)}</span>` : '-'}</td>
            </tr>`).join('')}</tbody></table>
            <div id="perms-pagination">${renderPagination(result.page, result.totalPages, result.totalCount)}</div>
        </div>`;

        bindPagination('#perms-pagination', (p) => loadPermissions(p, search));
        const searchInput = $('#perm-search');
        if (searchInput) {
            searchInput.value = search;
            let debounce;
            searchInput.addEventListener('input', (e) => {
                clearTimeout(debounce);
                debounce = setTimeout(() => loadPermissions(1, e.target.value), 300);
            });
        }
    }

    // --- Access Tester ---

    async function loadAccessTester() {
        const [principalsResult, permissionsResult, treeResult] = await Promise.all([
            api('principals?pageSize=100'), api('permissions?pageSize=100'), api('resources/tree?maxDepth=5')
        ]);
        const principals = principalsResult.data;
        const permissions = permissionsResult.data;
        const resources = treeResult.nodes;

        content.innerHTML = `<div class="card">
            <h3 style="margin-bottom:1rem">Access Tester</h3>
            <p style="color:#666;margin-bottom:1.5rem;font-size:0.9rem">
                Test whether a principal has a specific permission on a resource. Returns a detailed trace of how the decision was made.
            </p>
            <div class="tester-form">
                <div class="tester-field">
                    <label>Principal</label>
                    <select id="tester-principal">
                        <option value="">Select a principal...</option>
                        ${principals.map(p => `<option value="${esc(p.id)}">${esc(p.displayName)} (${esc(p.principalTypeId)})</option>`).join('')}
                    </select>
                </div>
                <div class="tester-field">
                    <label>Permission</label>
                    <select id="tester-permission">
                        <option value="">Select a permission...</option>
                        ${permissions.map(p => `<option value="${esc(p.key)}">${esc(p.key)} — ${esc(p.name)}</option>`).join('')}
                    </select>
                </div>
                <div class="tester-field">
                    <label>Resource</label>
                    <select id="tester-resource">
                        <option value="">Select a resource...</option>
                        ${resources.map(r => `<option value="${esc(r.id)}">${esc(r.name)} (${esc(r.resourceType)})</option>`).join('')}
                    </select>
                </div>
                <button id="tester-run" class="tester-btn">Test Access</button>
            </div>
            <div id="tester-result"></div>
        </div>`;

        $('#tester-run').addEventListener('click', runAccessTest);
    }

    async function runAccessTest() {
        const principalId = $('#tester-principal').value;
        const permissionKey = $('#tester-permission').value;
        const resourceId = $('#tester-resource').value;

        if (!principalId || !permissionKey || !resourceId) {
            $('#tester-result').innerHTML = '<div class="tester-error">Please select a principal, permission, and resource.</div>';
            return;
        }

        $('#tester-result').innerHTML = '<div class="loading">Running trace...</div>';
        $('#tester-run').disabled = true;

        try {
            const trace = await fetch(`${basePath}/api/trace`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ principalId, permissionKey, resourceId })
            }).then(r => r.json());

            $('#tester-result').innerHTML = renderTraceResult(trace);
        } catch (e) {
            $('#tester-result').innerHTML = `<div class="tester-error">Error: ${e.message}</div>`;
        } finally {
            $('#tester-run').disabled = false;
        }
    }

    function renderTraceResult(t) {
        const granted = t.accessGranted;
        const bannerClass = granted ? 'trace-banner-granted' : 'trace-banner-denied';
        const bannerIcon = granted ? '\u2713' : '\u2717';
        const bannerText = granted ? 'ACCESS GRANTED' : 'ACCESS DENIED';

        let html = `<div class="trace-result">`;
        html += `<div class="trace-banner ${bannerClass}">
            <span class="trace-banner-icon">${bannerIcon}</span> ${bannerText}
        </div>`;

        html += `<div class="trace-section">
            <div class="trace-section-title">Decision Summary</div>
            <div class="trace-summary-text">${esc(t.decisionSummary)}</div>`;
        if (t.denialReason) html += `<div class="trace-denial">${esc(t.denialReason)}</div>`;
        if (t.suggestion) html += `<div class="trace-suggestion">${esc(t.suggestion)}</div>`;
        html += `</div>`;

        if (t.principalsChecked && t.principalsChecked.length > 0) {
            html += `<div class="trace-section">
                <div class="trace-section-title">Principals Checked</div>
                <div class="trace-principals">
                    ${t.principalsChecked.map(p => `<span class="badge ${p.isDirect ? 'badge-blue' : 'badge-green'}">${esc(p.displayName)} (${esc(p.type)})${p.isDirect ? '' : ' — via group'}</span>`).join(' ')}
                </div>
            </div>`;
        }

        if (t.pathNodes && t.pathNodes.length > 0) {
            html += `<div class="trace-section">
                <div class="trace-section-title">Resource Path &amp; Grants</div>
                <div class="trace-path">`;
            t.pathNodes.forEach((node, i) => {
                html += `<div class="trace-path-node">
                    <div class="trace-path-connector">${i > 0 ? '<div class="trace-path-line"></div>' : ''}</div>
                    <div class="trace-path-content">
                        <div class="trace-path-header">
                            <strong>${esc(node.name)}</strong>
                            <span class="badge badge-gray">${esc(node.resourceType)}</span>
                            ${node.isTarget ? '<span class="badge badge-blue">Target</span>' : ''}
                            ${node.permissionFoundHere ? '<span class="badge badge-green">\u2713 Permission found here</span>' : ''}
                        </div>
                        <div class="trace-path-id">${esc(node.resourceId)}</div>`;
                if (node.grantsOnThisNode && node.grantsOnThisNode.length > 0) {
                    html += `<div class="trace-path-grants">`;
                    node.grantsOnThisNode.forEach(g => {
                        const grantClass = g.contributedToDecision ? 'trace-grant-contributed' : '';
                        html += `<div class="trace-grant ${grantClass}">
                            <span class="badge badge-blue">${esc(g.roleName)}</span>
                            <span style="margin:0 0.3rem">\u2192</span>
                            <span>${esc(g.principalDisplayName)}</span>
                            ${g.viaGroupName ? `<span class="trace-via-group">via ${esc(g.viaGroupName)}</span>` : ''}
                            ${g.contributedToDecision ? '<span class="badge badge-green" style="margin-left:0.5rem">\u2713</span>' : ''}
                        </div>`;
                    });
                    html += `</div>`;
                }
                if (node.effectivePermissions && node.effectivePermissions.length > 0) {
                    html += `<div class="trace-path-perms">
                        ${node.effectivePermissions.map(p => `<span class="badge ${p === t.permissionKey ? 'badge-green' : 'badge-gray'}" style="margin:2px">${esc(p)}</span>`).join('')}
                    </div>`;
                }
                html += `</div></div>`;
            });
            html += `</div></div>`;
        }

        if (t.allRolesUsed && t.allRolesUsed.length > 0) {
            html += `<div class="trace-section">
                <div class="trace-section-title">Roles &amp; Permissions Used</div>
                <table class="trace-roles-table"><thead><tr>
                    <th>Role</th><th>Source</th><th>Permissions</th><th>Match?</th>
                </tr></thead><tbody>`;
            t.allRolesUsed.forEach(r => {
                const rowClass = r.contributedToDecision ? 'trace-role-contributed' : '';
                const permBadges = r.permissions.slice(0, 8).map(p =>
                    `<span class="badge ${p.usedForDecision ? 'badge-green' : 'badge-gray'}" style="margin:2px">${esc(p.permissionKey)}</span>`
                ).join('');
                const moreCount = r.permissions.length > 8 ? `<span class="badge badge-gray" style="margin:2px">+${r.permissions.length - 8} more</span>` : '';
                html += `<tr class="${rowClass}">
                    <td><strong>${esc(r.roleName)}</strong> <code>${esc(r.roleKey)}</code>${r.isVirtualRole ? ' <em>(virtual)</em>' : ''}</td>
                    <td>${r.sourceResourceName ? `${esc(r.sourceResourceName)} <span class="badge badge-gray">${esc(r.sourceResourceType || '')}</span>` : '-'}</td>
                    <td>${permBadges}${moreCount}</td>
                    <td>${r.contributedToDecision ? '<span class="trace-match">\u2713</span>' : ''}</td>
                </tr>`;
            });
            html += `</tbody></table></div>`;
        }

        html += `</div>`;
        return html;
    }

    function esc(s) {
        if (!s) return '';
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

    // Init
    loadStats();
    loadView('resources');
})();
