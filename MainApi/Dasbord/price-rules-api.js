(function () {
    const RULE_TYPES = {
        base: { label: '单副价', requiresModel: false, requiresQuantity: false, priceLabel: '单副价格', defaultQuantity: 1, allowPrice: true },
        bulk: { label: '多付活动', requiresModel: false, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 2, allowPrice: true },
        clearance: { label: '清仓商品', requiresModel: true, requiresQuantity: false, priceLabel: '清仓价格', defaultQuantity: 0, allowPrice: false },
        clearance_threshold: { label: '清仓门槛', requiresModel: false, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 4, allowPrice: true }
    };

    const state = {
        items: [],
        catalogOptions: [],
        modelsByPeriod: new Map(),
        periods: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        keyword: '',
        editingId: null
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
        inputModelName: document.getElementById('modelName'),
        inputRequiredQuantity: document.getElementById('requiredQuantity'),
        inputValue: document.getElementById('priceValue'),
        modelField: document.getElementById('modelField'),
        quantityField: document.getElementById('quantityField'),
        priceField: document.getElementById('priceField'),
        priceLabel: document.getElementById('priceValueLabel'),
        formHint: document.getElementById('priceRuleFormHint')
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

    function renderModelOptions(selectedWearPeriod = '', selectedModel = '') {
        const models = state.modelsByPeriod.get(normalizeText(selectedWearPeriod)) || [];
        setSelectOptions(
            elements.inputModelName,
            models.map(model => ({ value: model, text: model })),
            normalizeText(selectedWearPeriod) ? '请选择型号' : '请先选择周期',
            selectedModel
        );
    }

    function buildRuleHint(ruleType) {
        switch (normalizeText(ruleType)) {
            case 'bulk':
                return '多付活动按周期统一生效，例如 4 副半年抛 200 元，整包优先，剩余再回落到单副价。';
            case 'clearance':
                return '清仓商品只维护“周期 + 型号”清仓池。保存时不录入价格，只有命中对应周期的清仓门槛后才会按清仓价计算。';
            case 'clearance_threshold':
                return '清仓门槛按周期设置整包数量和整包价格，例如半年抛每 4 副清仓款按 88 元计算。';
            case 'base':
            default:
                return '基础单价按周期统一生效，不再区分颜色系列和型号。';
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

        elements.inputModelName.required = meta.requiresModel;
        elements.inputRequiredQuantity.required = meta.requiresQuantity;
        elements.inputValue.required = meta.allowPrice;
        elements.inputModelName.disabled = !meta.requiresModel;
        elements.inputRequiredQuantity.disabled = !meta.requiresQuantity;
        elements.inputValue.disabled = !meta.allowPrice;

        if (meta.requiresModel) {
            renderModelOptions(elements.inputWearPeriod.value, elements.inputModelName.value);
        }

        if (!meta.requiresQuantity) {
            elements.inputRequiredQuantity.value = String(meta.defaultQuantity);
        }

        if (!meta.allowPrice) {
            elements.inputValue.value = '0';
        }
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

        elements.tableBody.innerHTML = state.items.map(rule => `
            <tr class="hover:bg-slate-50 transition-all">
                <td class="px-4 py-3 text-sm text-slate-500">${rule.id}</td>
                <td class="px-4 py-3 text-sm font-medium text-slate-800">${dashboardApp.escapeHtml(getRuleLabel(rule.ruleType))}</td>
                <td class="px-4 py-3 text-sm text-slate-700">${dashboardApp.escapeHtml(rule.specificationToken || '-')}</td>
                <td class="px-4 py-3 text-sm text-slate-700">${dashboardApp.escapeHtml(rule.modelToken || '-')}</td>
                <td class="px-4 py-3 text-sm text-slate-700">${rule.requiredQuantity || '-'}</td>
                <td class="px-4 py-3 text-sm text-slate-700">${rule.priceValue}</td>
                <td class="px-4 py-3 text-sm text-slate-500">${dashboardApp.formatDateTime(rule.updatedAtUtc)}</td>
                <td class="px-4 py-3 text-sm whitespace-nowrap">
                    <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${rule.id}">编辑</button>
                    <button class="text-rose-600 hover:text-rose-700 delete-btn" data-id="${rule.id}">删除</button>
                </td>
            </tr>
        `).join('');

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
        elements.inputId.value = '';
        elements.inputRuleType.value = 'base';
        renderPeriodOptions();
        renderModelOptions();
        elements.inputRequiredQuantity.value = '1';
        elements.inputValue.value = '0';
        elements.modalTitle.textContent = '新增价格规则';
        refreshFormByRuleType();
    }

    function openModal() {
        elements.modal.classList.remove('hidden');
    }

    function closeModal() {
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
        elements.inputId.value = String(id);
        elements.inputRuleType.value = rule.ruleType;
        renderPeriodOptions(rule.specificationToken);
        renderModelOptions(rule.specificationToken, rule.modelToken);
        elements.inputRequiredQuantity.value = String(rule.requiredQuantity || getRuleMeta(rule.ruleType).defaultQuantity);
        elements.inputValue.value = String(rule.priceValue || 0);
        elements.modalTitle.textContent = '编辑价格规则';
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
        const modelToken = meta.requiresModel ? normalizeText(elements.inputModelName.value) : '';
        const requiredQuantity = meta.requiresQuantity
            ? Number(elements.inputRequiredQuantity.value || meta.defaultQuantity)
            : meta.defaultQuantity;
        const priceValue = meta.allowPrice ? Number(elements.inputValue.value || '0') : 0;

        return {
            ruleType,
            specificationToken,
            modelToken,
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
        if (meta.requiresModel && !body.modelToken) {
            dashboardApp.showToast('请选择型号', 'error');
            return;
        }

        if (meta.requiresQuantity && (!Number.isInteger(body.requiredQuantity) || body.requiredQuantity < 1)) {
            dashboardApp.showToast('数量必须是大于等于 1 的整数', 'error');
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
        const rows = [['规则类型', '周期', '型号', '数量', '价格']];
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
            clearancethreshold: 'clearance_threshold',
            清仓门槛: 'clearance_threshold',
            threshold: 'clearance_threshold'
        };
        return aliases[normalized] || normalized;
    }

    function readImportEntries(rows) {
        const entries = [];
        rows.forEach(row => {
            const ruleTypeKey = findColumnKey(row, ['规则类型', 'ruletype', 'type']);
            const specKey = findColumnKey(row, ['周期', 'wearperiod', 'period']);
            const modelKey = findColumnKey(row, ['型号', 'modeltoken', 'model']);
            const quantityKey = findColumnKey(row, ['数量', 'requiredquantity', 'quantity']);
            const priceKey = findColumnKey(row, ['价格', 'price', 'pricevalue']);

            const ruleType = parseRuleType(ruleTypeKey ? row[ruleTypeKey] : '');
            const specificationToken = normalizeText(specKey ? row[specKey] : '');
            const modelToken = normalizeText(modelKey ? row[modelKey] : '');
            const quantityText = normalizeText(quantityKey ? row[quantityKey] : '');
            const priceText = normalizeText(priceKey ? row[priceKey] : '');

            if (!ruleType && !specificationToken && !modelToken && !quantityText && !priceText) {
                return;
            }

            entries.push({
                ruleType,
                specificationToken,
                modelToken,
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
            renderModelOptions(elements.inputWearPeriod.value);
        });
        elements.searchInput.addEventListener('keydown', async event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                await onSearch();
            }
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
