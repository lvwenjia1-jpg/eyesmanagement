(function () {
    const RULE_TYPES = {
        base: { label: '单副价', requiresModel: false, requiresQuantity: false, priceLabel: '单副价格', defaultQuantity: 1, allowPrice: true },
        bulk: { label: '多付活动', requiresModel: false, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 2, allowPrice: true },
        clearance: { label: '清仓规则', requiresModel: true, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 4, allowPrice: true }
    };

    const MODEL_TOKEN_SEPARATORS = /[,\uFF0C;\uFF1B\u3001|\r\n]+/;
    const state = {
        items: [],
        catalogOptions: [],
        modelsByPeriod: new Map(),
        periods: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        keyword: '',
        editingId: null,
        selectedModelTokens: [],
        isModelDropdownOpen: false
    };

    const elements = {
        tableBody: document.getElementById('priceRulesTableBody'),
        addBtn: document.getElementById('addPriceRuleBtn'),
        downloadTemplateBtn: document.getElementById('downloadTemplateBtn'),
        importBtn: document.getElementById('importExcelBtn'),
        importInput: document.getElementById('importExcelInput'),
        searchInput: document.getElementById('searchInput'),
        searchBtn: document.getElementById('searchBtn'),
        resetBtn: document.getElementById('resetBtn'),
        pageInfo: document.getElementById('pageInfo'),
        mobilePageInfo: document.getElementById('mobilePageInfo'),
        pagination: document.getElementById('pagination'),
        mobilePrevBtn: document.getElementById('mobilePrevBtn'),
        mobileNextBtn: document.getElementById('mobileNextBtn'),
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
        inputRuleType: document.getElementById('ruleType'),
        inputWearPeriod: document.getElementById('wearPeriod'),
        inputRequiredQuantity: document.getElementById('requiredQuantity'),
        inputValue: document.getElementById('priceValue'),
        modelField: document.getElementById('modelField'),
        quantityField: document.getElementById('quantityField'),
        priceField: document.getElementById('priceField'),
        priceLabel: document.getElementById('priceValueLabel'),
        formHint: document.getElementById('priceRuleFormHint'),
        modelDropdownBtn: document.getElementById('modelDropdownBtn'),
        modelSelectionSummary: document.getElementById('modelSelectionSummary'),
        modelDropdownPanel: document.getElementById('modelDropdownPanel'),
        modelOptionsList: document.getElementById('modelOptionsList'),
        selectAllModelsBtn: document.getElementById('selectAllModelsBtn'),
        clearModelsBtn: document.getElementById('clearModelsBtn')
    };

    function normalizeText(value) {
        return String(value ?? '').trim();
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

    function normalizeModelTokens(values) {
        const source = Array.isArray(values) ? values : [values];
        const result = [];
        source.forEach(value => {
            String(value ?? '')
                .split(MODEL_TOKEN_SEPARATORS)
                .map(item => normalizeText(item))
                .filter(Boolean)
                .forEach(item => {
                    if (!result.some(existing => existing.localeCompare(item, 'zh-Hans-CN', { sensitivity: 'accent' }) === 0)) {
                        result.push(item);
                    }
                });
        });
        result.sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
        return result;
    }

    function formatModelSummary(models) {
        if (!models.length) {
            return '-';
        }

        if (models.length <= 3) {
            return models.join('、');
        }

        return `${models.slice(0, 3).join('、')} 等 ${models.length} 款`;
    }

    function getRuleMeta(ruleType) {
        return RULE_TYPES[normalizeText(ruleType)] || RULE_TYPES.base;
    }

    function getRuleLabel(ruleType) {
        return getRuleMeta(ruleType).label;
    }

    function rebuildCatalogIndex() {
        state.modelsByPeriod = new Map();
        state.periods = [];

        state.catalogOptions.forEach(option => {
            const period = normalizeText(option.specificationToken);
            const model = normalizeText(option.modelToken);
            if (!period) {
                return;
            }

            if (!state.periods.includes(period)) {
                state.periods.push(period);
            }

            if (!state.modelsByPeriod.has(period)) {
                state.modelsByPeriod.set(period, []);
            }

            if (model) {
                const models = state.modelsByPeriod.get(period);
                if (!models.includes(model)) {
                    models.push(model);
                    models.sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
                }
            }
        });

        state.periods.sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
    }

    function setSelectOptions(selectElement, options, placeholder, selectedValue) {
        const normalizedSelectedValue = normalizeText(selectedValue);
        const placeholderHtml = `<option value="">${dashboardApp.escapeHtml(placeholder)}</option>`;
        const optionHtml = options.map(option => {
            const value = normalizeText(option.value);
            const selected = value === normalizedSelectedValue ? ' selected' : '';
            return `<option value="${dashboardApp.escapeHtml(value)}"${selected}>${dashboardApp.escapeHtml(option.text)}</option>`;
        }).join('');
        selectElement.innerHTML = `${placeholderHtml}${optionHtml}`;
        selectElement.value = normalizedSelectedValue;
    }

    function renderPeriodOptions(selectedWearPeriod = '') {
        setSelectOptions(
            elements.inputWearPeriod,
            state.periods.map(period => ({ value: period, text: period })),
            '请选择周期',
            selectedWearPeriod
        );
    }

    function getAvailableModels() {
        return state.modelsByPeriod.get(normalizeText(elements.inputWearPeriod.value)) || [];
    }

    function closeModelDropdown() {
        state.isModelDropdownOpen = false;
        elements.modelDropdownPanel.classList.add('hidden');
    }

    function updateModelSelectionSummary() {
        const availableModels = getAvailableModels();
        if (!normalizeText(elements.inputWearPeriod.value)) {
            elements.modelSelectionSummary.textContent = '请先选择周期';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        if (availableModels.length === 0) {
            elements.modelSelectionSummary.textContent = '当前周期没有可选型号';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        if (state.selectedModelTokens.length === 0) {
            elements.modelSelectionSummary.textContent = '请选择一个或多个型号';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        elements.modelSelectionSummary.textContent = formatModelSummary(state.selectedModelTokens);
        elements.modelSelectionSummary.className = 'text-slate-700';
    }

    function syncSelectedModelsWithCurrentPeriod() {
        const available = new Set(getAvailableModels());
        state.selectedModelTokens = state.selectedModelTokens.filter(model => available.has(model));
    }

    function renderModelOptions() {
        syncSelectedModelsWithCurrentPeriod();
        const models = getAvailableModels();

        if (!normalizeText(elements.inputWearPeriod.value)) {
            elements.modelOptionsList.innerHTML = '<div class="px-3 py-3 text-sm text-slate-400">请先选择周期</div>';
            updateModelSelectionSummary();
            return;
        }

        if (models.length === 0) {
            elements.modelOptionsList.innerHTML = '<div class="px-3 py-3 text-sm text-slate-400">当前周期没有可选型号</div>';
            updateModelSelectionSummary();
            return;
        }

        const selectedSet = new Set(state.selectedModelTokens);
        elements.modelOptionsList.innerHTML = models.map(model => `
            <label class="flex items-center gap-3 px-3 py-2 hover:bg-slate-50 cursor-pointer">
                <input type="checkbox" class="model-option h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary" value="${dashboardApp.escapeHtml(model)}" ${selectedSet.has(model) ? 'checked' : ''}>
                <span class="text-sm text-slate-700">${dashboardApp.escapeHtml(model)}</span>
            </label>
        `).join('');

        elements.modelOptionsList.querySelectorAll('.model-option').forEach(input => {
            input.addEventListener('change', () => {
                state.selectedModelTokens = Array.from(elements.modelOptionsList.querySelectorAll('.model-option:checked'))
                    .map(item => normalizeText(item.value))
                    .sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
                updateModelSelectionSummary();
            });
        });

        updateModelSelectionSummary();
    }

    function buildRuleHint(ruleType) {
        switch (normalizeText(ruleType)) {
            case 'bulk':
                return '多付活动按周期统一生效，例如 4 副半年抛 200 元，整包优先，剩余数量再回落到单副价。';
            case 'clearance':
                return '清仓规则会把一个周期下的多个型号绑定成同一清仓池，并按“整包数量 + 整包价格”优先计价。';
            case 'base':
            default:
                return '基础单价按周期统一生效，不再区分型号。';
        }
    }

    function refreshFormByRuleType() {
        const ruleType = normalizeText(elements.inputRuleType.value);
        const meta = getRuleMeta(ruleType);

        elements.modelField.classList.toggle('hidden', !meta.requiresModel);
        elements.quantityField.classList.toggle('hidden', !meta.requiresQuantity);
        elements.priceField.classList.toggle('hidden', !meta.allowPrice);
        elements.priceLabel.textContent = meta.priceLabel;
        elements.formHint.textContent = buildRuleHint(ruleType);
        elements.inputRequiredQuantity.required = meta.requiresQuantity;
        elements.inputValue.required = meta.allowPrice;
        elements.inputRequiredQuantity.disabled = !meta.requiresQuantity;
        elements.inputValue.disabled = !meta.allowPrice;
        elements.modelDropdownBtn.disabled = !meta.requiresModel;
        elements.modelDropdownBtn.classList.toggle('bg-slate-100', !meta.requiresModel);
        elements.modelDropdownBtn.classList.toggle('cursor-not-allowed', !meta.requiresModel);

        if (!meta.requiresModel) {
            state.selectedModelTokens = [];
            closeModelDropdown();
        }

        if (!meta.requiresQuantity) {
            elements.inputRequiredQuantity.value = String(meta.defaultQuantity);
        }

        if (!meta.allowPrice) {
            elements.inputValue.value = '0';
        }

        renderModelOptions();
    }

    function setLoading(isLoading) {
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        [
            elements.searchBtn,
            elements.resetBtn,
            elements.addBtn,
            elements.downloadTemplateBtn,
            elements.importBtn
        ].forEach(button => {
            button.disabled = isLoading;
        });
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.items.length);
        elements.totalCountCard.textContent = String(state.totalCount);
        elements.filterSummaryCard.textContent = state.keyword ? `关键词：${state.keyword}` : '全部规则';
    }

    function getRuleModels(rule) {
        return normalizeModelTokens(rule.modelTokens && rule.modelTokens.length ? rule.modelTokens : rule.modelToken);
    }

    function renderTable() {
        if (state.items.length === 0) {
            elements.tableBody.innerHTML = `
                <tr>
                    <td colspan="8" class="px-6 py-10 text-center text-slate-500">
                        暂无价格规则
                    </td>
                </tr>
            `;
            return;
        }

        elements.tableBody.innerHTML = state.items.map(rule => {
            const models = getRuleModels(rule);
            return `
                <tr class="hover:bg-slate-50 transition-all">
                    <td class="px-4 py-3 text-sm text-slate-500">${rule.id}</td>
                    <td class="px-4 py-3 text-sm font-medium text-slate-800">${dashboardApp.escapeHtml(getRuleLabel(rule.ruleType))}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${dashboardApp.escapeHtml(rule.specificationToken || '-')}</td>
                    <td class="px-4 py-3 text-sm text-slate-700" title="${dashboardApp.escapeHtml(models.join('、'))}">${dashboardApp.escapeHtml(formatModelSummary(models))}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${rule.requiredQuantity || '-'}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${rule.priceValue}</td>
                    <td class="px-4 py-3 text-sm text-slate-500">${dashboardApp.formatDateTime(rule.updatedAtUtc)}</td>
                    <td class="px-4 py-3 text-sm whitespace-nowrap">
                        <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${rule.id}">编辑</button>
                        <button class="text-rose-600 hover:text-rose-700 delete-btn" data-id="${rule.id}">删除</button>
                    </td>
                </tr>
            `;
        }).join('');

        elements.tableBody.querySelectorAll('.edit-btn').forEach(button => button.addEventListener('click', onEdit));
        elements.tableBody.querySelectorAll('.delete-btn').forEach(button => button.addEventListener('click', onDeletePriceRule));
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
        elements.pagination.innerHTML = '';
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

    async function loadCatalogOptions() {
        state.catalogOptions = await dashboardApp.apiRequest('/api/price-rules/catalog-options');
        rebuildCatalogIndex();
        renderPeriodOptions();
        renderModelOptions();
    }

    function resetForm() {
        state.editingId = null;
        state.selectedModelTokens = [];
        elements.inputId.value = '';
        elements.inputRuleType.value = 'base';
        renderPeriodOptions();
        elements.inputRequiredQuantity.value = '1';
        elements.inputValue.value = '0';
        elements.modalTitle.textContent = '新增价格规则';
        closeModelDropdown();
        refreshFormByRuleType();
    }

    function openModal() {
        elements.modal.classList.remove('hidden');
    }

    function closeModal() {
        closeModelDropdown();
        elements.modal.classList.add('hidden');
    }

    function onAdd() {
        resetForm();
        openModal();
    }

    function onEdit(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        const rule = state.items.find(item => item.id === id);
        if (!rule) {
            return;
        }

        state.editingId = id;
        state.selectedModelTokens = getRuleModels(rule);
        elements.inputId.value = String(id);
        elements.inputRuleType.value = normalizeText(rule.ruleType) || 'base';
        renderPeriodOptions(rule.specificationToken);
        elements.inputRequiredQuantity.value = String(rule.requiredQuantity || getRuleMeta(rule.ruleType).defaultQuantity);
        elements.inputValue.value = String(rule.priceValue || 0);
        elements.modalTitle.textContent = '编辑价格规则';
        closeModelDropdown();
        refreshFormByRuleType();
        openModal();
    }

    async function onDeletePriceRule(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        if (id <= 0 || !window.confirm('确认删除这条价格规则吗？')) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/price-rules/${id}`, { method: 'DELETE' });
            dashboardApp.showToast('价格规则已删除');
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除价格规则失败', 'error');
        }
    }

    async function onSearch() {
        state.keyword = normalizeText(elements.searchInput.value);
        state.currentPage = 1;
        await loadPriceRules();
    }

    async function onReset() {
        elements.searchInput.value = '';
        state.keyword = '';
        state.currentPage = 1;
        await loadPriceRules();
    }

    function getRequestBody() {
        const ruleType = normalizeText(elements.inputRuleType.value);
        const meta = getRuleMeta(ruleType);
        const specificationToken = normalizeText(elements.inputWearPeriod.value);
        const modelTokens = meta.requiresModel ? state.selectedModelTokens.slice() : [];
        const requiredQuantity = meta.requiresQuantity
            ? Number(elements.inputRequiredQuantity.value || meta.defaultQuantity)
            : meta.defaultQuantity;
        const priceValue = meta.allowPrice ? Number(elements.inputValue.value || '0') : 0;

        return {
            ruleType,
            specificationToken,
            modelToken: modelTokens.join('|'),
            modelTokens,
            requiredQuantity,
            priceValue,
            isActive: true
        };
    }

    async function onSubmit(event) {
        event.preventDefault();
        refreshFormByRuleType();

        const body = getRequestBody();
        if (!body.specificationToken) {
            dashboardApp.showToast('请选择周期', 'error');
            return;
        }

        const meta = getRuleMeta(body.ruleType);
        if (meta.requiresModel && body.modelTokens.length === 0) {
            dashboardApp.showToast('请至少选择一个型号', 'error');
            return;
        }

        if (meta.requiresQuantity && (!Number.isInteger(body.requiredQuantity) || body.requiredQuantity < 1)) {
            dashboardApp.showToast('整包数量必须是大于等于 1 的整数', 'error');
            return;
        }

        if (body.ruleType === 'bulk' && body.requiredQuantity < 2) {
            dashboardApp.showToast('多付活动数量必须大于等于 2', 'error');
            return;
        }

        if (meta.allowPrice && (!Number.isInteger(body.priceValue) || body.priceValue < 0)) {
            dashboardApp.showToast('价格必须是大于等于 0 的整数', 'error');
            return;
        }

        try {
            if (state.editingId) {
                await dashboardApp.apiRequest(`/api/price-rules/${state.editingId}`, {
                    method: 'PUT',
                    body
                });
                dashboardApp.showToast('价格规则已更新');
            } else {
                await dashboardApp.apiRequest('/api/price-rules', {
                    method: 'POST',
                    body
                });
                dashboardApp.showToast('价格规则已创建');
            }

            closeModal();
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存价格规则失败', 'error');
        }
    }

    function downloadTemplate() {
        const rows = [['规则类型', '周期', '型号集合', '数量', '价格']];
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(rows), '价格规则模板');
        XLSX.writeFile(workbook, '价格规则导入模板.xlsx');
    }

    function parseRuleType(value) {
        const normalized = normalizeText(value).toLowerCase();
        const aliases = {
            base: 'base',
            单副: 'base',
            单副价: 'base',
            基础: 'base',
            bulk: 'bulk',
            多付: 'bulk',
            多副: 'bulk',
            clearance: 'clearance',
            清仓: 'clearance',
            清仓规则: 'clearance',
            clearancethreshold: 'clearance',
            清仓门槛: 'clearance',
            threshold: 'clearance'
        };
        return aliases[normalized] || normalized;
    }

    function readImportEntries(rows) {
        const entries = [];
        rows.forEach(row => {
            const ruleTypeKey = findColumnKey(row, ['规则类型', 'ruletype', 'type']);
            const specKey = findColumnKey(row, ['周期', 'wearperiod', 'period']);
            const modelKey = findColumnKey(row, ['型号集合', '型号', 'modeltokens', 'modeltoken', 'model']);
            const quantityKey = findColumnKey(row, ['数量', 'requiredquantity', 'quantity']);
            const priceKey = findColumnKey(row, ['价格', 'price', 'pricevalue']);

            const ruleType = parseRuleType(ruleTypeKey ? row[ruleTypeKey] : '');
            const specificationToken = normalizeText(specKey ? row[specKey] : '');
            const modelTokens = normalizeModelTokens(modelKey ? row[modelKey] : '');
            const quantityText = normalizeText(quantityKey ? row[quantityKey] : '');
            const priceText = normalizeText(priceKey ? row[priceKey] : '');

            if (!ruleType && !specificationToken && modelTokens.length === 0 && !quantityText && !priceText) {
                return;
            }

            entries.push({
                ruleType,
                specificationToken,
                modelToken: modelTokens.join('|'),
                modelTokens,
                requiredQuantity: quantityText ? Number(quantityText) : 0,
                priceValue: priceText ? Number(priceText) : 0,
                isActive: true
            });
        });

        if (entries.length === 0) {
            throw new Error('未在 Excel 中识别到可导入的价格规则');
        }

        return entries;
    }

    async function importPriceRules(file) {
        const fileName = file && file.name ? file.name : '导入文件';
        const buffer = await file.arrayBuffer();
        const workbook = XLSX.read(buffer, { type: 'array' });
        const sheetName = workbook.SheetNames[0];
        if (!sheetName) {
            throw new Error('Excel 文件为空');
        }

        const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { defval: '' });
        const entries = readImportEntries(rows);
        const result = await dashboardApp.apiRequest('/api/price-rules/import', {
            method: 'POST',
            body: {
                sourceFileName: fileName,
                entries
            }
        });
        dashboardApp.showToast(`导入完成：新增 ${result.createdCount}，更新 ${result.updatedCount}，跳过 ${result.skippedCount}`);
        await loadPriceRules();
    }

    async function onImportInputChange(event) {
        const file = event.target.files && event.target.files[0];
        event.target.value = '';
        if (!file) {
            return;
        }

        try {
            await importPriceRules(file);
        } catch (error) {
            dashboardApp.showToast(error.message || '导入价格规则失败', 'error');
        }
    }

    function bindEvents() {
        elements.addBtn.addEventListener('click', onAdd);
        elements.downloadTemplateBtn.addEventListener('click', downloadTemplate);
        elements.importBtn.addEventListener('click', () => elements.importInput.click());
        elements.importInput.addEventListener('change', onImportInputChange);
        elements.searchBtn.addEventListener('click', onSearch);
        elements.resetBtn.addEventListener('click', onReset);
        elements.closeModalBtn.addEventListener('click', closeModal);
        elements.cancelBtn.addEventListener('click', closeModal);
        elements.form.addEventListener('submit', onSubmit);
        elements.inputRuleType.addEventListener('change', refreshFormByRuleType);
        elements.inputWearPeriod.addEventListener('change', () => {
            state.selectedModelTokens = [];
            renderModelOptions();
        });
        elements.modelDropdownBtn.addEventListener('click', () => {
            if (elements.modelDropdownBtn.disabled) {
                return;
            }

            state.isModelDropdownOpen = !state.isModelDropdownOpen;
            elements.modelDropdownPanel.classList.toggle('hidden', !state.isModelDropdownOpen);
        });
        elements.selectAllModelsBtn.addEventListener('click', () => {
            state.selectedModelTokens = getAvailableModels().slice();
            renderModelOptions();
        });
        elements.clearModelsBtn.addEventListener('click', () => {
            state.selectedModelTokens = [];
            renderModelOptions();
        });
        document.addEventListener('click', event => {
            if (!state.isModelDropdownOpen) {
                return;
            }

            if (elements.modelField.contains(event.target)) {
                return;
            }

            closeModelDropdown();
        });
        elements.searchInput.addEventListener('keydown', async event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                await onSearch();
            }
        });
        elements.mobilePrevBtn.addEventListener('click', async () => {
            if (state.currentPage <= 1) {
                return;
            }

            state.currentPage -= 1;
            await loadPriceRules();
        });
        elements.mobileNextBtn.addEventListener('click', async () => {
            const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
            if (state.currentPage >= totalPages) {
                return;
            }

            state.currentPage += 1;
            await loadPriceRules();
        });
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        elements.currentLoginName.textContent = dashboardApp.getCurrentLoginName() || '-';
        bindEvents();
        await loadCatalogOptions();
        resetForm();
        await loadPriceRules();
    });
})();
