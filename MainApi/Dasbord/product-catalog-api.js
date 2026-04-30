(function () {
    const state = {
        groups: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        selectedGroupKey: '',
        filters: {
            keyword: '',
            specificationToken: '',
            modelToken: '',
            degree: ''
        },
        isLoading: false
    };

    const elements = {
        currentLoginName: document.getElementById('currentLoginName'),
        downloadTemplateBtn: document.getElementById('downloadTemplateBtn'),
        exportBtn: document.getElementById('exportBtn'),
        importBtn: document.getElementById('importBtn'),
        importExcelInput: document.getElementById('importExcelInput'),
        addBtn: document.getElementById('addBtn'),
        addDegreeBtn: document.getElementById('addDegreeBtn'),
        keywordInput: document.getElementById('keywordInput'),
        specificationTokenInput: document.getElementById('specificationTokenInput'),
        modelTokenInput: document.getElementById('modelTokenInput'),
        degreeInput: document.getElementById('degreeInput'),
        searchBtn: document.getElementById('searchBtn'),
        resetBtn: document.getElementById('resetBtn'),
        loadingHint: document.getElementById('loadingHint'),
        catalogGroupTableBody: document.getElementById('catalogGroupTableBody'),
        catalogDetailTitle: document.getElementById('catalogDetailTitle'),
        catalogDegreeTableBody: document.getElementById('catalogDegreeTableBody'),
        pageInfo: document.getElementById('pageInfo'),
        mobilePageInfo: document.getElementById('mobilePageInfo'),
        pagination: document.getElementById('pagination'),
        mobilePrevBtn: document.getElementById('mobilePrevBtn'),
        mobileNextBtn: document.getElementById('mobileNextBtn'),
        pageCountCard: document.getElementById('pageCountCard'),
        totalCountCard: document.getElementById('totalCountCard'),
        totalDegreeCountCard: document.getElementById('totalDegreeCountCard'),
        editModal: document.getElementById('editModal'),
        modalTitle: document.getElementById('modalTitle'),
        closeModalBtn: document.getElementById('closeModalBtn'),
        cancelBtn: document.getElementById('cancelBtn'),
        editForm: document.getElementById('editForm'),
        inputSpecificationToken: document.getElementById('inputSpecificationToken'),
        inputModelToken: document.getElementById('inputModelToken'),
        inputDegree: document.getElementById('inputDegree'),
        inputBarcode: document.getElementById('inputBarcode')
    };

    function normalizeText(value) {
        return String(value || '').trim();
    }

    function normalizeHeader(value) {
        return normalizeText(value)
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

    function buildAutoProductCode() {
        const specificationToken = normalizeText(elements.inputSpecificationToken.value);
        const modelToken = normalizeText(elements.inputModelToken.value);
        const degree = normalizeText(elements.inputDegree.value);
        return buildAutoProductCodeForDegree(specificationToken, modelToken, degree);
    }

    function buildAutoProductCodeForDegree(specificationToken, modelToken, degree) {
        const normalizedSpecificationToken = normalizeText(specificationToken);
        const normalizedModelToken = normalizeText(modelToken);
        const normalizedDegree = normalizeText(degree);
        const base = `${normalizedSpecificationToken}${normalizedModelToken}`.trim();
        if (!base) {
            return '';
        }

        return normalizedDegree ? `${base}${normalizedDegree}` : base;
    }

    function parseDegreeBatchInput(value) {
        const normalized = String(value || '')
            .replace(/\r/g, '\n')
            .replace(/[，、；;]+/g, ',');
        return normalized
            .split(/[\n,\s]+/)
            .map(item => normalizeText(item))
            .filter(Boolean);
    }

    function isOutOfStock(value) {
        if (typeof value === 'boolean') {
            return value;
        }

        const normalized = normalizeText(value).toLowerCase();
        if (!normalized) {
            return false;
        }

        return normalized === '1' ||
            normalized === 'true' ||
            normalized === 'yes' ||
            normalized === 'y' ||
            normalized === '是' ||
            normalized === '缺货' ||
            normalized.includes('缺货');
    }
    function setLoading(isLoading) {
        state.isLoading = isLoading;
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        elements.searchBtn.disabled = isLoading;
        elements.resetBtn.disabled = isLoading;
        elements.importBtn.disabled = isLoading;
        elements.addBtn.disabled = isLoading;
        if (elements.addDegreeBtn) {
            elements.addDegreeBtn.disabled = isLoading;
        }
        elements.exportBtn.disabled = isLoading;
        elements.downloadTemplateBtn.disabled = isLoading;
    }

    function collectFiltersFromInputs() {
        state.filters.keyword = normalizeText(elements.keywordInput.value);
        state.filters.specificationToken = normalizeText(elements.specificationTokenInput.value);
        state.filters.modelToken = normalizeText(elements.modelTokenInput.value);
        state.filters.degree = normalizeText(elements.degreeInput.value);
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.groups.length);
        elements.totalCountCard.textContent = String(state.totalCount);
        elements.totalDegreeCountCard.textContent = String(
            state.groups.reduce((sum, group) => sum + ((group.degrees || []).length), 0)
        );
    }

    function buildGroupKey(group) {
        const specificationToken = normalizeText(group && group.specificationToken);
        const modelToken = normalizeText(group && group.modelToken);
        return `${specificationToken}||${modelToken}`;
    }

    function getSelectedGroup() {
        return state.groups.find(group => buildGroupKey(group) === state.selectedGroupKey) || null;
    }

    function normalizeSelectedGroup() {
        if (state.groups.length === 0) {
            state.selectedGroupKey = '';
            return;
        }

        if (!state.selectedGroupKey) {
            state.selectedGroupKey = buildGroupKey(state.groups[0]);
            return;
        }

        const exists = state.groups.some(group => buildGroupKey(group) === state.selectedGroupKey);
        if (!exists) {
            state.selectedGroupKey = buildGroupKey(state.groups[0]);
        }
    }

    function buildGroupDegreeSummary(group) {
        const degrees = group.degrees || [];
        if (degrees.length === 0) {
            return '无度数条目';
        }

        const preview = degrees
            .slice(0, 3)
            .map(item => normalizeText(item.degree) || '-')
            .join(' / ');

        if (degrees.length <= 3) {
            return `度数：${preview}`;
        }

        return `度数：${preview} 等 ${degrees.length} 项`;
    }

    function renderGroupTable() {
        if (state.groups.length === 0) {
            elements.catalogGroupTableBody.innerHTML = `
                <tr>
                    <td colspan="3" class="px-6 py-10 text-center">
                        <div class="text-slate-400 text-4xl mb-3"><i class="fa fa-inbox"></i></div>
                        <div class="text-slate-600 font-medium">暂无商品编码记录</div>
                        <div class="text-sm text-slate-400 mt-1">可新增单条，或用结构化 Excel 增量导入。</div>
                    </td>
                </tr>
            `;
            renderDegreeTable();
            return;
        }

        elements.catalogGroupTableBody.innerHTML = state.groups.map(group => {
            const groupKey = buildGroupKey(group);
            const isSelected = groupKey === state.selectedGroupKey;
            const selectedClass = isSelected ? 'bg-blue-50' : 'hover:bg-slate-50';
            const buttonClass = isSelected
                ? 'show-degrees-btn w-full text-left bg-blue-50 border border-primary rounded-md px-3 py-2 transition-all'
                : 'show-degrees-btn w-full text-left bg-white border border-slate-300 hover:border-primary rounded-md px-3 py-2 transition-all';
            const modelToken = normalizeText(group.modelToken) || '-';

            return `
            <tr class="${selectedClass} transition-all align-top">
                <td class="w-24 px-3 py-4 text-sm text-slate-700 whitespace-nowrap">${dashboardApp.escapeHtml(group.specificationToken || '-')}</td>
                <td class="px-3 py-4 text-sm text-slate-700">
                    <button
                        type="button"
                        class="${buttonClass}"
                        data-group-key="${dashboardApp.escapeHtml(groupKey)}">
                        <div class="font-semibold text-slate-800">${dashboardApp.escapeHtml(modelToken)}</div>
                        <div class="text-xs text-slate-500 mt-1">${dashboardApp.escapeHtml(buildGroupDegreeSummary(group))}</div>
                        <div class="text-xs text-primary mt-2">查看度数</div>
                    </button>
                </td>
                <td class="w-32 px-3 py-4 text-sm text-slate-500 whitespace-nowrap">${dashboardApp.formatDateTime(group.updatedAtUtc)}</td>
            </tr>
            `;
        }).join('');

        elements.catalogGroupTableBody.querySelectorAll('.show-degrees-btn').forEach(button => {
            button.addEventListener('click', onShowDegrees);
        });

        renderDegreeTable();
    }

    function renderDegreeTable() {
        const selectedGroup = getSelectedGroup();
        if (!selectedGroup) {
            elements.catalogDetailTitle.textContent = '度数明细';
            elements.catalogDegreeTableBody.innerHTML = `
                <tr>
                    <td colspan="5" class="px-4 py-8 text-sm text-slate-400 text-center">请先从左侧选择一个型号</td>
                </tr>
            `;
            return;
        }

        const titleSpecification = normalizeText(selectedGroup.specificationToken) || '-';
        const titleModel = normalizeText(selectedGroup.modelToken) || '-';
        elements.catalogDetailTitle.textContent = `度数明细：${titleSpecification} / ${titleModel}`;

        const degrees = selectedGroup.degrees || [];
        if (degrees.length === 0) {
            elements.catalogDegreeTableBody.innerHTML = `
                <tr>
                    <td colspan="5" class="px-4 py-8 text-sm text-slate-400 text-center">当前分组还没有度数，请点击右上角“新增度数”</td>
                </tr>
            `;
            return;
        }

        elements.catalogDegreeTableBody.innerHTML = degrees.map(item => {
            const degreeLabel = normalizeText(item.degree) || '-';
            const productCode = normalizeText(item.productCode) || '-';
            const barcode = normalizeText(item.barcode) || '-';
            const outOfStock = isOutOfStock(item.isOutOfStock);
            const stockStatusClass = outOfStock
                ? 'bg-blue-50 text-blue-700 border-blue-200'
                : 'bg-emerald-50 text-emerald-700 border-emerald-200';
            const stockButtonClass = outOfStock
                ? 'bg-slate-100 hover:bg-slate-200 text-slate-700 border-slate-300'
                : 'bg-blue-50 hover:bg-blue-100 text-blue-700 border-blue-200';
            const stockStatusText = outOfStock ? '是' : '否';
            const stockButtonText = outOfStock ? '取消缺货' : '标记缺货';

            return `
                <tr class="hover:bg-slate-50">
                    <td class="px-4 py-3 text-sm text-slate-700 whitespace-nowrap">${dashboardApp.escapeHtml(degreeLabel)}</td>
                    <td class="px-4 py-3 text-sm text-slate-700 whitespace-nowrap">${dashboardApp.escapeHtml(productCode)}</td>
                    <td class="px-4 py-3 text-sm text-slate-500 whitespace-nowrap">${dashboardApp.escapeHtml(barcode)}</td>
                    <td class="w-28 px-4 py-3 text-sm whitespace-nowrap">
                        <span class="inline-flex min-w-[3rem] items-center justify-center px-2 py-1 rounded border text-xs font-medium ${stockStatusClass}">
                            ${stockStatusText}
                        </span>
                    </td>
                    <td class="w-56 px-4 py-3 text-sm whitespace-nowrap">
                        <div class="flex items-center gap-2">
                            <button
                                type="button"
                                class="toggle-out-of-stock-btn px-2 py-1 rounded border text-xs transition-all ${stockButtonClass}"
                                data-id="${item.id}"
                                data-next-value="${outOfStock ? 'false' : 'true'}"
                                data-code="${dashboardApp.escapeHtml(productCode)}"
                                data-degree="${dashboardApp.escapeHtml(degreeLabel)}">
                                ${stockButtonText}
                            </button>
                            <button
                                type="button"
                                class="delete-btn bg-red-50 hover:bg-red-100 text-red-700 border border-red-200 px-2 py-1 rounded text-xs transition-all"
                                data-id="${item.id}"
                                data-code="${dashboardApp.escapeHtml(productCode)}"
                                data-degree="${dashboardApp.escapeHtml(degreeLabel)}">
                                删除
                            </button>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');

        const degreeTableHeaders = elements.catalogDegreeTableBody
            .closest('table')
            ?.querySelectorAll('thead th');
        if (degreeTableHeaders && degreeTableHeaders.length >= 5) {
            degreeTableHeaders[3].classList.add('w-28', 'whitespace-nowrap');
            degreeTableHeaders[4].classList.add('w-56', 'whitespace-nowrap');
        }

        elements.catalogDegreeTableBody.querySelectorAll('.toggle-out-of-stock-btn').forEach(button => {
            button.addEventListener('click', onToggleOutOfStock);
        });
        elements.catalogDegreeTableBody.querySelectorAll('.delete-btn').forEach(button => {
            button.addEventListener('click', onDelete);
        });
    }

    async function onToggleOutOfStock(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        const nextValue = event.currentTarget.dataset.nextValue === 'true';
        const productCode = event.currentTarget.dataset.code || '';
        const degree = event.currentTarget.dataset.degree || '-';
        if (id <= 0) {
            return;
        }

        const confirmMessage = nextValue
            ? `确认将 ${productCode}（度数 ${degree}）标记为缺货吗？`
            : `确认将 ${productCode}（度数 ${degree}）改为不缺货吗？`;
        if (!window.confirm(confirmMessage)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/product-catalog/${id}/out-of-stock`, {
                method: 'PATCH',
                body: {
                    isOutOfStock: nextValue
                }
            });
            dashboardApp.showToast(nextValue ? '已标记为缺货' : '已取消缺货');
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '更新缺货状态失败', 'error');
        }
    }

    function onShowDegrees(event) {
        const groupKey = event.currentTarget.dataset.groupKey || '';
        if (!groupKey || groupKey === state.selectedGroupKey) {
            return;
        }

        state.selectedGroupKey = groupKey;
        renderGroupTable();
    }

    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        const start = state.totalCount === 0 ? 0 : (state.currentPage - 1) * state.pageSize + 1;
        const end = Math.min(state.currentPage * state.pageSize, state.totalCount);
        const summary = `显示 ${start} 到 ${end} 组，共 ${state.totalCount} 组`;

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
            button.className = `relative inline-flex items-center px-3 py-2 border border-slate-300 text-sm ${
                options.active ? 'bg-primary text-white' : 'bg-white text-slate-700 hover:bg-slate-50'
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
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-slate-300 bg-white text-sm text-slate-400';
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
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-slate-300 bg-white text-sm text-slate-400';
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

            const response = await dashboardApp.apiRequest(`/api/product-catalog/query-groups?${query.toString()}`);
            state.groups = response.items || [];
            state.totalCount = response.totalCount || 0;
            state.currentPage = response.pageNumber || state.currentPage;
            normalizeSelectedGroup();

            renderGroupTable();
            renderPagination();
            updateSummaryCards();
        } finally {
            setLoading(false);
        }
    }

    async function loadAllCatalog() {
        return await dashboardApp.apiRequest('/api/product-catalog');
    }

    function openCreateModal(seedGroup = null) {
        elements.modalTitle.textContent = '新增商品编码';
        elements.inputSpecificationToken.value = seedGroup ? normalizeText(seedGroup.specificationToken) : '';
        elements.inputModelToken.value = seedGroup ? normalizeText(seedGroup.modelToken) : '';
        elements.inputDegree.value = '';
        elements.inputBarcode.value = '';
        elements.editModal.classList.remove('hidden');
        if (seedGroup) {
            elements.inputDegree.focus();
        } else {
            elements.inputSpecificationToken.focus();
        }
    }
    function onAddDegreeFromSelectedGroup() {
        const selectedGroup = getSelectedGroup();
        if (!selectedGroup) {
            dashboardApp.showToast('请先从左侧选择一个商品分组', 'error');
            return;
        }

        openCreateModal(selectedGroup);
    }

    function closeModal() {
        elements.editModal.classList.add('hidden');
    }

    function downloadTemplate() {
        const rows = [['商品编码', '条码']];
        const workbook = XLSX.utils.book_new();
        const worksheet = XLSX.utils.aoa_to_sheet(rows);
        XLSX.utils.book_append_sheet(workbook, worksheet, '商品编码模板');
        XLSX.writeFile(workbook, '商品编码导入模板.xlsx');
        dashboardApp.showToast('模板已下载');
    }

    async function onExport() {
        try {
            const entries = await loadAllCatalog();
            const rows = [
                ['周期', '型号', '度数', '编码名称', '商品名称', '条码', '更新时间'],
                ...entries.map(item => [
                    item.specificationToken || '',
                    item.modelToken || '',
                    item.degree || '',
                    item.productCode || '',
                    item.productName || '',
                    item.barcode || '',
                    item.updatedAtUtc || ''
                ])
            ];

            const workbook = XLSX.utils.book_new();
            const worksheet = XLSX.utils.aoa_to_sheet(rows);
            XLSX.utils.book_append_sheet(workbook, worksheet, '商品编码目录');
            XLSX.writeFile(workbook, '商品编码目录导出.xlsx');
            dashboardApp.showToast(`已导出 ${entries.length} 条编码`);
        } catch (error) {
            dashboardApp.showToast(error.message || '导出失败', 'error');
        }
    }

    function readImportEntries(rows) {
        const entries = [];
        let codeKey = '';
        let barcodeKey = '';
        let specificationTokenKey = '';
        let modelTokenKey = '';
        let degreeKey = '';

        rows.forEach(row => {
            if (!codeKey) {
                codeKey = findColumnKey(row, ['编码名称', '商品编码', '编码', 'productcode', 'code']);
            }
            if (!barcodeKey) {
                barcodeKey = findColumnKey(row, ['条码', 'barcode', 'barcodecode']);
            }
            if (!specificationTokenKey) {
                specificationTokenKey = findColumnKey(row, ['周期', '规格', 'specificationtoken', 'wearperiod', 'period']);
            }
            if (!modelTokenKey) {
                modelTokenKey = findColumnKey(row, ['型号', '款式', 'modeltoken', 'model', 'basename']);
            }
            if (!degreeKey) {
                degreeKey = findColumnKey(row, ['度数', 'degree']);
            }
        });

        if (!codeKey && !(specificationTokenKey && modelTokenKey)) {
            throw new Error("Excel 缺少有效表头。标准模板请使用“商品编码”和“条码”两列；兼容模板至少需要“周期”和“型号”列。");
        }

        const invalidRows = [];

        rows.forEach((row, index) => {
            const productCode = codeKey ? normalizeText(row[codeKey]) : '';
            const barcode = barcodeKey ? normalizeText(row[barcodeKey]) : '';
            const specificationToken = specificationTokenKey ? normalizeText(row[specificationTokenKey]) : '';
            const modelToken = modelTokenKey ? normalizeText(row[modelTokenKey]) : '';
            const degree = degreeKey ? normalizeText(row[degreeKey]) : '';

            if (!productCode && !barcode && !specificationToken && !modelToken && !degree) {
                return;
            }

            if (!productCode && !(specificationToken && modelToken)) {
                invalidRows.push(index + 2);
                return;
            }

            entries.push({
                productCode,
                specificationToken,
                modelToken,
                degree,
                barcode,
                isOutOfStock: false
            });
        });

        if (invalidRows.length > 0) {
            const preview = invalidRows.slice(0, 5).join('、');
            const suffix = invalidRows.length > 5 ? ' 等' : '';
            throw new Error(`Excel 第 ${preview}${suffix} 行缺少有效商品编码，且无法从“周期 + 型号”推导，请修正后再导入。`);
        }

        if (entries.length === 0) {
            throw new Error('未识别到可导入的商品编码，请确认表头和数据是否符合标准模板。');
        }

        return entries;
    }

    async function importCatalog(file) {
        const fileName = file && file.name ? file.name : '商品编码导入文件';
        const buffer = await file.arrayBuffer();
        const workbook = XLSX.read(buffer, { type: 'array' });
        const firstSheetName = workbook.SheetNames[0];
        if (!firstSheetName) {
            throw new Error('Excel 文件为空');
        }

        const worksheet = workbook.Sheets[firstSheetName];
        const rows = XLSX.utils.sheet_to_json(worksheet, { defval: '' });
        const entries = readImportEntries(rows);

        const result = await dashboardApp.apiRequest('/api/product-catalog/import', {
            method: 'POST',
            body: {
                sourceFileName: fileName,
                entries
            }
        });

        dashboardApp.showToast(`导入完成：新增 ${result.addedCount}，更新 ${result.updatedCount}，跳过 ${result.skippedCount}`);
        state.currentPage = 1;
        await loadCatalog();
    }

    async function onImportInputChange(event) {
        const file = event.target.files && event.target.files[0];
        event.target.value = '';
        if (!file) {
            return;
        }

        try {
            await importCatalog(file);
        } catch (error) {
            dashboardApp.showToast(error.message || '导入失败', 'error');
        }
    }

    async function onSubmit(event) {
        event.preventDefault();

        const specificationToken = normalizeText(elements.inputSpecificationToken.value);
        const modelToken = normalizeText(elements.inputModelToken.value);
        const degrees = parseDegreeBatchInput(elements.inputDegree.value);
        const barcode = normalizeText(elements.inputBarcode.value);

        if (!specificationToken) {
            dashboardApp.showToast('请填写周期', 'error');
            return;
        }

        if (!modelToken) {
            dashboardApp.showToast('请填写型号', 'error');
            return;
        }

        if (degrees.length === 0) {
            dashboardApp.showToast('请填写度数', 'error');
            return;
        }

        try {
            const entries = degrees.map(degree => ({
                productCode: buildAutoProductCodeForDegree(specificationToken, modelToken, degree),
                specificationToken,
                modelToken,
                degree,
                barcode
            }));

            const result = await dashboardApp.apiRequest('/api/product-catalog/import', {
                method: 'POST',
                body: {
                    sourceFileName: 'manual-batch-create',
                    entries
                }
            });

            closeModal();
            dashboardApp.showToast(`保存完成：新增 ${result.addedCount}，更新 ${result.updatedCount}，跳过 ${result.skippedCount}`);
            state.currentPage = 1;
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存失败', 'error');
        }
    }

    async function onDelete(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        const productCode = event.currentTarget.dataset.code || '';
        const degree = event.currentTarget.dataset.degree || '-';
        if (id <= 0) {
            return;
        }

        if (!window.confirm(`确认删除 ${productCode}（度数 ${degree}）吗？`)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/product-catalog/${id}`, {
                method: 'DELETE'
            });
            dashboardApp.showToast('删除成功');

            const totalPagesBefore = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
            if (state.currentPage > totalPagesBefore - 1 && state.currentPage > 1) {
                state.currentPage -= 1;
            }

            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除失败', 'error');
        }
    }

    async function onSearch() {
        collectFiltersFromInputs();
        state.currentPage = 1;
        await loadCatalog();
    }

    async function onReset() {
        elements.keywordInput.value = '';
        elements.specificationTokenInput.value = '';
        elements.modelTokenInput.value = '';
        elements.degreeInput.value = '';
        collectFiltersFromInputs();
        state.currentPage = 1;
        await loadCatalog();
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
        elements.downloadTemplateBtn.addEventListener('click', downloadTemplate);
        elements.exportBtn.addEventListener('click', onExport);
        elements.importBtn.addEventListener('click', () => elements.importExcelInput.click());
        elements.importExcelInput.addEventListener('change', onImportInputChange);
        elements.addBtn.addEventListener('click', () => openCreateModal());
        if (elements.addDegreeBtn) {
            elements.addDegreeBtn.addEventListener('click', onAddDegreeFromSelectedGroup);
        }
        elements.searchBtn.addEventListener('click', onSearch);
        elements.resetBtn.addEventListener('click', onReset);
        elements.closeModalBtn.addEventListener('click', closeModal);
        elements.cancelBtn.addEventListener('click', closeModal);
        elements.editForm.addEventListener('submit', onSubmit);
        elements.mobilePrevBtn.addEventListener('click', () => goToPage(-1));
        elements.mobileNextBtn.addEventListener('click', () => goToPage(1));

        [
            elements.keywordInput,
            elements.specificationTokenInput,
            elements.modelTokenInput,
            elements.degreeInput
        ].forEach(input => {
            input.addEventListener('keydown', async event => {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    await onSearch();
                }
            });
        });

        elements.editModal.addEventListener('click', event => {
            if (event.target === elements.editModal) {
                closeModal();
            }
        });

        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && !elements.editModal.classList.contains('hidden')) {
                closeModal();
            }
        });
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        elements.currentLoginName.textContent = dashboardApp.getCurrentLoginName() || '-';
        bindEvents();

        try {
            collectFiltersFromInputs();
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载目录失败', 'error');
        }
    });
})();
