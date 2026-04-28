(function () {
    const PRICE_NAME_SEPARATOR = ' / ';
    const KNOWN_WEAR_PERIODS = ['日抛', '周抛', '月抛', '季抛', '半年抛', '年抛'];

    const state = {
        items: [],
        alertKeywords: [],
        catalogOptions: [],
        catalogOptionMap: new Map(),
        catalogModelsByPeriod: new Map(),
        totalCount: 0,
        currentPage: 1,
        pageSize: 20,
        keyword: '',
        editingId: null,
        isLoading: false
    };

    const elements = {
        tableBody: document.getElementById('priceRulesTableBody'),
        addBtn: document.getElementById('addPriceRuleBtn'),
        manageAlertKeywordsBtn: document.getElementById('manageAlertKeywordsBtn'),
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
        inputWearPeriod: document.getElementById('wearPeriod'),
        inputModelName: document.getElementById('modelName'),
        inputPriceNamePreview: document.getElementById('priceNamePreview'),
        inputValue: document.getElementById('priceValue'),
        alertKeywordModal: document.getElementById('alertKeywordModal'),
        closeAlertKeywordModalBtn: document.getElementById('closeAlertKeywordModal'),
        cancelAlertKeywordBtn: document.getElementById('cancelAlertKeywordBtn'),
        alertKeywordForm: document.getElementById('alertKeywordForm'),
        alertKeywordId: document.getElementById('alertKeywordId'),
        alertKeywordInput: document.getElementById('alertKeywordInput'),
        alertKeywordList: document.getElementById('alertKeywordList')
    };

    function normalizeText(value) {
        return String(value ?? '').trim();
    }

    function composePriceName(wearPeriod, modelName) {
        const normalizedWearPeriod = normalizeText(wearPeriod);
        const normalizedModelName = normalizeText(modelName);

        if (!normalizedWearPeriod) {
            return normalizedModelName;
        }

        if (!normalizedModelName) {
            return normalizedWearPeriod;
        }

        return `${normalizedWearPeriod}${PRICE_NAME_SEPARATOR}${normalizedModelName}`;
    }

    function buildCatalogOptionKey(wearPeriod, modelName) {
        return composePriceName(wearPeriod, modelName).toLowerCase();
    }

    function splitPriceName(priceName) {
        const normalized = normalizeText(priceName);
        if (!normalized) {
            return { wearPeriod: '', modelName: '' };
        }

        const slashIndex = normalized.indexOf('/');
        if (slashIndex >= 0) {
            return {
                wearPeriod: normalizeText(normalized.slice(0, slashIndex)),
                modelName: normalizeText(normalized.slice(slashIndex + 1))
            };
        }

        const matchedWearPeriod = KNOWN_WEAR_PERIODS.find(period => normalized.startsWith(period));
        if (matchedWearPeriod) {
            return {
                wearPeriod: matchedWearPeriod,
                modelName: normalizeText(normalized.slice(matchedWearPeriod.length))
            };
        }

        return { wearPeriod: '', modelName: normalized };
    }

    function refreshPriceNamePreview() {
        elements.inputPriceNamePreview.value = composePriceName(
            elements.inputWearPeriod.value,
            elements.inputModelName.value);
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
        if (normalizedSelectedValue && !options.some(option => normalizeText(option.value) === normalizedSelectedValue)) {
            const extraOption = document.createElement('option');
            extraOption.value = normalizedSelectedValue;
            extraOption.textContent = normalizedSelectedValue;
            extraOption.selected = true;
            selectElement.appendChild(extraOption);
        } else {
            selectElement.value = normalizedSelectedValue;
        }
    }

    function rebuildCatalogOptionIndex() {
        state.catalogOptionMap = new Map();
        state.catalogModelsByPeriod = new Map();

        state.catalogOptions.forEach(option => {
            const wearPeriod = normalizeText(option.specificationToken);
            const modelName = normalizeText(option.modelToken);
            if (!wearPeriod || !modelName) {
                return;
            }

            const key = buildCatalogOptionKey(wearPeriod, modelName);
            state.catalogOptionMap.set(key, {
                specificationToken: wearPeriod,
                modelToken: modelName,
                priceName: composePriceName(wearPeriod, modelName)
            });

            if (!state.catalogModelsByPeriod.has(wearPeriod)) {
                state.catalogModelsByPeriod.set(wearPeriod, []);
            }

            const models = state.catalogModelsByPeriod.get(wearPeriod);
            if (!models.includes(modelName)) {
                models.push(modelName);
                models.sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'));
            }
        });
    }

    function renderWearPeriodOptions(selectedWearPeriod = '') {
        const periods = Array.from(state.catalogModelsByPeriod.keys())
            .sort((left, right) => left.localeCompare(right, 'zh-Hans-CN'))
            .map(period => ({ value: period, text: period }));
        setSelectOptions(elements.inputWearPeriod, periods, '请选择周期', selectedWearPeriod);
    }

    function renderModelOptions(wearPeriod, selectedModel = '') {
        const normalizedWearPeriod = normalizeText(wearPeriod);
        const models = normalizedWearPeriod
            ? (state.catalogModelsByPeriod.get(normalizedWearPeriod) || [])
            : [];

        const options = models.map(model => ({ value: model, text: model }));
        const placeholder = normalizedWearPeriod ? '请选择型号' : '请先选择周期';
        setSelectOptions(elements.inputModelName, options, placeholder, selectedModel);
    }

    function setModalSelection(wearPeriod, modelName) {
        renderWearPeriodOptions(wearPeriod);
        renderModelOptions(wearPeriod, modelName);
        refreshPriceNamePreview();
    }

    function getSelectedCatalogOption() {
        const wearPeriod = normalizeText(elements.inputWearPeriod.value);
        const modelName = normalizeText(elements.inputModelName.value);
        const key = buildCatalogOptionKey(wearPeriod, modelName);
        return state.catalogOptionMap.get(key) || null;
    }

    function setLoading(isLoading) {
        state.isLoading = isLoading;
        elements.loadingHint.classList.toggle('hidden', !isLoading);
        elements.searchBtn.disabled = isLoading;
        elements.resetBtn.disabled = isLoading;
        elements.addBtn.disabled = isLoading;
        elements.downloadTemplateBtn.disabled = isLoading;
        elements.importBtn.disabled = isLoading;
        elements.manageAlertKeywordsBtn.disabled = isLoading;
    }

    function openModal() {
        elements.modal.classList.remove('hidden');
        refreshPriceNamePreview();
        window.setTimeout(() => elements.inputWearPeriod.focus(), 0);
    }

    function closeModal() {
        elements.modal.classList.add('hidden');
    }

    function openAlertKeywordModal() {
        elements.alertKeywordModal.classList.remove('hidden');
        window.setTimeout(() => elements.alertKeywordInput.focus(), 0);
    }

    function closeAlertKeywordModal() {
        elements.alertKeywordModal.classList.add('hidden');
        resetAlertKeywordForm();
    }

    function resetForm() {
        state.editingId = null;
        elements.inputId.value = '';
        setModalSelection('', '');
        elements.inputPriceNamePreview.value = '';
        elements.inputValue.value = '0';
        elements.modalTitle.textContent = '新增价格规则';
    }

    function resetAlertKeywordForm() {
        elements.alertKeywordId.value = '';
        elements.alertKeywordInput.value = '';
    }

    function updateSummaryCards() {
        elements.pageCountCard.textContent = String(state.items.length);
        elements.totalCountCard.textContent = String(state.totalCount);
        elements.filterSummaryCard.textContent = state.keyword ? `关键词：${state.keyword}` : '全部规则';
    }

    function renderEmptyTable() {
        elements.tableBody.innerHTML = [
            '<tr>',
            '  <td colspan="5" class="px-6 py-10 text-center">',
            '    <div class="text-slate-400 text-4xl mb-3"><i class="fa fa-inbox"></i></div>',
            '    <div class="text-slate-600 font-medium">暂无价格规则</div>',
            '    <div class="text-sm text-slate-400 mt-1">可以下载模板后导入 Excel，也可以手动新增价格规则。</div>',
            '  </td>',
            '</tr>'
        ].join('');
    }

    function renderTable() {
        if (state.items.length === 0) {
            renderEmptyTable();
            return;
        }

        elements.tableBody.innerHTML = state.items.map(rule => {
            const parsed = splitPriceName(rule.priceName);
            const split = {
                wearPeriod: normalizeText(rule.specificationToken) || parsed.wearPeriod,
                modelName: normalizeText(rule.modelToken) || parsed.modelName
            };
            const detailParts = [];
            if (split.wearPeriod) {
                detailParts.push(`周期：${dashboardApp.escapeHtml(split.wearPeriod)}`);
            }
            if (split.modelName) {
                detailParts.push(`型号：${dashboardApp.escapeHtml(split.modelName)}`);
            }

            return `
                <tr class="hover:bg-slate-50 transition-all">
                    <td class="px-6 py-4 whitespace-nowrap text-sm text-slate-500">${rule.id}</td>
                    <td class="px-6 py-4 text-sm">
                        <div class="font-medium text-slate-900">${dashboardApp.escapeHtml(rule.priceName)}</div>
                        <div class="text-xs text-slate-500 mt-1">${detailParts.join(' ｜ ') || '未拆分'}</div>
                    </td>
                    <td class="px-6 py-4 whitespace-nowrap text-sm text-slate-700">${rule.priceValue}</td>
                    <td class="px-6 py-4 whitespace-nowrap text-sm text-slate-500">${dashboardApp.formatDateTime(rule.updatedAtUtc)}</td>
                    <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                        <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${rule.id}">
                            <i class="fa fa-pencil mr-1"></i>编辑
                        </button>
                        <button class="text-rose-600 hover:text-rose-700 delete-btn" data-id="${rule.id}">
                            <i class="fa fa-trash mr-1"></i>删除
                        </button>
                    </td>
                </tr>
            `;
        }).join('');

        document.querySelectorAll('.edit-btn').forEach(button => {
            button.addEventListener('click', onEdit);
        });
        document.querySelectorAll('.delete-btn').forEach(button => {
            button.addEventListener('click', onDeletePriceRule);
        });
    }

    function renderAlertKeywords() {
        if (state.alertKeywords.length === 0) {
            elements.alertKeywordList.innerHTML = `
                <div class="px-4 py-8 text-center text-sm text-slate-500">
                    暂无特殊价格字符，可先新增“清仓”、“特殊价格”等提醒词。
                </div>
            `;
            return;
        }

        elements.alertKeywordList.innerHTML = state.alertKeywords.map(item => `
            <div class="px-4 py-3 flex items-center justify-between gap-4">
                <div>
                    <div class="font-medium text-slate-800">${dashboardApp.escapeHtml(item.keyword)}</div>
                    <div class="text-xs text-slate-500 mt-1">
                        更新时间：${dashboardApp.formatDateTime(item.updatedAtUtc)}
                    </div>
                </div>
                <div class="flex items-center gap-3 text-sm">
                    <button class="text-primary hover:text-blue-800 alert-edit-btn" data-id="${item.id}">
                        <i class="fa fa-pencil mr-1"></i>编辑
                    </button>
                    <button class="text-rose-600 hover:text-rose-700 alert-delete-btn" data-id="${item.id}">
                        <i class="fa fa-trash mr-1"></i>删除
                    </button>
                </div>
            </div>
        `).join('');

        document.querySelectorAll('.alert-edit-btn').forEach(button => {
            button.addEventListener('click', onEditAlertKeyword);
        });
        document.querySelectorAll('.alert-delete-btn').forEach(button => {
            button.addEventListener('click', onDeleteAlertKeyword);
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
                dots.className = 'relative inline-flex items-center px-3 py-2 border border-slate-300 bg-white text-sm text-slate-400';
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
        const options = await dashboardApp.apiRequest('/api/price-rules/catalog-options');
        state.catalogOptions = options || [];
        rebuildCatalogOptionIndex();
    }

    async function loadAlertKeywords() {
        const items = await dashboardApp.apiRequest('/api/price-alert-keywords');
        state.alertKeywords = (items || []).filter(item => item.isActive !== false);
        renderAlertKeywords();
    }

    function onAdd() {
        resetForm();
        openModal();
    }

    async function onOpenAlertKeywords() {
        resetAlertKeywordForm();
        await loadAlertKeywords();
        openAlertKeywordModal();
    }

    function onEdit(event) {
        const id = Number(event.currentTarget.dataset.id);
        const rule = state.items.find(item => item.id === id);
        if (!rule) {
            return;
        }

        const split = splitPriceName(rule.priceName);
        const wearPeriod = normalizeText(rule.specificationToken) || split.wearPeriod;
        const modelName = normalizeText(rule.modelToken) || split.modelName;
        state.editingId = id;
        elements.inputId.value = String(rule.id);
        setModalSelection(wearPeriod, modelName);
        elements.inputValue.value = String(rule.priceValue);
        elements.modalTitle.textContent = '编辑价格规则';
        openModal();
    }

    async function onDeletePriceRule(event) {
        const id = Number(event.currentTarget.dataset.id);
        const rule = state.items.find(item => item.id === id);
        if (!rule) {
            return;
        }

        if (!window.confirm(`确认删除价格规则“${rule.priceName}”吗？`)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/price-rules/${id}`, {
                method: 'DELETE'
            });
            dashboardApp.showToast('价格规则已删除');
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除价格规则失败', 'error');
        }
    }

    async function onSearch() {
        state.keyword = elements.searchInput.value.trim();
        state.currentPage = 1;
        await loadPriceRules();
    }

    async function onReset() {
        elements.searchInput.value = '';
        state.keyword = '';
        state.currentPage = 1;
        await loadPriceRules();
    }

    async function onSubmit(event) {
        event.preventDefault();

        const selectedOption = getSelectedCatalogOption();
        const priceValue = Number(elements.inputValue.value);

        if (!selectedOption) {
            dashboardApp.showToast('请选择可匹配商品目录的周期和型号', 'error');
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
                    body: {
                        priceName: selectedOption.priceName,
                        specificationToken: selectedOption.specificationToken,
                        modelToken: selectedOption.modelToken,
                        priceValue,
                        isActive: true
                    }
                });
                dashboardApp.showToast('价格规则已更新');
            } else {
                await dashboardApp.apiRequest('/api/price-rules', {
                    method: 'POST',
                    body: {
                        priceName: selectedOption.priceName,
                        specificationToken: selectedOption.specificationToken,
                        modelToken: selectedOption.modelToken,
                        priceValue
                    }
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

    function parsePriceValue(value) {
        const normalized = normalizeText(value);
        if (!normalized) {
            throw new Error('价格不能为空');
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

    function downloadTemplate() {
        const rows = [['周期', '型号', '价格']];
        const workbook = XLSX.utils.book_new();
        const worksheet = XLSX.utils.aoa_to_sheet(rows);
        XLSX.utils.book_append_sheet(workbook, worksheet, '价格规则模板');
        XLSX.writeFile(workbook, '价格规则导入模板.xlsx');
        dashboardApp.showToast('模板已下载到本地');
    }

    function readImportEntries(rows) {
        const entries = [];
        const unmatchedRows = [];

        rows.forEach((row, rowIndex) => {
            const wearPeriodKey = findColumnKey(row, ['周期', 'wearperiod', 'wear']);
            const modelNameKey = findColumnKey(row, ['型号', 'modelname', 'model']);
            const priceKey = findColumnKey(row, ['价格', 'price', 'pricevalue']);
            const legacyPriceNameKey = findColumnKey(row, ['价格名称', 'pricename']);

            let wearPeriod = wearPeriodKey ? normalizeText(row[wearPeriodKey]) : '';
            let modelName = modelNameKey ? normalizeText(row[modelNameKey]) : '';
            const legacyPriceName = legacyPriceNameKey ? normalizeText(row[legacyPriceNameKey]) : '';

            if (!priceKey) {
                throw new Error('Excel 中缺少“价格”列');
            }

            if ((!wearPeriod || !modelName) && legacyPriceName) {
                const split = splitPriceName(legacyPriceName);
                wearPeriod = wearPeriod || split.wearPeriod;
                modelName = modelName || split.modelName;
            }

            if (!wearPeriod && !modelName && !legacyPriceName) {
                return;
            }

            const key = buildCatalogOptionKey(wearPeriod, modelName);
            const option = state.catalogOptionMap.get(key);
            if (!option) {
                unmatchedRows.push(`第 ${rowIndex + 2} 行：${composePriceName(wearPeriod, modelName) || legacyPriceName || '(空)'}`);
                return;
            }

            entries.push({
                priceName: option.priceName,
                specificationToken: option.specificationToken,
                modelToken: option.modelToken,
                priceValue: parsePriceValue(row[priceKey]),
                isActive: true
            });
        });

        if (entries.length === 0) {
            throw new Error('未在 Excel 中识别到可导入的价格规则');
        }

        if (unmatchedRows.length > 0) {
            throw new Error(`有 ${unmatchedRows.length} 条未匹配商品目录，请先修正：${unmatchedRows.slice(0, 5).join('；')}`);
        }

        return entries;
    }

    async function importPriceRules(file) {
        const fileName = file && file.name ? file.name : '导入文件';
        const buffer = await file.arrayBuffer();
        const workbook = XLSX.read(buffer, { type: 'array' });
        const firstSheetName = workbook.SheetNames[0];
        if (!firstSheetName) {
            throw new Error('Excel 文件为空');
        }

        const worksheet = workbook.Sheets[firstSheetName];
        const rows = XLSX.utils.sheet_to_json(worksheet, { defval: '' });
        const entries = readImportEntries(rows);

        const result = await dashboardApp.apiRequest('/api/price-rules/import', {
            method: 'POST',
            body: {
                sourceFileName: fileName,
                entries
            }
        });

        dashboardApp.showToast(`导入完成：新增 ${result.createdCount} 条，更新 ${result.updatedCount} 条，跳过 ${result.skippedCount || 0} 条`);
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

    function onEditAlertKeyword(event) {
        const id = Number(event.currentTarget.dataset.id);
        const item = state.alertKeywords.find(keyword => keyword.id === id);
        if (!item) {
            return;
        }

        elements.alertKeywordId.value = String(item.id);
        elements.alertKeywordInput.value = item.keyword;
        elements.alertKeywordInput.focus();
    }

    async function onDeleteAlertKeyword(event) {
        const id = Number(event.currentTarget.dataset.id);
        const item = state.alertKeywords.find(keyword => keyword.id === id);
        if (!item) {
            return;
        }

        if (!window.confirm(`确认删除特殊价格字符“${item.keyword}”吗？`)) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/price-alert-keywords/${id}`, {
                method: 'DELETE'
            });
            dashboardApp.showToast('特殊价格字符已删除');
            await loadAlertKeywords();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除特殊价格字符失败', 'error');
        }
    }

    async function onSubmitAlertKeyword(event) {
        event.preventDefault();

        const id = Number(elements.alertKeywordId.value || '0');
        const keyword = normalizeText(elements.alertKeywordInput.value);

        if (!keyword) {
            dashboardApp.showToast('请输入特殊价格字符', 'error');
            return;
        }

        try {
            if (id > 0) {
                await dashboardApp.apiRequest(`/api/price-alert-keywords/${id}`, {
                    method: 'PUT',
                    body: { keyword, isActive: true }
                });
                dashboardApp.showToast('特殊价格字符已更新');
            } else {
                await dashboardApp.apiRequest('/api/price-alert-keywords', {
                    method: 'POST',
                    body: { keyword }
                });
                dashboardApp.showToast('特殊价格字符已新增');
            }

            resetAlertKeywordForm();
            await loadAlertKeywords();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存特殊价格字符失败', 'error');
        }
    }

    function bindEvents() {
        elements.addBtn.addEventListener('click', onAdd);
        elements.manageAlertKeywordsBtn.addEventListener('click', onOpenAlertKeywords);
        elements.downloadTemplateBtn.addEventListener('click', downloadTemplate);
        elements.importBtn.addEventListener('click', () => elements.importInput.click());
        elements.importInput.addEventListener('change', onImportInputChange);
        elements.searchBtn.addEventListener('click', onSearch);
        elements.resetBtn.addEventListener('click', onReset);
        elements.closeModalBtn.addEventListener('click', closeModal);
        elements.cancelBtn.addEventListener('click', closeModal);
        elements.form.addEventListener('submit', onSubmit);
        elements.closeAlertKeywordModalBtn.addEventListener('click', closeAlertKeywordModal);
        elements.cancelAlertKeywordBtn.addEventListener('click', closeAlertKeywordModal);
        elements.alertKeywordForm.addEventListener('submit', onSubmitAlertKeyword);
        elements.mobilePrevBtn.addEventListener('click', async () => goToPage(-1));
        elements.mobileNextBtn.addEventListener('click', async () => goToPage(1));
        elements.searchInput.addEventListener('keydown', async event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                await onSearch();
            }
        });
        elements.inputWearPeriod.addEventListener('change', () => {
            renderModelOptions(elements.inputWearPeriod.value, '');
            refreshPriceNamePreview();
        });
        elements.inputModelName.addEventListener('change', refreshPriceNamePreview);
        elements.modal.addEventListener('click', event => {
            if (event.target === elements.modal) {
                closeModal();
            }
        });
        elements.alertKeywordModal.addEventListener('click', event => {
            if (event.target === elements.alertKeywordModal) {
                closeAlertKeywordModal();
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
            await loadCatalogOptions();
            setModalSelection('', '');
            await loadPriceRules();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载价格规则失败', 'error');
        }
    });
})();
