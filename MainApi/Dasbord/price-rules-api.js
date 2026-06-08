(function () {
    const RULE_TYPES = {
        base: { label: '单副价', requiresModel: false, requiresQuantity: false, priceLabel: '单副价格', defaultQuantity: 1, allowPrice: true },
        bulk: { label: '多付活动', requiresModel: false, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 2, allowPrice: true },
        clearance: { label: '清仓规则', requiresModel: true, requiresQuantity: true, priceLabel: '整包价格', defaultQuantity: 4, allowPrice: true }
    };

    const MODEL_TOKEN_SEPARATORS = /[,\uFF0C;\uFF1B\u3001|\r\n]+/;
    const SPECIFICATION_TOKEN_SEPARATORS = /[,\uFF0C;\uFF1B\u3001|\r\n]+/;
    const SORT_OPTIONS = [
        { key: 'id', label: 'ID' },
        { key: 'ruleType', label: '类型' },
        { key: 'specificationToken', label: '价格周期' },
        { key: 'modelToken', label: '型号集合' },
        { key: 'requiredQuantity', label: '整包数量' },
        { key: 'priceValue', label: '价格' },
        { key: 'updatedAtUtc', label: '更新时间' }
    ];

    const state = {
        items: [],
        catalogOptions: [],
        modelsByPeriod: new Map(),
        periods: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        keyword: '',
        sortBy: 'updatedAtUtc',
        sortDirection: 'desc',
        editingId: null,
        selectedWearPeriods: [],
        activeModelWearPeriod: '',
        selectedModelTokens: [],
        modelSelectionsByPeriodKey: new Map(),
        isWearPeriodDropdownOpen: false,
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
        wearPeriodField: document.getElementById('wearPeriodField'),
        wearPeriodDropdownWrapper: document.getElementById('wearPeriodDropdownWrapper'),
        wearPeriodDropdownBtn: document.getElementById('wearPeriodDropdownBtn'),
        wearPeriodSelectionSummary: document.getElementById('wearPeriodSelectionSummary'),
        wearPeriodDropdownPanel: document.getElementById('wearPeriodDropdownPanel'),
        wearPeriodOptionsList: document.getElementById('wearPeriodOptionsList'),
        selectAllWearPeriodsBtn: document.getElementById('selectAllWearPeriodsBtn'),
        clearWearPeriodsBtn: document.getElementById('clearWearPeriodsBtn'),
        wearPeriodHint: document.getElementById('wearPeriodHint'),
        clearanceModelWearPeriodField: document.getElementById('clearanceModelWearPeriodField'),
        clearanceModelWearPeriod: document.getElementById('clearanceModelWearPeriod'),
        inputRequiredQuantity: document.getElementById('requiredQuantity'),
        inputValue: document.getElementById('priceValue'),
        modelField: document.getElementById('modelField'),
        quantityField: document.getElementById('quantityField'),
        priceField: document.getElementById('priceField'),
        priceLabel: document.getElementById('priceValueLabel'),
        formHint: document.getElementById('priceRuleFormHint'),
        modelFieldHint: document.getElementById('modelFieldHint'),
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

    function normalizeSpecificationTokens(values) {
        const source = Array.isArray(values) ? values : [values];
        const result = [];
        source.forEach(value => {
            String(value ?? '')
                .split(SPECIFICATION_TOKEN_SEPARATORS)
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
            return models.join(', ');
        }

        return `${models.slice(0, 3).join(' ')} +${models.length - 3}`;
    }

    function formatSpecificationSummary(periods) {
        if (!periods.length) {
            return '-';
        }

        if (periods.length <= 3) {
            return periods.join(', ');
        }

        return `${periods.slice(0, 3).join(' ')} +${periods.length - 3}`;
    }

    function formatPeriodScopedModelSummary(period, models) {
        const normalizedPeriod = normalizeText(period);
        if (!normalizedPeriod) {
            return formatModelSummary(models);
        }

        if (!models.length) {
            return `${normalizedPeriod}：未选型号`;
        }

        if (models.length <= 2) {
            return `${normalizedPeriod}：${models.join('、')}`;
        }

        return `${normalizedPeriod}：${models.slice(0, 2).join('、')} 等 ${models.length} 个型号`;
    }

    function getRuleMeta(ruleType) {
        return RULE_TYPES[normalizeText(ruleType)] || RULE_TYPES.base;
    }

    function getRuleLabel(ruleType) {
        return getRuleMeta(ruleType).label;
    }

    function getSortIndicator(sortKey) {
        if (state.sortBy !== sortKey) {
            return '->';
        }

        return state.sortDirection === 'asc' ? '↑' : '↓';
    }

    function enhanceSortHeaders() {
        const headerCells = elements.tableBody?.closest('table')?.querySelectorAll('thead th');
        if (!headerCells || headerCells.length < SORT_OPTIONS.length) {
            return;
        }

        SORT_OPTIONS.forEach((option, index) => {
            const cell = headerCells[index];
            if (!cell || cell.dataset.sortEnhanced === 'true') {
                return;
            }

            cell.dataset.sortEnhanced = 'true';
            cell.innerHTML = `
                <button type="button" class="price-sort-btn inline-flex items-center gap-1 text-left text-xs font-medium uppercase tracking-wider text-slate-500 hover:text-slate-700" data-sort-by="${option.key}">
                    <span>${option.label}</span>
                    <span class="sort-indicator text-slate-400" data-sort-indicator="${option.key}">${getSortIndicator(option.key)}</span>
                </button>
            `;
        });

        document.querySelectorAll('.price-sort-btn').forEach(button => {
            button.addEventListener('click', async () => {
                const nextSortBy = button.dataset.sortBy || 'updatedAtUtc';
                if (state.sortBy === nextSortBy) {
                    state.sortDirection = state.sortDirection === 'asc' ? 'desc' : 'asc';
                } else {
                    state.sortBy = nextSortBy;
                    state.sortDirection = nextSortBy === 'updatedAtUtc' ? 'desc' : 'asc';
                }

                state.currentPage = 1;
                renderSortIndicators();
                await loadPriceRules();
            });
        });
    }

    function renderSortIndicators() {
        document.querySelectorAll('[data-sort-indicator]').forEach(element => {
            const sortKey = element.dataset.sortIndicator || '';
            element.textContent = getSortIndicator(sortKey);
            element.className = state.sortBy === sortKey
                ? 'sort-indicator text-primary'
                : 'sort-indicator text-slate-400';
        });
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

    function closeWearPeriodDropdown() {
        state.isWearPeriodDropdownOpen = false;
        elements.wearPeriodDropdownPanel.classList.add('hidden');
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

    function closeModelDropdown() {
        state.isModelDropdownOpen = false;
        elements.modelDropdownPanel.classList.add('hidden');
    }

    function buildRuleHint(ruleType) {
        switch (normalizeText(ruleType)) {
            case 'bulk':
                return '多付活动按价格周期生效，不足整包的部分会回落到单副价。';
            case 'clearance':
                return '清仓规则会把所选价格周期内命中的型号合并计算整包。';
            case 'base':
            default:
                return '单副价仅按价格周期生效。';
        }
    }


    function isClearanceRuleSelected() {
        return normalizeText(elements.inputRuleType.value) === 'clearance';
    }

    function getSelectedWearPeriods() {
        if (isClearanceRuleSelected()) {
            return normalizeSpecificationTokens(state.selectedWearPeriods);
        }

        const value = normalizeText(elements.inputWearPeriod.value);
        return value ? [value] : [];
    }

    function syncActiveModelWearPeriod() {
        if (!isClearanceRuleSelected()) {
            state.activeModelWearPeriod = '';
            return;
        }

        const selectedPeriods = getSelectedWearPeriods();
        if (selectedPeriods.length === 0) {
            state.activeModelWearPeriod = '';
            return;
        }

        const normalizedActivePeriod = normalizeText(state.activeModelWearPeriod);
        if (normalizedActivePeriod && selectedPeriods.includes(normalizedActivePeriod)) {
            state.activeModelWearPeriod = normalizedActivePeriod;
            return;
        }

        state.activeModelWearPeriod = selectedPeriods[0];
    }

    function buildWearPeriodSelectionKey(periods = state.selectedWearPeriods) {
        return normalizeSpecificationTokens(periods).join('|');
    }

    function rememberSelectedModelsForPeriods(periods = state.selectedWearPeriods, modelTokens = state.selectedModelTokens) {
        if (!isClearanceRuleSelected()) {
            return;
        }

        const key = buildWearPeriodSelectionKey(periods);
        if (!key) {
            return;
        }

        state.modelSelectionsByPeriodKey.set(key, normalizeModelTokens(modelTokens));
    }

    function getAvailableModelsForPeriods(periods) {
        const result = [];
        normalizeSpecificationTokens(periods).forEach(period => {
            (state.modelsByPeriod.get(period) || []).forEach(model => {
                if (!result.includes(model)) {
                    result.push(model);
                }
            });
        });
        result.sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
        return result;
    }

    function renderClearanceModelWearPeriodOptions() {
        const isClearance = isClearanceRuleSelected();
        elements.clearanceModelWearPeriodField.classList.toggle('hidden', !isClearance);

        if (!isClearance) {
            setSelectOptions(elements.clearanceModelWearPeriod, [], '请选择周期', '');
            return;
        }

        syncActiveModelWearPeriod();
        const selectedPeriods = getSelectedWearPeriods();
        setSelectOptions(
            elements.clearanceModelWearPeriod,
            selectedPeriods.map(period => ({ value: period, text: period })),
            '请选择周期',
            state.activeModelWearPeriod
        );
    }

    function restoreSelectedModelsForPeriods(periods = state.selectedWearPeriods, fallbackModelTokens = state.selectedModelTokens) {
        if (!isClearanceRuleSelected()) {
            state.selectedModelTokens = [];
            return;
        }

        const key = buildWearPeriodSelectionKey(periods);
        if (!key) {
            state.selectedModelTokens = [];
            return;
        }

        if (state.modelSelectionsByPeriodKey.has(key)) {
            state.selectedModelTokens = normalizeModelTokens(state.modelSelectionsByPeriodKey.get(key) || []);
            return;
        }

        const availableModels = new Set(getAvailableModelsForPeriods(periods));
        state.selectedModelTokens = normalizeModelTokens(fallbackModelTokens).filter(model => availableModels.has(model));
    }

    function getModelViewerWearPeriod() {
        if (!isClearanceRuleSelected()) {
            return getSelectedWearPeriods()[0] || '';
        }

        syncActiveModelWearPeriod();
        return normalizeText(state.activeModelWearPeriod);
    }

    function getDisplayedModelPeriods() {
        const activePeriod = getModelViewerWearPeriod();
        return activePeriod ? [activePeriod] : [];
    }

    function getDisplayedModels() {
        return getAvailableModelsForPeriods(getDisplayedModelPeriods());
    }

    function getSelectedModelsForDisplayedPeriod() {
        const displayedSet = new Set(getDisplayedModels());
        return state.selectedModelTokens.filter(model => displayedSet.has(model));
    }

    function mergeSelectedModelsForDisplayedPeriod(nextDisplayedModels) {
        const displayedSet = new Set(getDisplayedModels());
        const preservedModels = state.selectedModelTokens.filter(model => !displayedSet.has(model));
        state.selectedModelTokens = normalizeModelTokens([...preservedModels, ...nextDisplayedModels]);
    }

    function updateWearPeriodSelectionSummary() {
        const selectedPeriods = getSelectedWearPeriods();
        if (selectedPeriods.length === 0) {
            elements.wearPeriodSelectionSummary.textContent = isClearanceRuleSelected() ? '请选择一个或多个周期' : '请选择一个周期';
            elements.wearPeriodSelectionSummary.className = 'text-slate-500';
            return;
        }

        elements.wearPeriodSelectionSummary.textContent = formatSpecificationSummary(selectedPeriods);
        elements.wearPeriodSelectionSummary.className = 'text-slate-700';
    }

    function renderPeriodOptions(selectedWearPeriods = state.selectedWearPeriods) {
        const normalizedSelected = normalizeSpecificationTokens(selectedWearPeriods);
        const isClearance = isClearanceRuleSelected();
        state.selectedWearPeriods = isClearance ? normalizedSelected : normalizedSelected.slice(0, 1);
        syncActiveModelWearPeriod();
        setSelectOptions(
            elements.inputWearPeriod,
            state.periods.map(period => ({ value: period, text: period })),
            '请选择周期',
            state.selectedWearPeriods[0] || ''
        );
        elements.inputWearPeriod.classList.toggle('hidden', isClearance);
        elements.wearPeriodDropdownWrapper.classList.toggle('hidden', !isClearance);
        elements.wearPeriodHint.textContent = isClearance
            ? '清仓规则可以选择多个价格周期，并将这些周期下的命中型号一起凑整包。'
            : '单副价和多付活动只能选择一个价格周期。';
        elements.selectAllWearPeriodsBtn.classList.toggle('hidden', !isClearance);
        renderClearanceModelWearPeriodOptions();

        if (!isClearance) {
            updateWearPeriodSelectionSummary();
            closeWearPeriodDropdown();
            return;
        }

        if (state.periods.length === 0) {
            elements.wearPeriodOptionsList.innerHTML = '<div class="px-3 py-3 text-sm text-slate-400">暂无可选周期</div>';
            updateWearPeriodSelectionSummary();
            return;
        }

        const selectedSet = new Set(state.selectedWearPeriods);
        elements.wearPeriodOptionsList.innerHTML = state.periods
            .map(period => {
                const checked = selectedSet.has(period) ? 'checked' : '';
                const inputType = isClearance ? 'checkbox' : 'radio';
                return `
                    <label class="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-slate-50">
                        <input type="${inputType}" name="wear-period-option" class="wear-period-option h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary" value="${dashboardApp.escapeHtml(period)}" ${checked}>
                        <span class="text-sm text-slate-700">${dashboardApp.escapeHtml(period)}</span>
                    </label>
                `;
            })
            .join('');

        elements.wearPeriodOptionsList.querySelectorAll('.wear-period-option').forEach(input => {
            input.addEventListener('change', event => {
                const previousModels = state.selectedModelTokens.slice();
                rememberSelectedModelsForPeriods();

                if (isClearance) {
                    state.selectedWearPeriods = Array.from(elements.wearPeriodOptionsList.querySelectorAll('.wear-period-option:checked'))
                        .map(item => normalizeText(item.value))
                        .sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
                } else if (event.currentTarget.checked) {
                    state.selectedWearPeriods = [normalizeText(event.currentTarget.value)];
                } else {
                    state.selectedWearPeriods = [];
                }

                restoreSelectedModelsForPeriods(state.selectedWearPeriods, previousModels);
                renderPeriodOptions(state.selectedWearPeriods);
                renderModelOptions();
                if (!isClearance) {
                    closeWearPeriodDropdown();
                }
            });
        });

        updateWearPeriodSelectionSummary();
    }

    function getAvailableModels() {
        return getDisplayedModels();
    }

    function updateModelSelectionSummary() {
        const selectedWearPeriods = getSelectedWearPeriods();
        const availableModels = getAvailableModels();
        const displayedPeriod = getModelViewerWearPeriod();
        const displayedSelectedModels = getSelectedModelsForDisplayedPeriod();

        if (selectedWearPeriods.length === 0) {
            elements.modelSelectionSummary.textContent = '请先选择周期';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        if (isClearanceRuleSelected() && !displayedPeriod) {
            elements.modelSelectionSummary.textContent = '请选择型号联动周期';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        if (availableModels.length === 0) {
            elements.modelSelectionSummary.textContent = isClearanceRuleSelected() ? '当前联动周期下没有可用型号' : '当前周期下没有可用型号';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        if (isClearanceRuleSelected() ? displayedSelectedModels.length === 0 : state.selectedModelTokens.length === 0) {
            elements.modelSelectionSummary.textContent = isClearanceRuleSelected()
                ? `${displayedPeriod}：未选型号`
                : '请选择一个或多个型号';
            elements.modelSelectionSummary.className = 'text-slate-500';
            return;
        }

        elements.modelSelectionSummary.textContent = isClearanceRuleSelected()
            ? formatPeriodScopedModelSummary(displayedPeriod, displayedSelectedModels)
            : formatModelSummary(state.selectedModelTokens);
        elements.modelSelectionSummary.className = 'text-slate-700';
    }

    function syncSelectedModelsWithCurrentPeriod() {
        const available = new Set(getAvailableModelsForPeriods(getSelectedWearPeriods()));
        state.selectedModelTokens = state.selectedModelTokens.filter(model => available.has(model));
        if (isClearanceRuleSelected()) {
            rememberSelectedModelsForPeriods();
        }
    }

    function renderModelOptions() {
        syncSelectedModelsWithCurrentPeriod();
        const models = getAvailableModels();

        if (getSelectedWearPeriods().length === 0) {
            elements.modelOptionsList.innerHTML = '<div class="px-3 py-3 text-sm text-slate-400">请先选择周期</div>';
            updateModelSelectionSummary();
            return;
        }

        if (isClearanceRuleSelected() && !getModelViewerWearPeriod()) {
            elements.modelOptionsList.innerHTML = '<div class="px-3 py-3 text-sm text-slate-400">请选择型号联动周期</div>';
            updateModelSelectionSummary();
            return;
        }

        if (models.length === 0) {
            elements.modelOptionsList.innerHTML = `<div class="px-3 py-3 text-sm text-slate-400">${isClearanceRuleSelected() ? '当前联动周期下没有可用型号' : '当前周期下没有可用型号'}</div>`;
            updateModelSelectionSummary();
            return;
        }

        const selectedSet = new Set(
            isClearanceRuleSelected() ? getSelectedModelsForDisplayedPeriod() : state.selectedModelTokens
        );
        elements.modelOptionsList.innerHTML = models.map(model => `
            <label class="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-slate-50">
                <input type="checkbox" class="model-option h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary" value="${dashboardApp.escapeHtml(model)}" ${selectedSet.has(model) ? 'checked' : ''}>
                <span class="text-sm text-slate-700">${dashboardApp.escapeHtml(model)}</span>
            </label>
        `).join('');

        elements.modelOptionsList.querySelectorAll('.model-option').forEach(input => {
            input.addEventListener('change', () => {
                const checkedModels = Array.from(elements.modelOptionsList.querySelectorAll('.model-option:checked'))
                    .map(item => normalizeText(item.value))
                    .sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
                if (isClearanceRuleSelected()) {
                    mergeSelectedModelsForDisplayedPeriod(checkedModels);
                    rememberSelectedModelsForPeriods();
                } else {
                    state.selectedModelTokens = checkedModels;
                }
                updateModelSelectionSummary();
            });
        });

        if (isClearanceRuleSelected()) {
            rememberSelectedModelsForPeriods();
        }
        updateModelSelectionSummary();
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
        elements.clearanceModelWearPeriodField.classList.toggle('hidden', !isClearanceRuleSelected() || !meta.requiresModel);
        if (elements.modelFieldHint) {
            elements.modelFieldHint.textContent = isClearanceRuleSelected()
                ? '先用上方多选框决定清仓规则覆盖的价格周期，再切换型号联动周期编辑该周期下的型号。'
                : '支持批量选择多个型号，命中的型号会共用同一条清仓整包规则。';
        }

        if (!meta.requiresModel) {
            closeModelDropdown();
        }

        if (!meta.requiresQuantity) {
            elements.inputRequiredQuantity.value = String(meta.defaultQuantity);
        }

        if (!meta.allowPrice) {
            elements.inputValue.value = '0';
        }

        if (!isClearanceRuleSelected() && state.selectedWearPeriods.length > 1) {
            state.selectedWearPeriods = state.selectedWearPeriods.slice(0, 1);
        }

        if (!isClearanceRuleSelected() && state.selectedWearPeriods.length === 0) {
            const currentWearPeriod = normalizeText(elements.inputWearPeriod.value);
            state.selectedWearPeriods = currentWearPeriod ? [currentWearPeriod] : [];
        }

        syncActiveModelWearPeriod();
        renderPeriodOptions(state.selectedWearPeriods);
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
            if (button) {
                button.disabled = isLoading;
            }
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
            const periods = normalizeSpecificationTokens(rule.specificationTokens && rule.specificationTokens.length ? rule.specificationTokens : rule.specificationToken);
            return `
                <tr class="hover:bg-slate-50 transition-all">
                    <td class="px-4 py-3 text-sm text-slate-500">${rule.id}</td>
                    <td class="px-4 py-3 text-sm font-medium text-slate-800">${dashboardApp.escapeHtml(getRuleLabel(rule.ruleType))}</td>
                    <td class="px-4 py-3 text-sm text-slate-700" title="${dashboardApp.escapeHtml(periods.join(', '))}">${dashboardApp.escapeHtml(formatSpecificationSummary(periods))}</td>
                    <td class="px-4 py-3 text-sm text-slate-700" title="${dashboardApp.escapeHtml(models.join(', '))}">${dashboardApp.escapeHtml(formatModelSummary(models))}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${rule.requiredQuantity || '-'}</td>
                    <td class="px-4 py-3 text-sm text-slate-700">${rule.priceValue}</td>
                    <td class="px-4 py-3 text-sm text-slate-500">${dashboardApp.formatDateTime(rule.updatedAtUtc)}</td>
                    <td class="px-4 py-3 text-sm whitespace-nowrap">
                        <button class="edit-btn mr-3 text-primary hover:text-blue-800" data-id="${rule.id}">编辑</button>
                        <button class="delete-btn text-rose-600 hover:text-rose-700" data-id="${rule.id}">删除</button>
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
        const summary = `显示 ${start}-${end} / 共 ${state.totalCount} 条规则`;

        elements.pageInfo.textContent = summary;
        elements.mobilePageInfo.textContent = summary;
        elements.mobilePrevBtn.disabled = state.currentPage <= 1;
        elements.mobileNextBtn.disabled = state.currentPage >= totalPages;
        elements.pagination.innerHTML = '';

        if (totalPages <= 1) {
            return;
        }

        const appendButton = (label, page, active, disabled) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = label;
            button.className = `inline-flex items-center rounded-md border px-3 py-2 text-sm ${
                active
                    ? 'border-primary bg-primary text-white'
                    : 'border-slate-300 bg-white text-slate-700 hover:bg-slate-50'
            } ${disabled ? 'cursor-not-allowed opacity-50' : ''}`;

            if (!disabled && !active) {
                button.addEventListener('click', async () => {
                    state.currentPage = page;
                    await loadPriceRules();
                });
            }

            elements.pagination.appendChild(button);
        };

        appendButton('上一页', Math.max(1, state.currentPage - 1), false, state.currentPage <= 1);
        for (let page = 1; page <= totalPages; page += 1) {
            appendButton(String(page), page, page === state.currentPage, false);
        }
        appendButton('下一页', Math.min(totalPages, state.currentPage + 1), false, state.currentPage >= totalPages);
    }

    async function loadPriceRules() {
        setLoading(true);
        try {
            const query = new URLSearchParams({
                pageNumber: String(state.currentPage),
                pageSize: String(state.pageSize),
                sortBy: state.sortBy,
                sortDirection: state.sortDirection
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
            renderSortIndicators();
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
        state.selectedWearPeriods = [];
        state.activeModelWearPeriod = '';
        state.selectedModelTokens = [];
        state.modelSelectionsByPeriodKey = new Map();
        elements.inputId.value = '';
        elements.inputRuleType.value = 'base';
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
        closeWearPeriodDropdown();
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
        state.selectedWearPeriods = normalizeSpecificationTokens(rule.specificationTokens && rule.specificationTokens.length ? rule.specificationTokens : rule.specificationToken);
        state.activeModelWearPeriod = state.selectedWearPeriods[0] || '';
        state.selectedModelTokens = getRuleModels(rule);
        state.modelSelectionsByPeriodKey = new Map();
        elements.inputId.value = String(id);
        elements.inputRuleType.value = normalizeText(rule.ruleType) || 'base';
        rememberSelectedModelsForPeriods(state.selectedWearPeriods, state.selectedModelTokens);
        elements.inputRequiredQuantity.value = String(rule.requiredQuantity || getRuleMeta(rule.ruleType).defaultQuantity);
        elements.inputValue.value = String(rule.priceValue || 0);
        elements.modalTitle.textContent = '编辑价格规则';
        closeModelDropdown();
        refreshFormByRuleType();
        openModal();
    }

    async function onDeletePriceRule(event) {
        const id = Number(event.currentTarget.dataset.id || '0');
        if (id <= 0) {
            return;
        }

        const confirmed = await dashboardApp.showConfirm('确认删除这条价格规则吗？', {
            title: '删除价格规则',
            type: 'error',
            confirmText: '删除'
        });
        if (!confirmed) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/price-rules/${id}`, { method: 'DELETE' });
            if (state.items.length === 1 && state.currentPage > 1) {
                state.currentPage -= 1;
            }
            await loadPriceRules();
            await dashboardApp.showToast('价格规则已删除');
        } catch (error) {
            await dashboardApp.showToast(error.message || '删除价格规则失败', 'error');
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
        state.sortBy = 'updatedAtUtc';
        state.sortDirection = 'desc';
        renderSortIndicators();
        await loadPriceRules();
    }

    function getRequestBody() {
        const ruleType = normalizeText(elements.inputRuleType.value);
        const meta = getRuleMeta(ruleType);
        const specificationTokens = getSelectedWearPeriods();
        const specificationToken = specificationTokens.join('|');
        const modelTokens = meta.requiresModel ? state.selectedModelTokens.slice() : [];
        const requiredQuantity = meta.requiresQuantity
            ? Number(elements.inputRequiredQuantity.value || meta.defaultQuantity)
            : meta.defaultQuantity;
        const priceValue = meta.allowPrice ? Number(elements.inputValue.value || '0') : 0;

        return {
            ruleType,
            specificationToken,
            specificationTokens,
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
        if (body.specificationTokens.length === 0) {
            await dashboardApp.showToast('请选择周期', 'error');
            return;
        }

        const meta = getRuleMeta(body.ruleType);
        if (body.ruleType !== 'clearance' && body.specificationTokens.length !== 1) {
            await dashboardApp.showToast('该规则类型只能选择一个周期', 'error');
            return;
        }

        if (meta.requiresModel && body.modelTokens.length === 0) {
            await dashboardApp.showToast('请至少选择一个型号', 'error');
            return;
        }

        if (meta.requiresQuantity && (!Number.isInteger(body.requiredQuantity) || body.requiredQuantity < 1)) {
            await dashboardApp.showToast('整包数量必须是大于等于 1 的整数', 'error');
            return;
        }

        if (body.ruleType === 'bulk' && body.requiredQuantity < 2) {
            await dashboardApp.showToast('多付活动数量必须大于等于 2', 'error');
            return;
        }

        if (meta.allowPrice && (!Number.isInteger(body.priceValue) || body.priceValue < 0)) {
            await dashboardApp.showToast('价格必须是大于等于 0 的整数', 'error');
            return;
        }

        try {
            if (state.editingId) {
                await dashboardApp.apiRequest(`/api/price-rules/${state.editingId}`, {
                    method: 'PUT',
                    body
                });
            } else {
                await dashboardApp.apiRequest('/api/price-rules', {
                    method: 'POST',
                    body
                });
            }

            closeModal();
            state.sortBy = 'updatedAtUtc';
            state.sortDirection = 'desc';
            state.currentPage = 1;
            await loadPriceRules();
            await dashboardApp.showToast(state.editingId ? '价格规则已更新' : '价格规则已创建');
        } catch (error) {
            await dashboardApp.showToast(error.message || '保存价格规则失败', 'error');
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
            '\u5355\u526f': 'base',
            '\u5355\u526f\u4ef7': 'base',
            '\u57fa\u7840': 'base',
            bulk: 'bulk',
            '\u591a\u4ed8': 'bulk',
            '\u591a\u526f': 'bulk',
            clearance: 'clearance',
            '\u6e05\u4ed3': 'clearance',
            '\u6e05\u4ed3\u89c4\u5219': 'clearance',
            clearancethreshold: 'clearance',
            '\u6e05\u4ed3\u95e8\u69db': 'clearance',
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
            const specificationTokens = normalizeSpecificationTokens(specKey ? row[specKey] : '');
            const specificationToken = specificationTokens.join('|');
            const modelTokens = normalizeModelTokens(modelKey ? row[modelKey] : '');
            const quantityText = normalizeText(quantityKey ? row[quantityKey] : '');
            const priceText = normalizeText(priceKey ? row[priceKey] : '');

            if (!ruleType && !specificationToken && modelTokens.length === 0 && !quantityText && !priceText) {
                return;
            }

            entries.push({
                ruleType,
                specificationToken,
                specificationTokens,
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

        state.sortBy = 'updatedAtUtc';
        state.sortDirection = 'desc';
        state.currentPage = 1;
        await loadPriceRules();
        dashboardApp.hideLoading();
        await dashboardApp.showToast(`导入完成：新增 ${result.createdCount}，更新 ${result.updatedCount}，跳过 ${result.skippedCount}`);
    }

    async function onImportInputChange(event) {
        const file = event.target.files && event.target.files[0];
        event.target.value = '';
        if (!file) {
            return;
        }

        dashboardApp.showLoading('正在导入价格规则，请稍候...');
        try {
            await importPriceRules(file);
        } catch (error) {
            dashboardApp.hideLoading();
            await dashboardApp.showToast(error.message || '导入价格规则失败', 'error');
            return;
        } finally {
            dashboardApp.hideLoading();
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
            state.selectedWearPeriods = getSelectedWearPeriods();
            state.activeModelWearPeriod = '';
            state.selectedModelTokens = [];
            renderModelOptions();
        });
        elements.clearanceModelWearPeriod.addEventListener('change', () => {
            state.activeModelWearPeriod = normalizeText(elements.clearanceModelWearPeriod.value);
            renderClearanceModelWearPeriodOptions();
            renderModelOptions();
        });
        elements.wearPeriodDropdownBtn.addEventListener('click', () => {
            if (!isClearanceRuleSelected()) {
                return;
            }

            state.isWearPeriodDropdownOpen = !state.isWearPeriodDropdownOpen;
            elements.wearPeriodDropdownPanel.classList.toggle('hidden', !state.isWearPeriodDropdownOpen);
            if (state.isWearPeriodDropdownOpen) {
                closeModelDropdown();
            }
        });
        elements.selectAllWearPeriodsBtn.addEventListener('click', () => {
            if (!isClearanceRuleSelected()) {
                return;
            }

            const previousModels = state.selectedModelTokens.slice();
            rememberSelectedModelsForPeriods();
            state.selectedWearPeriods = state.periods.slice();
            restoreSelectedModelsForPeriods(state.selectedWearPeriods, previousModels);
            renderPeriodOptions(state.selectedWearPeriods);
            renderModelOptions();
        });
        elements.clearWearPeriodsBtn.addEventListener('click', () => {
            rememberSelectedModelsForPeriods();
            state.selectedWearPeriods = [];
            state.selectedModelTokens = [];
            renderPeriodOptions(state.selectedWearPeriods);
            renderModelOptions();
        });
        elements.modelDropdownBtn.addEventListener('click', () => {
            if (elements.modelDropdownBtn.disabled) {
                return;
            }

            state.isModelDropdownOpen = !state.isModelDropdownOpen;
            elements.modelDropdownPanel.classList.toggle('hidden', !state.isModelDropdownOpen);
            if (state.isModelDropdownOpen) {
                closeWearPeriodDropdown();
            }
        });
        elements.selectAllModelsBtn.addEventListener('click', () => {
            const models = getAvailableModels().slice();
            if (isClearanceRuleSelected()) {
                mergeSelectedModelsForDisplayedPeriod(models);
                rememberSelectedModelsForPeriods();
            } else {
                state.selectedModelTokens = models;
            }
            renderModelOptions();
        });
        elements.clearModelsBtn.addEventListener('click', () => {
            if (isClearanceRuleSelected()) {
                mergeSelectedModelsForDisplayedPeriod([]);
                rememberSelectedModelsForPeriods();
            } else {
                state.selectedModelTokens = [];
            }
            renderModelOptions();
        });
        document.addEventListener('click', event => {
            if (state.isWearPeriodDropdownOpen && !elements.wearPeriodField.contains(event.target)) {
                closeWearPeriodDropdown();
            }

            if (state.isModelDropdownOpen && !elements.modelField.contains(event.target)) {
                closeModelDropdown();
            }
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
        enhanceSortHeaders();
        bindEvents();

        try {
            await loadCatalogOptions();
            resetForm();
            await loadPriceRules();
        } catch (error) {
            await dashboardApp.showToast(error.message || '加载价格规则失败', 'error');
        }
    });
})();
