(function () {
    const state = { pageNumber: 1, pageSize: 20, totalCount: 0, items: [] };
    const byId = id => document.getElementById(id);
    const fields = ['changedAtStart', 'changedAtEnd', 'receiverName', 'modifierLoginName', 'businessGroupName', 'orderNo'];
    const lockedBusinessGroupName = new URLSearchParams(window.location.search).get('businessGroupName') || '';

    function value(id) { return byId(id).value.trim(); }
    function formatAmount(amount) { return Number(amount || 0).toFixed(2); }
    function formatDifference(amount) { const number = Number(amount || 0); return `${number > 0 ? '+' : ''}${number.toFixed(2)}`; }

    function buildQuery(pageNumber, pageSize) {
        const query = new URLSearchParams({ pageNumber: String(pageNumber), pageSize: String(pageSize) });
        fields.forEach(id => {
            const input = value(id);
            if (input) {
                query.set(id, input);
            }
        });
        return query;
    }

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
        const query = buildQuery(state.pageNumber, state.pageSize);
        const response = await dashboardApp.apiRequest(`/api/order-change-logs?${query.toString()}`);
        state.items = Array.isArray(response.items) ? response.items : [];
        state.totalCount = Number(response.totalCount || 0);
        render();
    }

    async function loadAllFilteredLogs() {
        const pageSize = 200;
        const items = [];
        let pageNumber = 1;
        let totalCount = 0;

        while (true) {
            const query = buildQuery(pageNumber, pageSize);
            const response = await dashboardApp.apiRequest(`/api/order-change-logs?${query.toString()}`);
            const pageItems = Array.isArray(response.items) ? response.items : [];
            totalCount = Number(response.totalCount || 0);
            items.push(...pageItems);

            if (items.length >= totalCount || pageItems.length < pageSize) {
                return items;
            }

            pageNumber++;
        }
    }

    function buildExportFileName() {
        const now = new Date();
        const timestamp = [
            now.getFullYear(),
            String(now.getMonth() + 1).padStart(2, '0'),
            String(now.getDate()).padStart(2, '0')
        ].join('') + '-' + [
            String(now.getHours()).padStart(2, '0'),
            String(now.getMinutes()).padStart(2, '0'),
            String(now.getSeconds()).padStart(2, '0')
        ].join('');
        return `订单修改记录-${timestamp}.xlsx`;
    }

    async function exportFilteredLogs() {
        const exportButton = byId('exportBtn');
        exportButton.disabled = true;
        dashboardApp.showLoading('正在导出筛选后的修改记录，请稍候...');

        try {
            if (typeof XLSX === 'undefined') {
                throw new Error('Excel 导出组件加载失败。');
            }

            const items = await loadAllFilteredLogs();
            const rows = [
                ['修改时间', '订单号', '业务群', '收件人', '修改人账号', '修改前金额', '修改后金额', '差额', '修改内容'],
                ...items.map(item => [
                    dashboardApp.formatDateTime(item.changedAtUtc),
                    item.orderNo || '',
                    item.businessGroupName || '',
                    item.receiverName || '',
                    item.modifierLoginName || '',
                    Number(item.previousAmount || 0),
                    Number(item.currentAmount || 0),
                    Number(item.amountDifference || 0),
                    item.changeSummary || ''
                ])
            ];
            const worksheet = XLSX.utils.aoa_to_sheet(rows);
            worksheet['!cols'] = [
                { wch: 19 }, { wch: 20 }, { wch: 18 }, { wch: 14 }, { wch: 16 },
                { wch: 14 }, { wch: 14 }, { wch: 12 }, { wch: 48 }
            ];
            const workbook = XLSX.utils.book_new();
            XLSX.utils.book_append_sheet(workbook, worksheet, '订单修改记录');
            XLSX.writeFile(workbook, buildExportFileName());
            await dashboardApp.showToast(`已导出 ${items.length} 条修改记录。`);
        } catch (error) {
            await dashboardApp.showToast(error.message || '导出失败。', 'error');
        } finally {
            dashboardApp.hideLoading();
            exportButton.disabled = false;
        }
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) { return; }
        if (lockedBusinessGroupName) {
            const businessGroupInput = byId('businessGroupName');
            businessGroupInput.value = lockedBusinessGroupName;
            businessGroupInput.readOnly = true;
            businessGroupInput.classList.add('cursor-not-allowed', 'bg-gray-100', 'text-gray-700');
        }
        byId('filterBtn').addEventListener('click', () => { state.pageNumber = 1; load().catch(error => dashboardApp.showToast(error.message, 'error')); });
        byId('resetBtn').addEventListener('click', () => { fields.forEach(id => { if (id !== 'businessGroupName' || !lockedBusinessGroupName) { byId(id).value = ''; } }); state.pageNumber = 1; load().catch(error => dashboardApp.showToast(error.message, 'error')); });
        byId('exportBtn').addEventListener('click', () => { exportFilteredLogs(); });
        byId('pagination').addEventListener('click', event => { const page = Number(event.target.dataset.page); if (page > 0) { state.pageNumber = page; load().catch(error => dashboardApp.showToast(error.message, 'error')); } });
        await load();
    });
})();
