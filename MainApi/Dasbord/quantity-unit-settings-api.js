(function () {
    const state = {
        items: []
    };

    const elements = {
        currentLoginName: document.getElementById('currentLoginName'),
        addBtn: document.getElementById('addBtn'),
        saveBtn: document.getElementById('saveBtn'),
        tableBody: document.getElementById('tableBody')
    };

    function normalizeText(value) {
        return String(value || '').trim();
    }

    function createRule(seed = {}) {
        return {
            unit: normalizeText(seed.unit),
            actualQuantity: Number(seed.actualQuantity || 0)
        };
    }

    function render() {
        if (state.items.length === 0) {
            elements.tableBody.innerHTML = `
                <tr>
                    <td colspan="4" class="px-4 py-8 text-center text-sm text-slate-400">暂无量词规则，未命中时会继续使用历史数量逻辑。</td>
                </tr>
            `;
            return;
        }

        elements.tableBody.innerHTML = state.items.map((item, index) => `
            <tr>
                <td class="px-4 py-3 text-sm text-slate-500 whitespace-nowrap">${index + 1}</td>
                <td class="px-4 py-3">
                    <input type="text" class="unit-value w-full px-3 py-2 border border-slate-300 rounded-md focus:ring-2 focus:ring-primary focus:border-transparent" data-index="${index}" value="${dashboardApp.escapeHtml(item.unit)}" placeholder="例如：副">
                </td>
                <td class="px-4 py-3">
                    <input type="number" min="1" step="1" class="actual-quantity w-full px-3 py-2 border border-slate-300 rounded-md focus:ring-2 focus:ring-primary focus:border-transparent" data-index="${index}" value="${item.actualQuantity || ''}" placeholder="例如：2">
                </td>
                <td class="px-4 py-3">
                    <button type="button" class="remove-rule bg-red-50 hover:bg-red-100 text-red-700 border border-red-200 px-3 py-2 rounded-md text-sm" data-index="${index}">
                        删除
                    </button>
                </td>
            </tr>
        `).join('');

        elements.tableBody.querySelectorAll('.unit-value').forEach(input => {
            input.addEventListener('input', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.items[index].unit = normalizeText(event.currentTarget.value);
                }
            });
        });
        elements.tableBody.querySelectorAll('.actual-quantity').forEach(input => {
            input.addEventListener('input', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.items[index].actualQuantity = Number(event.currentTarget.value || 0);
                }
            });
        });
        elements.tableBody.querySelectorAll('.remove-rule').forEach(button => {
            button.addEventListener('click', event => {
                const index = Number(event.currentTarget.dataset.index || '-1');
                if (index >= 0) {
                    state.items.splice(index, 1);
                    render();
                }
            });
        });
    }

    async function loadSettings() {
        const response = await dashboardApp.apiRequest('/api/quantity-unit-settings');
        state.items = (response.items || []).map(createRule);
        render();
    }

    async function saveSettings() {
        const items = state.items
            .map(createRule)
            .filter(item => item.unit || item.actualQuantity);

        if (items.some(item => !item.unit || !Number.isInteger(item.actualQuantity) || item.actualQuantity <= 0)) {
            dashboardApp.showToast('请填写量词和大于 0 的整数实际数量。', 'error');
            return;
        }

        const duplicateUnits = new Set();
        for (const item of items) {
            const key = item.unit.toLocaleLowerCase();
            if (duplicateUnits.has(key)) {
                dashboardApp.showToast(`量词“${item.unit}”重复，请保留一条。`, 'error');
                return;
            }

            duplicateUnits.add(key);
        }

        await dashboardApp.apiRequest('/api/quantity-unit-settings', {
            method: 'PUT',
            body: { items }
        });

        dashboardApp.showToast('量词设置已保存');
        await loadSettings();
    }

    function bindEvents() {
        elements.addBtn.addEventListener('click', () => {
            state.items.push(createRule({ actualQuantity: 1 }));
            render();
        });
        elements.saveBtn.addEventListener('click', async () => {
            try {
                await saveSettings();
            } catch (error) {
                dashboardApp.showToast(error.message || '保存量词设置失败', 'error');
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
            dashboardApp.showToast(error.message || '加载量词设置失败', 'error');
        }
    });
})();
