(function () {
    const state = {
        items: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        keyword: '',
        isActive: '',
        editingId: null,
        isLoading: false
    };

    const elements = {
        tableBody: document.getElementById('priceRulesTableBody'),
        addBtn: document.getElementById('addPriceRuleBtn'),
        importBtn: document.getElementById('importExcelBtn'),
        importInput: document.getElementById('importExcelInput'),
        searchInput: document.getElementById('searchInput'),
        statusFilter: document.getElementById('statusFilter'),
        searchBtn: document.getElementById('searchBtn'),
        resetBtn: document.getElementById('resetBtn'),
        pageInfo: document.getElementById('pageInfo'),
        mobilePageInfo: document.getElementById('mobilePageInfo'),
        pagination: document.getElementById('pagination'),
        mobilePrevBtn: document.getElementById('mobilePrevBtn'),
        mobileNextBtn: document.getElementById('mobileNextBtn'),
        logoutBtn: document.getElementById('logoutBtn'),
        currentLoginName: document.getElementById('currentLoginName'),
        loadingHint: document.getElementById('loadingHint'),
        pageCountCard: document.getElementById('pageCountCard'),
        totalCountCard: document.getElementById('totalCountCard'),
        filterSummaryCard: document.getElementById('filterSummaryCard'),
        modal: document.getElementById('priceRuleModal'),
        modalTitle: document.getElementById('modalTitle'),
        closeModalBtn: document.getElementById('closeModal'),
        cancelBtn: document.getElementById('cancelBtn'),
        form: document.getElementById('priceRuleForm'),
        inputId: document.getElementById('priceRuleId'),
        inputName: document.getElementById('priceName'),
        inputValue: document.getElementById('priceValue'),
        inputActive: document.getElementById('isActive')
    };

    function setLoading(isLoading) {
        state.isLoading = isLoading;
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        elements.searchBtn.disabled = isLoading;
        elements.resetBtn.disabled = isLoading;
        elements.addBtn.disabled = isLoading;
        if (elements.importBtn) {
            elements.importBtn.disabled = isLoading;
        }
    }

    function openModal() {
        elements.modal.classList.remove('hidden');
        window.setTimeout(() => elements.inputName.focus(), 0);
    }

    function closeModal() {
        elements.modal.classList.add('hidden');
    }

    function resetForm() {
        state.editingId = null;
        elements.inputId.value = '';
        elements.inputName.value = '';
        elements.inputValue.value = '0';
        elements.inputActive.checked = true;
        elements.modalTitle.textContent = '新增价格';
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.items.length);
        elements.totalCountCard.textContent = String(state.totalCount);

        const keywordLabel = state.keyword ? `关键字：${state.keyword}` : '关键字：全部';
        const statusLabel = state.isActive === ''
            ? '状态：全部'
            : state.isActive === 'true'
                ? '状态：启用'
                : '状态：停用';
        elements.filterSummaryCard.textContent = `${keywordLabel} / ${statusLabel}`;
    }

    function buildStatusBadge(isActive) {
        return isActive
            ? '<span class="inline-flex items-center px-2.5 py-1 rounded-full text-xs bg-green-100 text-green-700">启用</span>'
            : '<span class="inline-flex items-center px-2.5 py-1 rounded-full text-xs bg-gray-200 text-gray-600">停用</span>';
    }

    function renderEmptyTable() {
        elements.tableBody.innerHTML = [
            '<tr>',
            '  <td colspan="6" class="px-6 py-10 text-center">',
            '    <div class="text-gray-400 text-4xl mb-3"><i class="fa fa-inbox"></i></div>',
            '    <div class="text-gray-600 font-medium">暂无价格规则</div>',
            '    <div class="text-sm text-gray-400 mt-1">可以调整筛选条件，手动新增，或直接导入 Excel 价格表。</div>',
            '  </td>',
            '</tr>'
        ].join('');
    }

    function renderTable() {
        if (state.items.length === 0) {
            renderEmptyTable();
            return;
        }

        elements.tableBody.innerHTML = state.items.map(rule => `
            <tr class="hover:bg-gray-50 transition-all">
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${rule.id}</td>
                <td class="px-6 py-4 text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(rule.priceName)}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">${rule.priceValue}</td>
                <td class="px-6 py-4 whitespace-nowrap">${buildStatusBadge(rule.isActive)}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.formatDateTime(rule.updatedAtUtc)}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${rule.id}">
                        <i class="fa fa-pencil mr-1"></i>编辑
                    </button>
                    <button class="text-warning hover:text-yellow-700 toggle-btn" data-id="${rule.id}">
                        <i class="fa fa-exchange mr-1"></i>${rule.isActive ? '停用' : '启用'}
                    </button>
                </td>
            </tr>
        `).join('');

        document.querySelectorAll('.edit-btn').forEach(button => {
            button.addEventListener('click', onEdit);
        });

        document.querySelectorAll('.toggle-btn').forEach(button => {
            button.addEventListener('click', onToggleStatus);
        });
    }

    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        const start = state.totalCount === 0 ? 0 : (state.currentPage - 1) * state.pageSize + 1;
        const end = Math.min(state.currentPage * state.pageSize, state.totalCount);
        const summary = `显示 ${start} 到 ${end} 条，共 ${state.totalCount} 条记录`;

        elements.pageInfo.textContent = summary;
        elements.mobilePageInfo.textContent = summary;
        elements.mobilePrevBtn.disabled = state.currentPage <= 1;
        elements.mobileNextBtn.disabled = state.currentPage >= totalPages;
        elements.mobilePrevBtn.classList.toggle('opacity-50', state.currentPage <= 1);
        elements.mobileNextBtn.classList.toggle('opacity-50', state.currentPage >= totalPages);

        elements.pagination.innerHTML = '';
        if (totalPages <= 1) {
            return;
        }

        const nav = document.createElement('nav');
        nav.className = 'relative z-0 inline-flex rounded-md shadow-sm -space-x-px';

        const visiblePages = [];
        const startPage = Math.max(1, state.currentPage - 2);
        const endPage = Math.min(totalPages, state.currentPage + 2);
        for (let page = startPage; page <= endPage; page += 1) {
            visiblePages.push(page);
        }

        function appendButton(label, page, options) {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = label;
            button.className = `relative inline-flex items-center px-3 py-2 border border-gray-300 text-sm ${
                options.active
                    ? 'bg-primary text-white'
                    : 'bg-white text-gray-700 hover:bg-gray-50'
            } ${options.disabled ? 'opacity-50 cursor-not-allowed' : ''}`;

            if (options.edge === 'left') {
                button.classList.add('rounded-l-md');
            }
            if (options.edge === 'right') {
                button.classList.add('rounded-r-md');
            }

            if (!options.disabled && !options.active) {
                button.addEventListener('click', async () => {
                    state.currentPage = page;
                    await loadPriceRules();
                });
            }

            nav.appendChild(button);
        }

        appendButton('<', state.currentPage - 1, {
            active: false,
            disabled: state.currentPage <= 1,
            edge: 'left'
        });

        if (visiblePages[0] > 1) {
            appendButton('1', 1, { active: state.currentPage === 1, disabled: false, edge: null });
            if (visiblePages[0] > 2) {
                const dots = document.createElement('span');
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-gray-300 bg-white text-sm text-gray-400';
                dots.textContent = '...';
                nav.appendChild(dots);
            }
        }

        visiblePages.forEach(page => {
            appendButton(String(page), page, {
                active: page === state.currentPage,
                disabled: false,
                edge: null
            });
        });

        if (visiblePages[visiblePages.length - 1] < totalPages) {
            if (visiblePages[visiblePages.length - 1] < totalPages - 1) {
                const dots = document.createElement('span');
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-gray-300 bg-white text-sm text-gray-400';
                dots.textContent = '...';
                nav.appendChild(dots);
            }
            appendButton(String(totalPages), totalPages, {
                active: state.currentPage === totalPages,
                disabled: false,
                edge: null
            });
        }

        appendButton('>', state.currentPage + 1, {
            active: false,
            disabled: state.currentPage >= totalPages,
            edge: 'right'
        });

        elements.pagination.appendChild(nav);
    }

    async function loadPriceRules() {
        setLoading(true);
        try {
            const query = new URLSearchParams({
                pageNumber: String(state.currentPage),
                pageSize: String(state.pageSize)
            });

            if (state.keyword) {
                query.set('keyword', state.keyword);
            }

            if (state.isActive !== '') {
                query.set('isActive', state.isActive);
            }

            const response = await dashboardApp.apiRequest(`/api/price-rules?${query.toString()}`);
            state.items = response.items || [];
            state.totalCount = response.totalCount || 0;
            state.currentPage = response.pageNumber || state.currentPage;

            renderTable();
            renderPagination();
            updateSummaryCards();
        } finally {
            setLoading(false);
        }
    }

    function onAdd() {
        resetForm();
        openModal();
    }

    function onEdit(event) {
        const id = Number(event.currentTarget.dataset.id);
        const rule = state.items.find(item => item.id === id);
        if (!rule) {
            return;
        }

        state.editingId = id;
        elements.inputId.value = String(rule.id);
        elements.inputName.value = rule.priceName;
        elements.inputValue.value = String(rule.priceValue);
        elements.inputActive.checked = Boolean(rule.isActive);
        elements.modalTitle.textContent = '编辑价格';
        openModal();
    }

    async function onToggleStatus(event) {
        const id = Number(event.currentTarget.dataset.id);
        const rule = state.items.find(item => item.id === id);
        if (!rule) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/price-rules/${id}`, {
                method: 'PUT',
                body: {
                    priceName: rule.priceName,
                    priceValue: rule.priceValue,
                    isActive: !rule.isActive
                }
            });

            dashboardApp.showToast(`价格规则已${rule.isActive ? '停用' : '启用'}`);
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '更新价格状态失败', 'error');
        }
    }

    async function onSearch() {
        state.keyword = elements.searchInput.value.trim();
        state.isActive = elements.statusFilter.value;
        state.currentPage = 1;
        await loadPriceRules();
    }

    async function onReset() {
        elements.searchInput.value = '';
        elements.statusFilter.value = '';
        state.keyword = '';
        state.isActive = '';
        state.currentPage = 1;
        await loadPriceRules();
    }

    async function onSubmit(event) {
        event.preventDefault();

        const priceName = elements.inputName.value.trim();
        const priceValue = Number(elements.inputValue.value);
        const isActive = Boolean(elements.inputActive.checked);

        if (!priceName) {
            dashboardApp.showToast('请输入价格名称', 'error');
            return;
        }

        if (!Number.isInteger(priceValue) || priceValue < 0) {
            dashboardApp.showToast('价格必须是大于等于 0 的整数', 'error');
            return;
        }

        try {
            if (state.editingId) {
                await dashboardApp.apiRequest(`/api/price-rules/${state.editingId}`, {
                    method: 'PUT',
                    body: { priceName, priceValue, isActive }
                });
                dashboardApp.showToast('价格规则已更新');
            } else {
                await dashboardApp.apiRequest('/api/price-rules', {
                    method: 'POST',
                    body: { priceName, priceValue }
                });
                dashboardApp.showToast('价格规则已创建');
            }

            closeModal();
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存价格规则失败', 'error');
        }
    }

    async function goToPage(offset) {
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        const nextPage = Math.min(totalPages, Math.max(1, state.currentPage + offset));
        if (nextPage === state.currentPage) {
            return;
        }

        state.currentPage = nextPage;
        await loadPriceRules();
    }

    function normalizeHeader(value) {
        return String(value || '')
            .trim()
            .toLowerCase()
            .replace(/[\s_\-()/\\]/g, '');
    }

    function findColumnKey(row, aliases) {
        for (const key of Object.keys(row || {})) {
            if (aliases.includes(normalizeHeader(key))) {
                return key;
            }
        }

        return '';
    }

    function parsePriceValue(value) {
        const normalized = String(value ?? '').trim();
        if (!normalized) {
            return 0;
        }

        if (!/^-?\d+$/.test(normalized)) {
            throw new Error(`价格必须是整数，收到：${normalized}`);
        }

        const parsed = Number(normalized);
        if (!Number.isInteger(parsed) || parsed < 0) {
            throw new Error(`价格必须是大于等于 0 的整数，收到：${normalized}`);
        }

        return parsed;
    }

    function parseIsActive(value) {
        const normalized = String(value ?? '').trim().toLowerCase();
        if (!normalized) {
            return true;
        }

        if (['1', 'true', 'yes', 'y', '启用', '开启', '开', '是', '有效'].includes(normalized)) {
            return true;
        }

        if (['0', 'false', 'no', 'n', '停用', '禁用', '关', '否', '无效'].includes(normalized)) {
            return false;
        }

        throw new Error(`无法识别启用状态：${value}`);
    }

    function extractImportEntries(rows) {
        if (!Array.isArray(rows) || rows.length === 0) {
            throw new Error('Excel 里没有可导入的数据');
        }

        const firstRow = rows[0] || {};
        const priceNameKey = findColumnKey(firstRow, ['pricename', 'name', '价格名称', '价格名', '名称']);
        const priceValueKey = findColumnKey(firstRow, ['pricevalue', 'value', 'amount', '价格', '金额', '单价', '价格值']);
        const isActiveKey = findColumnKey(firstRow, ['isactive', 'active', 'status', 'enabled', '启用', '是否启用', '状态']);

        if (!priceNameKey || !priceValueKey) {
            throw new Error('Excel 缺少必填列，请至少包含“价格名称”和“价格”两列');
        }

        return rows.map((row, index) => {
            const priceName = String(row[priceNameKey] ?? '').trim();
            if (!priceName) {
                throw new Error(`第 ${index + 2} 行缺少价格名称`);
            }

            const entry = {
                priceName,
                priceValue: parsePriceValue(row[priceValueKey]),
                isActive: true
            };

            if (isActiveKey) {
                entry.isActive = parseIsActive(row[isActiveKey]);
            }

            return entry;
        });
    }

    async function importPriceRules(file) {
        if (!file) {
            return;
        }

        if (typeof XLSX === 'undefined') {
            throw new Error('Excel 解析库加载失败，请刷新页面后重试');
        }

        setLoading(true);
        try {
            const buffer = await file.arrayBuffer();
            const workbook = XLSX.read(buffer, { type: 'array' });
            const sheetName = workbook.SheetNames[0];
            if (!sheetName) {
                throw new Error('Excel 中没有工作表');
            }

            const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], {
                defval: '',
                raw: false
            });
            const entries = extractImportEntries(rows);
            const result = await dashboardApp.apiRequest('/api/price-rules/import', {
                method: 'POST',
                body: {
                    sourceFileName: file.name,
                    entries
                }
            });

            dashboardApp.showToast(`导入完成：新增 ${result.createdCount} 条，更新 ${result.updatedCount} 条`);
            state.currentPage = 1;
            await loadPriceRules();
        } finally {
            setLoading(false);
            elements.importInput.value = '';
        }
    }

    function onImportClick() {
        if (!elements.importInput) {
            return;
        }

        elements.importInput.value = '';
        elements.importInput.click();
    }

    async function onImportChange(event) {
        const file = event.target.files && event.target.files[0];
        if (!file) {
            return;
        }

        try {
            await importPriceRules(file);
        } catch (error) {
            dashboardApp.showToast(error.message || '导入价格表失败', 'error');
        }
    }

    function bindEvents() {
        elements.addBtn.addEventListener('click', onAdd);
        if (elements.importBtn) {
            elements.importBtn.addEventListener('click', onImportClick);
        }
        if (elements.importInput) {
            elements.importInput.addEventListener('change', onImportChange);
        }
        elements.searchBtn.addEventListener('click', onSearch);
        elements.resetBtn.addEventListener('click', onReset);
        elements.searchInput.addEventListener('keyup', async event => {
            if (event.key === 'Enter') {
                await onSearch();
            }
        });
        elements.statusFilter.addEventListener('change', onSearch);
        elements.closeModalBtn.addEventListener('click', closeModal);
        elements.cancelBtn.addEventListener('click', closeModal);
        elements.form.addEventListener('submit', onSubmit);
        elements.logoutBtn.addEventListener('click', () => dashboardApp.logout());
        elements.mobilePrevBtn.addEventListener('click', () => goToPage(-1));
        elements.mobileNextBtn.addEventListener('click', () => goToPage(1));
        elements.modal.addEventListener('click', event => {
            if (event.target === elements.modal) {
                closeModal();
            }
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !elements.modal.classList.contains('hidden')) {
                closeModal();
            }
        });
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        bindEvents();
        elements.currentLoginName.textContent = dashboardApp.getCurrentLoginName() || '-';

        try {
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载价格规则失败', 'error');
        }
    });
})();
