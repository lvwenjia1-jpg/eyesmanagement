(function () {
    const state = {
        items: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        filters: {
            keyword: '',
            productCode: '',
            productName: '',
            specificationToken: '',
            modelToken: '',
            degree: ''
        },
        isLoading: false
    };

    const exampleEntries = [
        {
            productCode: '日抛2片深空物语蓝紫350',
            productName: '深空物语蓝紫',
            specCode: '日抛2片',
            barcode: '6932364896736',
            baseName: '深空物语蓝紫',
            specificationToken: '日抛2片',
            modelToken: 'lenspop日抛',
            degree: '350',
            searchText: '日抛2片深空物语蓝紫350 深空物语蓝紫 lenspop日抛 350'
        }
    ];

    const elements = {
        currentLoginName: document.getElementById('currentLoginName'),
        exportBtn: document.getElementById('exportBtn'),
        replaceBtn: document.getElementById('replaceBtn'),
        keywordInput: document.getElementById('keywordInput'),
        productCodeInput: document.getElementById('productCodeInput'),
        productNameInput: document.getElementById('productNameInput'),
        specificationInput: document.getElementById('specificationInput'),
        modelTokenInput: document.getElementById('modelTokenInput'),
        degreeInput: document.getElementById('degreeInput'),
        searchBtn: document.getElementById('searchBtn'),
        resetBtn: document.getElementById('resetBtn'),
        loadingHint: document.getElementById('loadingHint'),
        catalogTableBody: document.getElementById('catalogTableBody'),
        pageInfo: document.getElementById('pageInfo'),
        mobilePageInfo: document.getElementById('mobilePageInfo'),
        pagination: document.getElementById('pagination'),
        mobilePrevBtn: document.getElementById('mobilePrevBtn'),
        mobileNextBtn: document.getElementById('mobileNextBtn'),
        pageCountCard: document.getElementById('pageCountCard'),
        totalCountCard: document.getElementById('totalCountCard'),
        lastUpdatedCard: document.getElementById('lastUpdatedCard'),
        logoutBtn: document.getElementById('logoutBtn'),
        replaceModal: document.getElementById('replaceModal'),
        closeModalBtn: document.getElementById('closeModal'),
        cancelBtn: document.getElementById('cancelBtn'),
        loadCurrentBtn: document.getElementById('loadCurrentBtn'),
        loadExampleBtn: document.getElementById('loadExampleBtn'),
        formatJsonBtn: document.getElementById('formatJsonBtn'),
        sourceFileNameInput: document.getElementById('sourceFileNameInput'),
        catalogJsonInput: document.getElementById('catalogJsonInput'),
        submitReplaceBtn: document.getElementById('submitReplaceBtn')
    };

    function setLoading(isLoading) {
        state.isLoading = isLoading;
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        elements.searchBtn.disabled = isLoading;
        elements.resetBtn.disabled = isLoading;
        elements.replaceBtn.disabled = isLoading;
        elements.exportBtn.disabled = isLoading;
    }

    function openReplaceModal() {
        elements.replaceModal.classList.remove('hidden');
    }

    function closeReplaceModal() {
        elements.replaceModal.classList.add('hidden');
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.items.length);
        elements.totalCountCard.textContent = String(state.totalCount);

        const updatedAtValues = state.items
            .map(item => item.updatedAtUtc)
            .filter(Boolean)
            .sort()
            .reverse();
        elements.lastUpdatedCard.textContent = updatedAtValues.length > 0
            ? dashboardApp.formatDateTime(updatedAtValues[0])
            : '-';
    }

    function renderTable() {
        if (state.items.length === 0) {
            elements.catalogTableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="px-6 py-10 text-center">
                        <div class="text-gray-400 text-4xl mb-3"><i class="fa fa-inbox"></i></div>
                        <div class="text-gray-600 font-medium">暂无商品编码记录</div>
                        <div class="text-sm text-gray-400 mt-1">可以调整筛选条件，或通过“批量替换目录”导入新的商品编码表。</div>
                    </td>
                </tr>
            `;
            return;
        }

        elements.catalogTableBody.innerHTML = state.items.map(item => `
            <tr class="hover:bg-gray-50 transition-all">
                <td class="px-6 py-4 text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(item.productCode)}</td>
                <td class="px-6 py-4 text-sm text-gray-700">${dashboardApp.escapeHtml(item.productName)}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.escapeHtml(item.specCode || '-')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.escapeHtml(item.barcode || '-')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.escapeHtml(item.specificationToken || '-')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.escapeHtml(item.modelToken || '-')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.escapeHtml(item.degree || '-')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${dashboardApp.formatDateTime(item.updatedAtUtc)}</td>
            </tr>
        `).join('');
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

        function appendButton(label, targetPage, options) {
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
                    state.currentPage = targetPage;
                    await loadCatalog();
                });
            }

            nav.appendChild(button);
        }

        const pages = [];
        const startPage = Math.max(1, state.currentPage - 2);
        const endPage = Math.min(totalPages, state.currentPage + 2);
        for (let page = startPage; page <= endPage; page += 1) {
            pages.push(page);
        }

        appendButton('<', state.currentPage - 1, {
            active: false,
            disabled: state.currentPage <= 1,
            edge: 'left'
        });

        if (pages[0] > 1) {
            appendButton('1', 1, { active: state.currentPage === 1, disabled: false, edge: null });
            if (pages[0] > 2) {
                const dots = document.createElement('span');
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-gray-300 bg-white text-sm text-gray-400';
                dots.textContent = '...';
                nav.appendChild(dots);
            }
        }

        pages.forEach(page => {
            appendButton(String(page), page, {
                active: page === state.currentPage,
                disabled: false,
                edge: null
            });
        });

        if (pages[pages.length - 1] < totalPages) {
            if (pages[pages.length - 1] < totalPages - 1) {
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

    function collectFiltersFromInputs() {
        state.filters.keyword = elements.keywordInput.value.trim();
        state.filters.productCode = elements.productCodeInput.value.trim();
        state.filters.productName = elements.productNameInput.value.trim();
        state.filters.specificationToken = elements.specificationInput.value.trim();
        state.filters.modelToken = elements.modelTokenInput.value.trim();
        state.filters.degree = elements.degreeInput.value.trim();
    }

    async function loadCatalog() {
        setLoading(true);
        try {
            const query = new URLSearchParams({
                pageNumber: String(state.currentPage),
                pageSize: String(state.pageSize)
            });

            Object.entries(state.filters).forEach(([key, value]) => {
                if (value) {
                    query.set(key, value);
                }
            });

            const response = await dashboardApp.apiRequest(`/api/product-catalog/query?${query.toString()}`);
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

    async function loadAllCatalog() {
        return await dashboardApp.apiRequest('/api/product-catalog');
    }

    function normalizeEntry(entry) {
        const normalized = {
            productCode: String(entry.productCode || '').trim(),
            productName: String(entry.productName || '').trim(),
            specCode: String(entry.specCode || '').trim(),
            barcode: String(entry.barcode || '').trim(),
            baseName: String(entry.baseName || '').trim(),
            specificationToken: String(entry.specificationToken || '').trim(),
            modelToken: String(entry.modelToken || '').trim(),
            degree: String(entry.degree || '').trim(),
            searchText: String(entry.searchText || '').trim()
        };

        if (!normalized.baseName) {
            normalized.baseName = normalized.productName;
        }

        if (!normalized.searchText) {
            normalized.searchText = [
                normalized.productCode,
                normalized.productName,
                normalized.specCode,
                normalized.barcode,
                normalized.baseName,
                normalized.specificationToken,
                normalized.modelToken,
                normalized.degree
            ].filter(Boolean).join(' ');
        }

        return normalized;
    }

    function parseCatalogJson() {
        const raw = elements.catalogJsonInput.value.trim();
        if (!raw) {
            throw new Error('请先填写商品目录 JSON');
        }

        const parsed = JSON.parse(raw);
        const entries = Array.isArray(parsed)
            ? parsed
            : Array.isArray(parsed.entries)
                ? parsed.entries
                : null;

        if (!entries || entries.length === 0) {
            throw new Error('JSON 中至少需要一条商品编码记录');
        }

        const normalizedEntries = entries.map(normalizeEntry);
        const invalidIndex = normalizedEntries.findIndex(item => !item.productCode);
        if (invalidIndex >= 0) {
            throw new Error(`第 ${invalidIndex + 1} 条记录缺少 productCode`);
        }

        return normalizedEntries;
    }

    function downloadJson(filename, content) {
        const blob = new Blob([content], { type: 'application/json;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }

    async function onSearch() {
        collectFiltersFromInputs();
        state.currentPage = 1;
        await loadCatalog();
    }

    async function onReset() {
        elements.keywordInput.value = '';
        elements.productCodeInput.value = '';
        elements.productNameInput.value = '';
        elements.specificationInput.value = '';
        elements.modelTokenInput.value = '';
        elements.degreeInput.value = '';
        collectFiltersFromInputs();
        state.currentPage = 1;
        await loadCatalog();
    }

    async function onExport() {
        try {
            const entries = await loadAllCatalog();
            const json = JSON.stringify(entries, null, 2);
            const now = new Date();
            const timestamp = `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}-${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}${String(now.getSeconds()).padStart(2, '0')}`;
            downloadJson(`product-catalog-${timestamp}.json`, json);
            dashboardApp.showToast(`已导出 ${entries.length} 条商品编码`);
        } catch (error) {
            dashboardApp.showToast(error.message || '导出商品目录失败', 'error');
        }
    }

    async function onLoadCurrent() {
        try {
            const entries = await loadAllCatalog();
            elements.catalogJsonInput.value = JSON.stringify(entries, null, 2);
            if (!elements.sourceFileNameInput.value.trim()) {
                elements.sourceFileNameInput.value = 'product-catalog.json';
            }
            dashboardApp.showToast(`已加载当前目录，共 ${entries.length} 条`);
        } catch (error) {
            dashboardApp.showToast(error.message || '读取当前目录失败', 'error');
        }
    }

    function onLoadExample() {
        elements.catalogJsonInput.value = JSON.stringify(exampleEntries, null, 2);
        if (!elements.sourceFileNameInput.value.trim()) {
            elements.sourceFileNameInput.value = 'product-catalog-example.json';
        }
    }

    function onFormatJson() {
        try {
            const parsed = JSON.parse(elements.catalogJsonInput.value.trim());
            elements.catalogJsonInput.value = JSON.stringify(parsed, null, 2);
        } catch (error) {
            dashboardApp.showToast(`JSON 格式不正确：${error.message}`, 'error');
        }
    }

    async function onSubmitReplace() {
        try {
            const entries = parseCatalogJson();
            const sourceFileName = elements.sourceFileNameInput.value.trim() || 'manual-replace.json';
            const confirmed = window.confirm(`即将用 ${entries.length} 条商品编码覆盖当前目录，是否继续？`);
            if (!confirmed) {
                return;
            }

            await dashboardApp.apiRequest('/api/product-catalog', {
                method: 'PUT',
                body: {
                    sourceFileName,
                    entries
                }
            });

            dashboardApp.showToast(`商品目录已更新，共 ${entries.length} 条`);
            closeReplaceModal();
            state.currentPage = 1;
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '替换商品目录失败', 'error');
        }
    }

    async function goToPage(offset) {
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        const nextPage = Math.min(totalPages, Math.max(1, state.currentPage + offset));
        if (nextPage === state.currentPage) {
            return;
        }

        state.currentPage = nextPage;
        await loadCatalog();
    }

    function bindEvents() {
        elements.exportBtn.addEventListener('click', onExport);
        elements.replaceBtn.addEventListener('click', openReplaceModal);
        elements.searchBtn.addEventListener('click', onSearch);
        elements.resetBtn.addEventListener('click', onReset);
        [
            elements.keywordInput,
            elements.productCodeInput,
            elements.productNameInput,
            elements.specificationInput,
            elements.modelTokenInput,
            elements.degreeInput
        ].forEach(input => {
            input.addEventListener('keyup', async event => {
                if (event.key === 'Enter') {
                    await onSearch();
                }
            });
        });
        elements.logoutBtn.addEventListener('click', () => dashboardApp.logout());
        elements.mobilePrevBtn.addEventListener('click', () => goToPage(-1));
        elements.mobileNextBtn.addEventListener('click', () => goToPage(1));
        elements.closeModalBtn.addEventListener('click', closeReplaceModal);
        elements.cancelBtn.addEventListener('click', closeReplaceModal);
        elements.loadCurrentBtn.addEventListener('click', onLoadCurrent);
        elements.loadExampleBtn.addEventListener('click', onLoadExample);
        elements.formatJsonBtn.addEventListener('click', onFormatJson);
        elements.submitReplaceBtn.addEventListener('click', onSubmitReplace);
        elements.replaceModal.addEventListener('click', event => {
            if (event.target === elements.replaceModal) {
                closeReplaceModal();
            }
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !elements.replaceModal.classList.contains('hidden')) {
                closeReplaceModal();
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
            collectFiltersFromInputs();
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载商品目录失败', 'error');
        }
    });
})();
