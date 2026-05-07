(function () {
    const state = {
        wearPeriods: [],
        wearPeriodMappings: []
    };

    const elements = {
        currentLoginName: document.getElementById('currentLoginName'),
        saveBtn: document.getElementById('saveBtn'),
        addPeriodBtn: document.getElementById('addPeriodBtn'),
        addAliasBtn: document.getElementById('addAliasBtn'),
        periodTableBody: document.getElementById('periodTableBody'),
        aliasTableBody: document.getElementById('aliasTableBody')
    };

    function normalizeText(value) {
        return String(value || '').trim();
    }

    function createPeriodRow(seed = {}) {
        return {
            value: normalizeText(seed.value),
            sortOrder: Number(seed.sortOrder || 0)
        };
    }

    function createAliasRow(seed = {}) {
        return {
            alias: normalizeText(seed.alias),
            wearPeriod: normalizeText(seed.wearPeriod),
            sortOrder: Number(seed.sortOrder || 0)
        };
    }

    function render() {
        renderPeriods();
        renderAliases();
    }

    function renderPeriods() {
        if (state.wearPeriods.length === 0) {
            elements.periodTableBody.innerHTML = `
                <tr>
                    <td colspan="3" class="px-4 py-8 text-center text-sm text-slate-400">暂无周期，请至少保留一个标准周期。</td>
                </tr>
            `;
            return;
        }

        elements.periodTableBody.innerHTML = state.wearPeriods.map((item, index) => `
            <tr>
                <td class="px-4 py-3 text-sm text-slate-500 whitespace-nowrap">
                    ${index + 1}
                </td>
                <td class="px-4 py-3">
                    <input type="text" class="period-value w-full px-3 py-2 border border-slate-300 rounded-md" data-index="${index}" value="${dashboardApp.escapeHtml(item.value)}" placeholder="例如：半年抛">
                </td>
                <td class="px-4 py-3">
                    <button type="button" class="remove-period bg-red-50 hover:bg-red-100 text-red-700 border border-red-200 px-3 py-2 rounded-md text-sm" data-index="${index}">
                        删除
                    </button>
                </td>
            </tr>
        `).join('');
        elements.periodTableBody.querySelectorAll('.period-value').forEach(input => {
            input.addEventListener('input', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.wearPeriods[index].value = normalizeText(event.currentTarget.value);
                    renderAliases();
                }
            });
        });
        elements.periodTableBody.querySelectorAll('.remove-period').forEach(button => {
            button.addEventListener('click', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.wearPeriods.splice(index, 1);
                    state.wearPeriodMappings = state.wearPeriodMappings.filter(item => item.wearPeriod && state.wearPeriods.some(period => period.value === item.wearPeriod));
                    render();
                }
            });
        });
    }

    function renderAliases() {
        const periodOptions = buildPeriodOptions();

        if (state.wearPeriodMappings.length === 0) {
            elements.aliasTableBody.innerHTML = `
                <tr>
                    <td colspan="4" class="px-4 py-8 text-center text-sm text-slate-400">暂无周期对照，可补充“日抛两片 -> 日抛2片”这类映射。</td>
                </tr>
            `;
            return;
        }

        elements.aliasTableBody.innerHTML = state.wearPeriodMappings.map((item, index) => `
            <tr>
                <td class="px-4 py-3 text-sm text-slate-500 whitespace-nowrap">
                    ${index + 1}
                </td>
                <td class="px-4 py-3">
                    <input type="text" class="alias-value w-full px-3 py-2 border border-slate-300 rounded-md" data-index="${index}" value="${dashboardApp.escapeHtml(item.alias)}" placeholder="例如：日抛两片">
                </td>
                <td class="px-4 py-3">
                    <select class="alias-period w-full px-3 py-2 border border-slate-300 rounded-md" data-index="${index}">
                        <option value="">选择标准周期</option>
                        ${periodOptions.map(option => `<option value="${dashboardApp.escapeHtml(option)}"${option === item.wearPeriod ? ' selected' : ''}>${dashboardApp.escapeHtml(option)}</option>`).join('')}
                    </select>
                </td>
                <td class="px-4 py-3">
                    <button type="button" class="remove-alias bg-red-50 hover:bg-red-100 text-red-700 border border-red-200 px-3 py-2 rounded-md text-sm" data-index="${index}">
                        删除
                    </button>
                </td>
            </tr>
        `).join('');
        elements.aliasTableBody.querySelectorAll('.alias-value').forEach(input => {
            input.addEventListener('input', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.wearPeriodMappings[index].alias = normalizeText(event.currentTarget.value);
                }
            });
        });
        elements.aliasTableBody.querySelectorAll('.alias-period').forEach(select => {
            select.addEventListener('change', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.wearPeriodMappings[index].wearPeriod = normalizeText(event.currentTarget.value);
                }
            });
        });
        elements.aliasTableBody.querySelectorAll('.remove-alias').forEach(button => {
            button.addEventListener('click', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.wearPeriodMappings.splice(index, 1);
                    renderAliases();
                }
            });
        });
    }

    function buildPeriodOptions() {
        return state.wearPeriods
            .map(item => normalizeText(item.value))
            .filter(Boolean);
    }

    async function loadSettings() {
        const response = await dashboardApp.apiRequest('/api/wear-period-settings');
        state.wearPeriods = (response.wearPeriods || []).map(createPeriodRow);
        state.wearPeriodMappings = (response.wearPeriodMappings || []).map(createAliasRow);
        render();
    }

    async function saveSettings() {
        const payload = {
            wearPeriods: state.wearPeriods
                .map((item, index) => createPeriodRow({ value: item.value, sortOrder: index }))
                .filter(item => item.value),
            wearPeriodMappings: state.wearPeriodMappings
                .map((item, index) => createAliasRow({ alias: item.alias, wearPeriod: item.wearPeriod, sortOrder: index }))
                .filter(item => item.alias && item.wearPeriod)
        };

        if (payload.wearPeriods.length === 0) {
            dashboardApp.showToast('请至少保留一个标准周期', 'error');
            return;
        }

        await dashboardApp.apiRequest('/api/wear-period-settings', {
            method: 'PUT',
            body: payload
        });

        dashboardApp.showToast('周期设置已保存');
        await loadSettings();
    }

    function bindEvents() {
        elements.addPeriodBtn.addEventListener('click', () => {
            state.wearPeriods.push(createPeriodRow({ sortOrder: state.wearPeriods.length }));
            renderPeriods();
            renderAliases();
        });
        elements.addAliasBtn.addEventListener('click', () => {
            state.wearPeriodMappings.push(createAliasRow({ sortOrder: state.wearPeriodMappings.length }));
            renderAliases();
        });
        elements.saveBtn.addEventListener('click', async () => {
            try {
                await saveSettings();
            } catch (error) {
                dashboardApp.showToast(error.message || '保存周期设置失败', 'error');
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
            await loadSettings();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载周期设置失败', 'error');
        }
    });
})();
