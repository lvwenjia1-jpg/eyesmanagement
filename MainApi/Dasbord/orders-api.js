(function () {
    const PREVIEW_ITEM_COUNT = 5;
    const DEFAULT_SORT_BY = 'createdAtUtc';
    const DEFAULT_SORT_DIRECTION = 'desc';

    const SORTABLE_COLUMNS = [
        { index: 0, key: 'orderNo', label: '订单ID' },
        { index: 1, key: 'uploaderLoginName', label: '上传人账号' },
        { index: 2, key: 'receiverName', label: '收件人' },
        { index: 5, key: 'status', label: '订单状态' },
        { index: 6, key: 'amount', label: '订单金额' },
        { index: 7, key: 'trackingNumber', label: '快递单号' }
    ];

    const state = {
        selectedGroupId: 0,
        selectedGroupName: '',
        selectedGroupBalance: 0,
        orders: [],
        totalCount: 0,
        currentPage: 1,
        pageSize: 10,
        sortBy: DEFAULT_SORT_BY,
        sortDirection: DEFAULT_SORT_DIRECTION,
        isSyncingTrackingNumbers: false
    };

    const elements = {
        groupTitle: document.getElementById('groupTitle'),
        currentDateEl: document.getElementById('currentDate'),
        ordersTableBody: document.getElementById('ordersTableBody'),
        backBtn: document.getElementById('backBtn'),
        startTimeInput: document.getElementById('startTime'),
        endTimeInput: document.getElementById('endTime'),
        hasTrackingNumberOnly: document.getElementById('hasTrackingNumberOnly'),
        filterBtn: document.getElementById('filterBtn'),
        resetBtn: document.getElementById('resetBtn'),
        syncTrackingBtn: document.getElementById('syncTrackingBtn'),
        exportBtn: document.getElementById('exportBtn'),
        orderModal: document.getElementById('orderModal'),
        closeOrderModalBtn: document.getElementById('closeOrderModal'),
        cancelOrderBtn: document.getElementById('cancelOrderBtn'),
        orderForm: document.getElementById('orderForm'),
        productsContainer: document.getElementById('productsContainer'),
        logoutBtn: document.getElementById('logoutBtn'),
        pageInfo: document.getElementById('pageInfo'),
        paginationContainer: document.getElementById('pagination'),
        mobilePrevBtn: document.getElementById('mobilePrevBtn'),
        mobileNextBtn: document.getElementById('mobileNextBtn'),
        editProductsHint: document.getElementById('editProductsHint'),
        productsDetailModal: document.getElementById('productsDetailModal'),
        productsDetailTitle: document.getElementById('productsDetailTitle'),
        productsDetailContainer: document.getElementById('productsDetailContainer'),
        closeProductsDetailModal: document.getElementById('closeProductsDetailModal'),
        closeProductsDetailFooterBtn: document.getElementById('closeProductsDetailFooterBtn')
    };

    function setCurrentDate() {
        elements.currentDateEl.textContent = new Date().toLocaleDateString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit'
        });
    }

    function setDefaultFilterTimeRange() {
        const now = new Date();
        const year = now.getFullYear();
        const month = String(now.getMonth() + 1).padStart(2, '0');
        const day = String(now.getDate()).padStart(2, '0');
        const defaultDateTime = `${year}-${month}-${day}T00:00`;
        elements.startTimeInput.value = defaultDateTime;
        elements.endTimeInput.value = defaultDateTime;
        if (elements.hasTrackingNumberOnly) {
            elements.hasTrackingNumberOnly.checked = false;
        }
    }

    function parseDateTimeLocalToIso(value) {
        if (!value) {
            return '';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return '';
        }

        return date.toISOString();
    }

    function formatCurrency(value) {
        const amount = Number(value || 0);
        return `¥${amount.toFixed(2)}`;
    }

    function getPricingDisplayText(priceName) {
        return String(priceName || '').trim();
    }

    function getRecognizedUnitPrice(item) {
        const unitPrice = Number(item && item.unitPrice);
        return Number.isFinite(unitPrice) && unitPrice > 0 ? unitPrice : 0;
    }

    function isClearancePrice(priceName) {
        const text = getPricingDisplayText(priceName);
        return text.startsWith('清仓门槛') || text.startsWith('清仓 /');
    }

    function isExtraChargePrice(priceName) {
        const text = getPricingDisplayText(priceName);
        return text.startsWith('多付 /');
    }

    function getPricingBadgeClass(priceName) {
        if (isClearancePrice(priceName)) {
            return 'border border-rose-200 bg-rose-50 text-rose-700';
        }

        if (isExtraChargePrice(priceName)) {
            return 'border border-amber-200 bg-amber-50 text-amber-700';
        }

        return 'border border-slate-200 bg-white text-slate-500';
    }

    function getItemCardClass(priceName) {
        if (isClearancePrice(priceName)) {
            return 'border-rose-200 bg-rose-50/70';
        }

        if (isExtraChargePrice(priceName)) {
            return 'border-amber-200 bg-amber-50/70';
        }

        return 'border-slate-200 bg-white';
    }

    function getOrderStatusBadge(order) {
        if (order && order.isCancelled) {
            return '<span class="inline-flex items-center rounded-md bg-red-50 px-2 py-0.5 font-medium text-red-700">订单已取消</span>';
        }

        const statusText = String((order && order.status) || '').trim();
        if (statusText) {
            return `<span class="inline-flex items-center rounded-md bg-slate-100 px-2 py-0.5 font-medium text-slate-700">${dashboardApp.escapeHtml(statusText)}</span>`;
        }

        return '<span class="inline-flex items-center rounded-md bg-emerald-50 px-2 py-0.5 font-medium text-emerald-700">正常</span>';
    }

    function getSortIndicator(sortKey) {
        if (state.sortBy !== sortKey) {
            return '↕';
        }

        return state.sortDirection === 'asc' ? '↑' : '↓';
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

    function enhanceSortHeaders() {
        const headerCells = elements.ordersTableBody?.closest('table')?.querySelectorAll('thead th');
        if (!headerCells || headerCells.length === 0) {
            return;
        }

        SORTABLE_COLUMNS.forEach(column => {
            const cell = headerCells[column.index];
            if (!cell || cell.dataset.sortEnhanced === 'true') {
                return;
            }

            cell.dataset.sortEnhanced = 'true';
            cell.innerHTML = `
                <button type="button" class="order-sort-btn inline-flex items-center gap-1 text-left text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700" data-sort-by="${column.key}">
                    <span>${column.label}</span>
                    <span class="sort-indicator text-slate-400" data-sort-indicator="${column.key}">${getSortIndicator(column.key)}</span>
                </button>
            `;
        });

        document.querySelectorAll('.order-sort-btn').forEach(button => {
            button.addEventListener('click', async () => {
                const nextSortBy = button.dataset.sortBy || DEFAULT_SORT_BY;
                if (state.sortBy === nextSortBy) {
                    state.sortDirection = state.sortDirection === 'asc' ? 'desc' : 'asc';
                } else {
                    state.sortBy = nextSortBy;
                    state.sortDirection = nextSortBy === DEFAULT_SORT_BY ? 'desc' : 'asc';
                }

                state.currentPage = 1;
                renderSortIndicators();
                await loadOrders();
            });
        });

        renderSortIndicators();
    }

    function renderOrderItemsHtml(items) {
        if (!items || items.length === 0) {
            return '<div class="rounded-lg border border-dashed border-slate-300 bg-white px-4 py-6 text-sm text-slate-500">暂无商品信息</div>';
        }

        return items.map(item => {
            const pricingText = getPricingDisplayText(item.priceName);
            const unitPrice = getRecognizedUnitPrice(item);
            const quantity = Number(item.quantity || 0);

            return `
                <div class="rounded-xl border p-4 shadow-sm ${getItemCardClass(item.priceName)}">
                    <div class="flex items-start justify-between gap-4">
                        <div class="min-w-0 flex-1">
                            <div class="text-sm font-semibold text-slate-900">${dashboardApp.escapeHtml(item.productName || '-')}</div>
                            <div class="mt-1 text-xs text-slate-500">编码：${dashboardApp.escapeHtml(item.productCode || '-')}</div>
                            ${unitPrice > 0 && pricingText ? `<div class="mt-2 inline-flex rounded-md px-2 py-1 text-xs font-medium ${getPricingBadgeClass(item.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>` : ''}
                            ${unitPrice > 0 ? `<div class="mt-2 text-xs font-medium text-emerald-700">识别价格：${unitPrice} 元</div>` : ''}
                        </div>
                        <div class="rounded-lg bg-white/80 px-3 py-1 text-sm font-semibold text-slate-700">x ${quantity}</div>
                    </div>
                </div>
            `;
        }).join('');
    }

    function buildProductsPreview(order) {
        const items = Array.isArray(order.items) ? order.items : [];
        if (items.length === 0) {
            return '<div class="text-sm text-slate-500">暂无商品信息</div>';
        }

        const previewItems = items.slice(0, PREVIEW_ITEM_COUNT);
        const cards = previewItems.map(item => {
            const pricingText = getPricingDisplayText(item.priceName);
            const unitPrice = getRecognizedUnitPrice(item);
            return `
                <div class="rounded-lg border px-3 py-2 ${getItemCardClass(item.priceName)}">
                    <div class="text-sm font-medium text-slate-800">${dashboardApp.escapeHtml(item.productName || '-')}</div>
                    <div class="mt-1 text-xs text-slate-500">${dashboardApp.escapeHtml(item.productCode || '-')} · x ${Number(item.quantity || 0)}</div>
                    ${unitPrice > 0 && pricingText ? `<div class="mt-2 inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${getPricingBadgeClass(item.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>` : ''}
                </div>
            `;
        }).join('');

        const moreButton = items.length > PREVIEW_ITEM_COUNT
            ? `
                <button type="button" class="view-products-detail inline-flex items-center rounded-md bg-slate-100 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-200" data-id="${order.id}">
                    共 ${items.length} 条，点击查看全部
                </button>
            `
            : '';

        return `
            <div class="space-y-2">
                ${cards}
                ${moreButton}
            </div>
        `;
    }

    function openProductsDetailModal(order) {
        const items = Array.isArray(order.items) ? order.items : [];
        elements.productsDetailTitle.textContent = `商品详情 · ${order.orderNo}`;
        elements.productsDetailContainer.innerHTML = `
            <div class="mb-4 rounded-lg border border-slate-200 bg-white px-4 py-3 text-sm text-slate-600">
                共 ${items.length} 条商品，颜色逻辑与订单列表保持一致。
            </div>
            <div class="grid gap-3">${renderOrderItemsHtml(items)}</div>
        `;
        elements.productsDetailModal.classList.remove('hidden');
        elements.productsDetailModal.classList.add('flex');
    }

    function closeProductsDetailModal() {
        elements.productsDetailModal.classList.add('hidden');
        elements.productsDetailModal.classList.remove('flex');
    }

    function openOrderModal(order) {
        const items = Array.isArray(order.items) ? order.items : [];
        document.getElementById('orderId').value = String(order.id);
        document.getElementById('editOrderId').value = order.orderNo || '-';
        document.getElementById('editUploader').value = order.uploaderLoginName || '-';
        document.getElementById('editRecipient').value = order.receiverName || '-';
        document.getElementById('editAddress').value = order.receiverAddress || '-';
        document.getElementById('editAmount').value = String(order.amount ?? 0);
        document.getElementById('editTrackingNumber').value = order.trackingNumber || '';

        elements.productsContainer.innerHTML = `<div class="grid gap-3 md:grid-cols-2">${renderOrderItemsHtml(items)}</div>`;
        elements.editProductsHint.textContent = items.length > PREVIEW_ITEM_COUNT
            ? `当前共 ${items.length} 条商品，可在此滚动查看全部`
            : `当前共 ${items.length} 条商品`;

        elements.orderModal.classList.remove('hidden');
        elements.orderModal.classList.add('flex');
    }

    function closeOrderModal() {
        elements.orderModal.classList.add('hidden');
        elements.orderModal.classList.remove('flex');
    }

    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
        const start = state.totalCount === 0 ? 0 : ((state.currentPage - 1) * state.pageSize) + 1;
        const end = Math.min(state.currentPage * state.pageSize, state.totalCount);

        elements.pageInfo.innerHTML = `
            <p class="text-sm text-gray-700">
                显示 <span class="font-medium">${start}</span> 到 <span class="font-medium">${end}</span> 条，共 <span class="font-medium">${state.totalCount}</span> 条记录
            </p>
        `;

        elements.mobilePrevBtn.disabled = state.currentPage <= 1;
        elements.mobileNextBtn.disabled = state.currentPage >= totalPages;
        elements.mobilePrevBtn.classList.toggle('opacity-50', elements.mobilePrevBtn.disabled);
        elements.mobileNextBtn.classList.toggle('opacity-50', elements.mobileNextBtn.disabled);

        if (totalPages <= 1) {
            elements.paginationContainer.innerHTML = '';
            return;
        }

        const pageButtons = [];
        for (let page = 1; page <= totalPages; page += 1) {
            const activeClass = page === state.currentPage
                ? 'bg-primary text-white border-primary'
                : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50';

            pageButtons.push(`
                <button type="button" class="page-btn relative inline-flex items-center border px-4 py-2 text-sm font-medium ${activeClass}" data-page="${page}">
                    ${page}
                </button>
            `);
        }

        elements.paginationContainer.innerHTML = `
            <nav class="isolate inline-flex -space-x-px rounded-md shadow-sm" aria-label="Pagination">
                <button type="button" class="page-btn relative inline-flex items-center rounded-l-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-500 hover:bg-gray-50 ${state.currentPage <= 1 ? 'pointer-events-none opacity-50' : ''}" data-page="${state.currentPage - 1}">
                    <i class="fa fa-chevron-left"></i>
                </button>
                ${pageButtons.join('')}
                <button type="button" class="page-btn relative inline-flex items-center rounded-r-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-500 hover:bg-gray-50 ${state.currentPage >= totalPages ? 'pointer-events-none opacity-50' : ''}" data-page="${state.currentPage + 1}">
                    <i class="fa fa-chevron-right"></i>
                </button>
            </nav>
        `;

        elements.paginationContainer.querySelectorAll('.page-btn').forEach(button => {
            button.addEventListener('click', async () => {
                const page = Number(button.dataset.page || state.currentPage);
                if (!Number.isFinite(page) || page < 1 || page > totalPages || page === state.currentPage) {
                    return;
                }

                state.currentPage = page;
                await loadOrders();
            });
        });
    }

    function renderOrders() {
        elements.ordersTableBody.innerHTML = '';

        if (state.orders.length === 0) {
            elements.ordersTableBody.innerHTML = '<tr><td colspan="9" class="px-6 py-4 text-center text-gray-500">暂无订单数据</td></tr>';
            renderPagination();
            return;
        }

        state.orders.forEach(order => {
            const row = document.createElement('tr');
            row.className = 'hover:bg-gray-50 transition-all';
            row.innerHTML = `
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(order.orderNo || '-')}</div>
                    <div class="text-xs text-gray-400">${dashboardApp.formatDateTime(order.createdAtUtc)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.uploaderLoginName || '-')}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.receiverName || '-')}</div>
                </td>
                <td class="px-6 py-4">
                    <div class="max-w-xs text-sm leading-6 text-gray-500">${dashboardApp.escapeHtml(order.receiverAddress || '-')}</div>
                </td>
                <td class="px-6 py-4">
                    <div class="max-w-sm">${buildProductsPreview(order)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    ${getOrderStatusBadge(order)}
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">${formatCurrency(order.amount)}</div>
                    <button type="button" class="edit-amount mt-1 text-xs text-primary hover:text-blue-800" data-id="${order.id}">
                        <i class="fa fa-pencil"></i> 修改
                    </button>
                </td>
                <td class="px-6 py-4">
                    <div class="max-w-[11rem] break-all text-sm text-gray-500">${dashboardApp.escapeHtml(order.trackingNumber || '-')}</div>
                    <button type="button" class="edit-tracking mt-1 text-xs text-primary hover:text-blue-800" data-id="${order.id}">
                        <i class="fa fa-pencil"></i> 填写
                    </button>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <button type="button" class="edit-order mr-3 text-primary hover:text-blue-800" data-id="${order.id}">
                        <i class="fa fa-edit"></i> 编辑
                    </button>
                    <button type="button" class="delete-order text-red-600 hover:text-red-800" data-id="${order.id}">
                        <i class="fa fa-trash"></i> 删除
                    </button>
                </td>
            `;
            elements.ordersTableBody.appendChild(row);
        });

        elements.ordersTableBody.querySelectorAll('.edit-order').forEach(button => {
            button.addEventListener('click', event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = state.orders.find(item => item.id === orderId);
                if (order) {
                    openOrderModal(order);
                }
            });
        });

        elements.ordersTableBody.querySelectorAll('.view-products-detail').forEach(button => {
            button.addEventListener('click', event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = state.orders.find(item => item.id === orderId);
                if (order) {
                    openProductsDetailModal(order);
                }
            });
        });

        elements.ordersTableBody.querySelectorAll('.edit-amount').forEach(button => {
            button.addEventListener('click', async event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = state.orders.find(item => item.id === orderId);
                if (!order) {
                    return;
                }

                const value = await dashboardApp.showPrompt('请输入新的订单金额。', String(order.amount ?? 0), {
                    title: '修改订单金额',
                    confirmText: '保存'
                });

                if (value === null) {
                    return;
                }

                const amount = Number(value);
                if (!Number.isFinite(amount) || amount < 0) {
                    await dashboardApp.showToast('请输入有效的金额。', 'error');
                    return;
                }

                await updateOrder(order.id, amount, order.receiverAddress || '', order.trackingNumber || '');
            });
        });

        elements.ordersTableBody.querySelectorAll('.edit-tracking').forEach(button => {
            button.addEventListener('click', async event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = state.orders.find(item => item.id === orderId);
                if (!order) {
                    return;
                }

                const value = await dashboardApp.showPrompt('请输入快递单号。', order.trackingNumber || '', {
                    title: '填写快递单号',
                    confirmText: '保存'
                });

                if (value === null) {
                    return;
                }

                await updateOrder(order.id, Number(order.amount || 0), order.receiverAddress || '', String(value).trim());
            });
        });

        elements.ordersTableBody.querySelectorAll('.delete-order').forEach(button => {
            button.addEventListener('click', async event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = state.orders.find(item => item.id === orderId);
                if (!order) {
                    return;
                }

                const confirmed = await dashboardApp.showConfirm(`确认删除订单“${order.orderNo}”吗？`, {
                    title: '删除订单',
                    type: 'error',
                    confirmText: '删除'
                });

                if (!confirmed) {
                    return;
                }

                try {
                    await dashboardApp.apiRequest(`/api/orders/${order.id}`, { method: 'DELETE' });
                    await dashboardApp.showToast('订单已删除。');
                    const maxPage = Math.max(1, Math.ceil(Math.max(0, state.totalCount - 1) / state.pageSize));
                    state.currentPage = Math.min(state.currentPage, maxPage);
                    await loadOrders();
                } catch (error) {
                    await dashboardApp.showToast(error.message || '删除订单失败。', 'error');
                }
            });
        });

        renderPagination();
    }

    async function loadGroupInfo() {
        const filter = dashboardApp.getOrderFilter();
        if (!filter || !Number(filter.businessGroupId)) {
            window.location.href = 'business.html';
            return false;
        }

        state.selectedGroupId = Number(filter.businessGroupId);
        state.selectedGroupName = filter.businessGroupName || '订单详情';
        elements.groupTitle.textContent = `${state.selectedGroupName} · 订单详情`;

        try {
            const group = await dashboardApp.apiRequest(`/api/business-groups/${state.selectedGroupId}`);
            state.selectedGroupBalance = Number(group?.balance || 0);
        } catch {
            state.selectedGroupBalance = 0;
        }

        return true;
    }

    async function loadOrders() {
        if (!state.selectedGroupId) {
            return;
        }

        const query = new URLSearchParams({
            pageNumber: String(state.currentPage),
            pageSize: String(state.pageSize),
            sortBy: state.sortBy,
            sortDirection: state.sortDirection
        });

        const startTime = parseDateTimeLocalToIso(elements.startTimeInput.value);
        const endTime = parseDateTimeLocalToIso(elements.endTimeInput.value);
        if (startTime) {
            query.set('startTime', startTime);
        }

        if (endTime) {
            query.set('endTime', endTime);
        }

        if (elements.hasTrackingNumberOnly?.checked) {
            query.set('hasTrackingNumber', 'true');
        }

        const response = await dashboardApp.apiRequest(`/api/business-groups/${state.selectedGroupId}/orders?${query.toString()}`);
        state.orders = Array.isArray(response.items) ? response.items : [];
        state.totalCount = Number(response.totalCount || 0);
        renderOrders();
        renderSortIndicators();
    }

    async function updateOrder(orderId, amount, receiverAddress, trackingNumber) {
        try {
            await dashboardApp.apiRequest(`/api/orders/${orderId}`, {
                method: 'PUT',
                body: {
                    amount,
                    receiverAddress,
                    trackingNumber
                }
            });

            const editingOrderId = Number(document.getElementById('orderId').value || 0);
            if (editingOrderId === orderId && !elements.orderModal.classList.contains('hidden')) {
                closeOrderModal();
            }

            await dashboardApp.showToast('订单已更新。');
            await loadOrders();
        } catch (error) {
            await dashboardApp.showToast(error.message || '更新订单失败。', 'error');
        }
    }

    async function handleOrderSubmit(event) {
        event.preventDefault();

        const orderId = Number(document.getElementById('orderId').value);
        const amount = Number(document.getElementById('editAmount').value);
        const receiverAddress = document.getElementById('editAddress').value.trim();
        const trackingNumber = document.getElementById('editTrackingNumber').value.trim();

        if (!Number.isFinite(amount) || amount < 0) {
            await dashboardApp.showToast('请输入有效的订单金额。', 'error');
            return;
        }

        if (!receiverAddress) {
            await dashboardApp.showToast('收货地址不能为空。', 'error');
            return;
        }

        await updateOrder(orderId, amount, receiverAddress, trackingNumber);
    }

    async function handleFilter() {
        state.currentPage = 1;
        await loadOrders();
    }

    async function handleReset() {
        setDefaultFilterTimeRange();
        state.currentPage = 1;
        state.sortBy = DEFAULT_SORT_BY;
        state.sortDirection = DEFAULT_SORT_DIRECTION;
        renderSortIndicators();
        await loadOrders();
    }

    function setSyncTrackingButtonState(isSyncing) {
        if (!elements.syncTrackingBtn) {
            return;
        }

        elements.syncTrackingBtn.disabled = isSyncing;
        elements.syncTrackingBtn.classList.toggle('opacity-60', isSyncing);
        elements.syncTrackingBtn.classList.toggle('cursor-not-allowed', isSyncing);
    }

    async function handleSyncTrackingNumbers(options = {}) {
        if (!state.selectedGroupId || state.isSyncingTrackingNumbers) {
            return false;
        }

        const showResultToast = options.showResultToast !== false;
        const showLoadingOverlay = options.showLoadingOverlay !== false;
        const startTime = parseDateTimeLocalToIso(elements.startTimeInput.value);
        const endTime = parseDateTimeLocalToIso(elements.endTimeInput.value);
        let syncResult = null;
        let syncError = null;

        state.isSyncingTrackingNumbers = true;
        setSyncTrackingButtonState(true);

        try {
            if (showLoadingOverlay) {
                dashboardApp.showLoading('正在同步快递单号，请稍候...');
            }

            syncResult = await dashboardApp.apiRequest(`/api/business-groups/${state.selectedGroupId}/orders/sync-tracking-numbers`, {
                method: 'POST',
                body: {
                    startTime: startTime || null,
                    endTime: endTime || null
                }
            });
        } catch (error) {
            syncError = error;
        } finally {
            if (showLoadingOverlay) {
                dashboardApp.hideLoading();
            }

            state.isSyncingTrackingNumbers = false;
            setSyncTrackingButtonState(false);
        }

        if (syncError) {
            await dashboardApp.showToast(syncError.message || '同步快递单号失败。', 'error');
            return false;
        }

        try {
            state.currentPage = 1;
            await loadOrders();
        } catch (error) {
            await dashboardApp.showToast(error.message || '刷新订单失败。', 'error');
            return false;
        }

        if (showResultToast && syncResult) {
            await dashboardApp.showToast(`同步完成，共检查 ${Number(syncResult.totalCount || 0)} 条，更新 ${Number(syncResult.updatedCount || 0)} 条。`);
        }

        return true;
    }

    async function handleExport() {
        if (!state.selectedGroupId) {
            return;
        }

        const query = new URLSearchParams({
            businessGroupId: String(state.selectedGroupId)
        });

        const startTime = parseDateTimeLocalToIso(elements.startTimeInput.value);
        const endTime = parseDateTimeLocalToIso(elements.endTimeInput.value);
        if (startTime) {
            query.set('startTime', startTime);
        }

        if (endTime) {
            query.set('endTime', endTime);
        }

        if (elements.hasTrackingNumberOnly?.checked) {
            query.set('hasTrackingNumber', 'true');
        }

        try {
            const response = await fetch(`${dashboardApp.getApiBaseUrl()}/api/exports/orders?${query.toString()}`, {
                headers: {
                    Authorization: `Bearer ${dashboardApp.getToken()}`
                }
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || '导出失败。');
            }

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            const contentDisposition = response.headers.get('content-disposition') || '';
            const matchedFileName = contentDisposition.match(/filename\*=UTF-8''([^;]+)|filename=\"?([^\";]+)\"?/i);
            const serverFileName = decodeURIComponent((matchedFileName && (matchedFileName[1] || matchedFileName[2])) || '').trim();
            link.download = serverFileName || `${state.selectedGroupName || '订单'}-${Date.now()}.csv`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        } catch (error) {
            await dashboardApp.showToast(error.message || '导出失败。', 'error');
        }
    }

    function bindEvents() {
        elements.backBtn.addEventListener('click', () => {
            window.location.href = 'business.html';
        });

        elements.filterBtn.addEventListener('click', () => {
            handleFilter();
        });

        elements.resetBtn.addEventListener('click', () => {
            handleReset();
        });

        elements.syncTrackingBtn?.addEventListener('click', () => {
            handleSyncTrackingNumbers();
        });

        elements.exportBtn.addEventListener('click', () => {
            handleExport();
        });

        elements.closeOrderModalBtn.addEventListener('click', closeOrderModal);
        elements.cancelOrderBtn.addEventListener('click', closeOrderModal);
        elements.orderForm.addEventListener('submit', handleOrderSubmit);
        elements.logoutBtn.addEventListener('click', () => dashboardApp.logout());

        elements.orderModal.addEventListener('click', event => {
            if (event.target === elements.orderModal) {
                closeOrderModal();
            }
        });

        elements.productsDetailModal.addEventListener('click', event => {
            if (event.target === elements.productsDetailModal) {
                closeProductsDetailModal();
            }
        });

        elements.closeProductsDetailModal.addEventListener('click', closeProductsDetailModal);
        elements.closeProductsDetailFooterBtn.addEventListener('click', closeProductsDetailModal);

        elements.mobilePrevBtn.addEventListener('click', async () => {
            if (state.currentPage <= 1) {
                return;
            }

            state.currentPage -= 1;
            await loadOrders();
        });

        elements.mobileNextBtn.addEventListener('click', async () => {
            const totalPages = Math.max(1, Math.ceil(state.totalCount / state.pageSize));
            if (state.currentPage >= totalPages) {
                return;
            }

            state.currentPage += 1;
            await loadOrders();
        });
    }

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        setCurrentDate();
        setDefaultFilterTimeRange();
        bindEvents();
        enhanceSortHeaders();

        try {
            const ready = await loadGroupInfo();
            if (!ready) {
                return;
            }

            await loadOrders();
        } catch (error) {
            await dashboardApp.showToast(error.message || '加载订单失败。', 'error');
        }
    });
})();
