(function () {
    const IMPORT_MODES = {
        incremental: 'incremental',
        overwrite: 'overwrite',
        stockOut: 'stock_out',
        stockIn: 'stock_in'
    };

    const state = {
        groups: [],
        wearPeriods: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        selectedGroupKey: '',
        filters: {
            keyword: '',
            specificationToken: '',
            modelToken: '',
            degree: ''
        }
    };

    const elements = {
        currentLoginName: document.getElementById('currentLoginName'),
        downloadTemplateBtn: document.getElementById('downloadTemplateBtn'),
        exportBtn: document.getElementById('exportBtn'),
        importBtn: document.getElementById('importBtn'),
        importExcelInput: document.getElementById('importExcelInput'),
        stockOutImportBtn: document.getElementById('stockOutImportBtn'),
        stockOutImportInput: document.getElementById('stockOutImportInput'),
        stockInImportBtn: document.getElementById('stockInImportBtn'),
        stockInImportInput: document.getElementById('stockInImportInput'),
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
        groupSpecificationSelect: document.getElementById('groupSpecificationSelect'),
        specificationTokenOptions: document.getElementById('specificationTokenOptions'),
        saveGroupSpecificationBtn: document.getElementById('saveGroupSpecificationBtn'),
        deleteGroupBtn: document.getElementById('deleteGroupBtn'),
        catalogDegreeTableBody: document.getElementById('catalogDegreeTableBody'),
        pageInfo: document.getElementById('pageInfo'),
        mobilePageInfo: document.getElementById('mobilePageInfo'),
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

    function normalizeGroupToken(value) {
        const normalized = normalizeText(value);
        return normalized === '-' ? '' : normalized;
    }

    function normalizeHeader(value) {
        return normalizeText(value).toLowerCase().replace(/[\s_\-()/\\]/g, '');
    }

    function findColumnKey(row, aliases) {
        for (const key of Object.keys(row || {})) {
            if (aliases.includes(normalizeHeader(key))) {
                return key;
            }
        }
        return '';
    }

    function buildAutoProductCodeForDegree(specificationToken, modelToken, degree) {
        const base = `${normalizeText(specificationToken)}${normalizeText(modelToken)}`.trim();
        return normalizeText(degree) ? `${base}${normalizeText(degree)}` : base;
    }

    function parseDegreeBatchInput(value) {
        return String(value || '')
            .replace(/\r/g, '\n')
            .replace(/[，、；;]+/g, ',')
            .split(/[\n,\s]+/)
            .map(item => normalizeText(item))
            .filter(Boolean);
    }

    function isOutOfStock(value) {
        if (typeof value === 'boolean') {
            return value;
        }

        const normalized = normalizeText(value).toLowerCase();
        return ['1', 'true', 'yes', 'y', '是', '缺货'].includes(normalized) || normalized.includes('缺货');
    }

    function getImportModeLabel(importMode) {
        switch (importMode) {
            case IMPORT_MODES.overwrite:
                return '覆盖导入';
            case IMPORT_MODES.stockOut:
                return '缺货导入';
            case IMPORT_MODES.stockIn:
                return '到货导入';
            default:
                return '增量导入';
        }
    }

    function setLoading(isLoading) {
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        [
            elements.searchBtn,
            elements.resetBtn,
            elements.importBtn,
            elements.stockOutImportBtn,
            elements.stockInImportBtn,
            elements.addBtn,
            elements.addDegreeBtn,
            elements.exportBtn,
            elements.downloadTemplateBtn
        ].forEach(button => {
            if (button) {
                button.disabled = isLoading;
            }
        });
    }

    function collectFiltersFromInputs() {
        state.filters.keyword = normalizeText(elements.keywordInput.value);
        state.filters.specificationToken = normalizeText(elements.specificationTokenInput.value);
        state.filters.modelToken = normalizeText(elements.modelTokenInput.value);
        state.filters.degree = normalizeText(elements.degreeInput.value);
    }

    function buildGroupKey(group) {
        return `${normalizeGroupToken(group.specificationToken)}||${normalizeGroupToken(group.modelToken)}`;
    }

    function normalizeSelectedGroup() {
        if (state.groups.length === 0) {
            state.selectedGroupKey = '';
            return;
        }

        if (!state.selectedGroupKey || !state.groups.some(group => buildGroupKey(group) === state.selectedGroupKey)) {
            state.selectedGroupKey = buildGroupKey(state.groups[0]);
        }
    }

    function getSelectedGroup() {
        return state.groups.find(group => buildGroupKey(group) === state.selectedGroupKey) || null;
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.groups.length);
        elements.totalCountCard.textContent = String(state.totalCount);
        elements.totalDegreeCountCard.textContent = String(state.groups.reduce((sum, item) => sum + (item.degrees || []).length, 0));
    }

    function renderSpecificationTokenOptions(selectedValue) {
        const options = Array.from(new Set([
            ...state.wearPeriods,
            ...state.groups.map(item => normalizeGroupToken(item.specificationToken))
        ].filter(Boolean))).sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'));

        elements.groupSpecificationSelect.innerHTML = [
            '<option value="">选择周期</option>',
            ...options.map(option => `<option value="${dashboardApp.escapeHtml(option)}"${option === selectedValue ? ' selected' : ''}>${dashboardApp.escapeHtml(option)}</option>`)
        ].join('');

        elements.specificationTokenOptions.innerHTML = options
            .map(option => `<option value="${dashboardApp.escapeHtml(option)}"></option>`)
            .join('');
    }

    function renderGroupTable() {
        if (state.groups.length === 0) {
            elements.catalogGroupTableBody.innerHTML = `
                <tr>
                    <td colspan="3" class="px-6 py-10 text-center text-slate-500">
                        暂无商品编码记录
                    </td>
                </tr>
            `;
            renderDegreeTable();
            return;
        }

        elements.catalogGroupTableBody.innerHTML = state.groups.map(group => {
            const groupKey = buildGroupKey(group);
            const isSelected = groupKey === state.selectedGroupKey;
            const degreePreview = (group.degrees || []).slice(0, 3).map(item => normalizeText(item.degree) || '-').join(' / ');
            return `
                <tr class="${isSelected ? 'bg-blue-50' : 'hover:bg-slate-50'}">
                    <td class="px-3 py-4 text-sm text-slate-700 whitespace-nowrap">${dashboardApp.escapeHtml(group.specificationToken || '-')}</td>
                    <td class="px-3 py-4 text-sm">
                        <button type="button" class="show-degrees-btn w-full text-left ${isSelected ? 'border-primary bg-blue-50' : 'border-slate-300 bg-white'} border rounded-md px-3 py-2" data-group-key="${dashboardApp.escapeHtml(groupKey)}">
                            <div class="font-semibold text-slate-800">${dashboardApp.escapeHtml(group.modelToken || '-')}</div>
                            <div class="text-xs text-slate-500 mt-1">${dashboardApp.escapeHtml(degreePreview || '暂无度数')}</div>
                        </button>
                    </td>
                    <td class="px-3 py-4 text-sm text-slate-500 whitespace-nowrap">${dashboardApp.formatDateTime(group.updatedAtUtc)}</td>
                </tr>
            `;
        }).join('');

        elements.catalogGroupTableBody.querySelectorAll('.show-degrees-btn').forEach(button => {
            button.addEventListener('click', () => {
                state.selectedGroupKey = button.dataset.groupKey || '';
                renderGroupTable();
            });
        });

        renderDegreeTable();
    }

    function renderDegreeTable() {
        const selectedGroup = getSelectedGroup();
        if (!selectedGroup) {
            elements.catalogDetailTitle.textContent = '度数明细';
            renderSpecificationTokenOptions('');
            elements.saveGroupSpecificationBtn.disabled = true;
            elements.deleteGroupBtn.disabled = true;
            elements.catalogDegreeTableBody.innerHTML = `
                <tr>
                    <td colspan="5" class="px-4 py-8 text-center text-sm text-slate-400">请先从左侧选择一个型号</td>
                </tr>
            `;
            return;
        }

        elements.catalogDetailTitle.textContent = `度数明细：${selectedGroup.specificationToken || '-'} / ${selectedGroup.modelToken || '-'}`;
        renderSpecificationTokenOptions(normalizeGroupToken(selectedGroup.specificationToken));
        elements.saveGroupSpecificationBtn.disabled = false;
        elements.deleteGroupBtn.disabled = false;

        const degrees = selectedGroup.degrees || [];
        if (degrees.length === 0) {
            elements.catalogDegreeTableBody.innerHTML = `
                <tr>
                    <td colspan="5" class="px-4 py-8 text-center text-sm text-slate-400">当前分组还没有度数</td>
                </tr>
            `;
            return;
        }

        elements.catalogDegreeTableBody.innerHTML = degrees.map(item => {
            const outOfStock = isOutOfStock(item.isOutOfStock);
            return `
                <tr class="hover:bg-slate-50">
                    <td class="px-4 py-3 text-sm text-slate-700">${dashboardApp.escapeHtml(item.degree || '-')}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${dashboardApp.escapeHtml(item.productCode || '-')}</td>
                    <td class="px-4 py-3 text-sm text-slate-500">${dashboardApp.escapeHtml(item.barcode || '-')}</td>
                    <td class="px-4 py-3 text-sm">${outOfStock ? '是' : '否'}</td>
                    <td class="px-4 py-3 text-sm whitespace-nowrap">
                        <button type="button" class="toggle-out-of-stock-btn px-2 py-1 rounded border text-xs ${outOfStock ? 'bg-slate-100' : 'bg-blue-50 text-blue-700'}" data-id="${item.id}" data-next-value="${outOfStock ? 'false' : 'true'}" data-code="${dashboardApp.escapeHtml(item.productCode || '')}" data-degree="${dashboardApp.escapeHtml(item.degree || '-')}">
                            ${outOfStock ? '取消缺货' : '标记缺货'}
                        </button>
                        <button type="button" class="delete-degree-btn bg-red-50 text-red-700 border border-red-200 px-2 py-1 rounded text-xs ml-2" data-id="${item.id}" data-code="${dashboardApp.escapeHtml(item.productCode || '')}" data-degree="${dashboardApp.escapeHtml(item.degree || '-')}">
                            删除
                        </button>
                    </td>
                </tr>
            `;
        }).join('');

        elements.catalogDegreeTableBody.querySelectorAll('.toggle-out-of-stock-btn').forEach(button => {
            button.addEventListener('click', onToggleOutOfStock);
        });
        elements.catalogDegreeTableBody.querySelectorAll('.delete-degree-btn').forEach(button => {
            button.addEventListener('click', onDeleteDegree);
        });
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
    }

    async function loadWearPeriodSettings() {
        const response = await dashboardApp.apiRequest('/api/wear-period-settings');
        state.wearPeriods = (response.wearPeriods || []).map(item => normalizeText(item.value)).filter(Boolean);
        renderSpecificationTokenOptions('');
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
            state.currentPage = response.pageNumber || 1;
            normalizeSelectedGroup();
            renderGroupTable();
            renderPagination();
            updateSummaryCards();
        } finally {
            setLoading(false);
        }
    }

    async function loadAllCatalog() {
        return dashboardApp.apiRequest('/api/product-catalog');
    }

    function openCreateModal(seedGroup = null) {
        elements.modalTitle.textContent = '新增商品编码';
        elements.inputSpecificationToken.value = seedGroup ? normalizeGroupToken(seedGroup.specificationToken) : '';
        elements.inputModelToken.value = seedGroup ? normalizeGroupToken(seedGroup.modelToken) : '';
        elements.inputDegree.value = '';
        elements.inputBarcode.value = '';
        elements.editModal.classList.remove('hidden');
    }

    function closeModal() {
        elements.editModal.classList.add('hidden');
    }

    function downloadTemplate() {
        const rows = [['商品编码', '条码']];
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(rows), '商品编码模板');
        XLSX.writeFile(workbook, '商品编码导入模板.xlsx');
        dashboardApp.showToast('模板已下载');
    }

    async function onExport() {
        try {
            const entries = await loadAllCatalog();
            const rows = [
                ['周期', '型号', '度数', '商品编码', '商品名称', '条码', '缺货', '更新时间'],
                ...entries.map(item => [
                    item.specificationToken || '',
                    item.modelToken || '',
                    item.degree || '',
                    item.productCode || '',
                    item.productName || '',
                    item.barcode || '',
                    item.isOutOfStock ? '是' : '否',
                    item.updatedAtUtc || ''
                ])
            ];
            const workbook = XLSX.utils.book_new();
            XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(rows), '商品编码目录');
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
                codeKey = findColumnKey(row, ['商品编码', '编码名称', '编码', 'productcode', 'code']);
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
            throw new Error('Excel 至少需要“商品编码”列；如果没有商品编码，则至少需要“周期”和“型号”列。');
        }

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
                throw new Error(`Excel 第 ${index + 2} 行缺少商品编码，且无法从周期和型号推导。`);
            }

            entries.push({
                productCode: productCode || buildAutoProductCodeForDegree(specificationToken, modelToken, degree),
                specificationToken,
                modelToken,
                degree,
                barcode,
                isOutOfStock: false
            });
        });

        if (entries.length === 0) {
            throw new Error('未识别到可导入的数据。');
        }

        return entries;
    }

    async function importCatalog(file, importMode) {
        const buffer = await file.arrayBuffer();
        const workbook = XLSX.read(buffer, { type: 'array' });
        const firstSheetName = workbook.SheetNames[0];
        if (!firstSheetName) {
            throw new Error('Excel 文件为空');
        }

        const rows = XLSX.utils.sheet_to_json(workbook.Sheets[firstSheetName], { defval: '' });
        const entries = readImportEntries(rows);
        const result = await dashboardApp.apiRequest('/api/product-catalog/import', {
            method: 'POST',
            body: {
                sourceFileName: file.name || '商品编码导入文件',
                importMode,
                entries
            }
        });

        dashboardApp.showToast(`${getImportModeLabel(importMode)}完成：新增 ${result.addedCount}，更新 ${result.updatedCount}，跳过 ${result.skippedCount}`);
        state.currentPage = 1;
        await loadCatalog();
    }

    async function onImportInputChange(event, importMode) {
        const file = event.target.files && event.target.files[0];
        event.target.value = '';
        if (!file) {
            return;
        }

        try {
            await importCatalog(file, importMode);
        } catch (error) {
            dashboardApp.showToast(error.message || `${getImportModeLabel(importMode)}失败`, 'error');
        }
    }

    async function onToggleOutOfStock(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        const nextValue = event.currentTarget.dataset.nextValue === 'true';
        const productCode = event.currentTarget.dataset.code || '';
        const degree = event.currentTarget.dataset.degree || '-';
        if (id <= 0) {
            return;
        }

        if (!window.confirm(`${nextValue ? '确认标记缺货' : '确认改为有货'}：${productCode}（${degree}）？`)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/product-catalog/${id}/out-of-stock`, {
                method: 'PATCH',
                body: { isOutOfStock: nextValue }
            });
            dashboardApp.showToast(nextValue ? '已标记为缺货' : '已改为有货');
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '更新库存状态失败', 'error');
        }
    }

    async function onSaveGroupSpecification() {
        const selectedGroup = getSelectedGroup();
        if (!selectedGroup) {
            dashboardApp.showToast('请先选择一个型号分组', 'error');
            return;
        }

        const targetSpecificationToken = normalizeText(elements.groupSpecificationSelect.value);
        if (!targetSpecificationToken) {
            dashboardApp.showToast('请选择要保存的周期', 'error');
            return;
        }

        try {
            await dashboardApp.apiRequest('/api/product-catalog/group-specification', {
                method: 'PATCH',
                body: {
                    specificationToken: normalizeGroupToken(selectedGroup.specificationToken),
                    modelToken: normalizeGroupToken(selectedGroup.modelToken),
                    targetSpecificationToken
                }
            });
            dashboardApp.showToast('周期已保存，商品编码未改动');
            state.selectedGroupKey = `${targetSpecificationToken}||${normalizeGroupToken(selectedGroup.modelToken)}`;
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存周期失败', 'error');
        }
    }

    async function onDeleteGroup() {
        const selectedGroup = getSelectedGroup();
        if (!selectedGroup) {
            dashboardApp.showToast('请先选择一个型号分组', 'error');
            return;
        }

        if (!window.confirm(`确认删除型号 ${selectedGroup.specificationToken || '-'} / ${selectedGroup.modelToken || '-'} 下的全部度数吗？`)) {
            return;
        }

        try {
            const query = new URLSearchParams({
                specificationToken: normalizeGroupToken(selectedGroup.specificationToken),
                modelToken: normalizeGroupToken(selectedGroup.modelToken)
            });
            await dashboardApp.apiRequest(`/api/product-catalog/group?${query.toString()}`, { method: 'DELETE' });
            dashboardApp.showToast('型号已删除');
            state.selectedGroupKey = '';
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除型号失败', 'error');
        }
    }

    async function onDeleteDegree(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        const productCode = event.currentTarget.dataset.code || '';
        if (id <= 0) {
            return;
        }

        if (!window.confirm(`确认删除 ${productCode} 吗？`)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/product-catalog/${id}`, { method: 'DELETE' });
            dashboardApp.showToast('删除成功');
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除失败', 'error');
        }
    }

    async function onSubmit(event) {
        event.preventDefault();
        const specificationToken = normalizeText(elements.inputSpecificationToken.value);
        const modelToken = normalizeText(elements.inputModelToken.value);
        const degrees = parseDegreeBatchInput(elements.inputDegree.value);
        const barcode = normalizeText(elements.inputBarcode.value);

        if (!specificationToken || !modelToken || degrees.length === 0) {
            dashboardApp.showToast('请填写周期、型号和度数', 'error');
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
                    importMode: IMPORT_MODES.incremental,
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

    function bindEvents() {
        elements.downloadTemplateBtn.addEventListener('click', downloadTemplate);
        elements.exportBtn.addEventListener('click', onExport);
        elements.importBtn.addEventListener('click', () => elements.importExcelInput.click());
        elements.stockOutImportBtn.addEventListener('click', () => elements.stockOutImportInput.click());
        elements.stockInImportBtn.addEventListener('click', () => elements.stockInImportInput.click());
        elements.importExcelInput.addEventListener('change', event => onImportInputChange(event, IMPORT_MODES.overwrite));
        elements.stockOutImportInput.addEventListener('change', event => onImportInputChange(event, IMPORT_MODES.stockOut));
        elements.stockInImportInput.addEventListener('change', event => onImportInputChange(event, IMPORT_MODES.stockIn));
        elements.addBtn.addEventListener('click', () => openCreateModal());
        elements.addDegreeBtn.addEventListener('click', () => {
            const selectedGroup = getSelectedGroup();
            if (!selectedGroup) {
                dashboardApp.showToast('请先选择一个分组', 'error');
                return;
            }
            openCreateModal(selectedGroup);
        });
        elements.saveGroupSpecificationBtn.addEventListener('click', onSaveGroupSpecification);
        elements.deleteGroupBtn.addEventListener('click', onDeleteGroup);
        elements.searchBtn.addEventListener('click', async () => {
            collectFiltersFromInputs();
            state.currentPage = 1;
            await loadCatalog();
        });
        elements.resetBtn.addEventListener('click', async () => {
            elements.keywordInput.value = '';
            elements.specificationTokenInput.value = '';
            elements.modelTokenInput.value = '';
            elements.degreeInput.value = '';
            collectFiltersFromInputs();
            state.currentPage = 1;
            await loadCatalog();
        });
        elements.closeModalBtn.addEventListener('click', closeModal);
        elements.cancelBtn.addEventListener('click', closeModal);
        elements.editForm.addEventListener('submit', onSubmit);
        elements.mobilePrevBtn.addEventListener('click', async () => {
            if (state.currentPage > 1) {
                state.currentPage -= 1;
                await loadCatalog();
            }
        });
        elements.mobileNextBtn.addEventListener('click', async () => {
            if (state.currentPage < Math.max(1, Math.ceil(state.totalCount / state.pageSize))) {
                state.currentPage += 1;
                await loadCatalog();
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
            await loadWearPeriodSettings();
            collectFiltersFromInputs();
            await loadCatalog();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载目录失败', 'error');
        }
    });
})();
