(function () {
    const state = { pageNumber: 1, pageSize: 20, totalCount: 0, items: [] };
    const byId = id => document.getElementById(id);
    const fields = ['changedAtStart', 'changedAtEnd', 'receiverName', 'modifierLoginName', 'businessGroupName', 'orderNo'];

    function value(id) { return byId(id).value.trim(); }
    function formatAmount(amount) { return Number(amount || 0).toFixed(2); }
    function formatDifference(amount) { const number = Number(amount || 0); return `${number > 0 ? '+' : ''}${number.toFixed(2)}`; }

    function render() {
        const body = byId('logTableBody');
        body.innerHTML = state.items.length === 0
            ? '<tr><td colspan="9" class="px-4 py-8 text-center text-gray-500">暂无订单修改记录</td></tr>'
            : state.items.map(item => {
                const difference = Number(item.amountDifference || 0);
                const differenceClass = difference > 0 ? 'text-green-600' : difference < 0 ? 'text-red-600' : 'text-gray-600';
                return `<tr><td class="px-4 py-3">${dashboardApp.escapeHtml(dashboardApp.formatDateTime(item.changedAtUtc))}</td><td class="px-4 py-3">${dashboardApp.escapeHtml(item.orderNo)}</td><td class="px-4 py-3">${dashboardApp.escapeHtml(item.businessGroupName)}</td><td class="px-4 py-3">${dashboardApp.escapeHtml(item.receiverName)}</td><td class="px-4 py-3">${dashboardApp.escapeHtml(item.modifierLoginName)}</td><td class="px-4 py-3">${formatAmount(item.previousAmount)}</td><td class="px-4 py-3">${formatAmount(item.currentAmount)}</td><td class="px-4 py-3 font-medium ${differenceClass}">${formatDifference(difference)}</td><td class="max-w-md whitespace-normal px-4 py-3 text-gray-600">${dashboardApp.escapeHtml(item.changeSummary || '-')}</td></tr>`;
            }).join('');
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        byId('pageInfo').textContent = `第 ${state.pageNumber} / ${totalPages} 页，共 ${state.totalCount} 条记录`;
        byId('pagination').innerHTML = `<button ${state.pageNumber === 1 ? 'disabled' : ''} data-page="${state.pageNumber - 1}" class="border px-3 py-1 disabled:opacity-40">上一页</button><button ${state.pageNumber >= totalPages ? 'disabled' : ''} data-page="${state.pageNumber + 1}" class="border px-3 py-1 disabled:opacity-40">下一页</button>`;
    }

    async function load() {
        const query = new URLSearchParams({ pageNumber: String(state.pageNumber), pageSize: String(state.pageSize) });
        fields.forEach(id => { const input = value(id); if (input) { query.set(id, input); } });
        const response = await dashboardApp.apiRequest(`/api/order-change-logs?${query.toString()}`);
        state.items = Array.isArray(response.items) ? response.items : [];
        state.totalCount = Number(response.totalCount || 0);
        render();
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) { return; }
        byId('filterBtn').addEventListener('click', () => { state.pageNumber = 1; load().catch(error => dashboardApp.showToast(error.message, 'error')); });
        byId('resetBtn').addEventListener('click', () => { fields.forEach(id => { byId(id).value = ''; }); state.pageNumber = 1; load().catch(error => dashboardApp.showToast(error.message, 'error')); });
        byId('pagination').addEventListener('click', event => { const page = Number(event.target.dataset.page); if (page > 0) { state.pageNumber = page; load().catch(error => dashboardApp.showToast(error.message, 'error')); } });
        await load();
    });
})();
