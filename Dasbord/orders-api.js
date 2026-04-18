(function () {
    let selectedGroupId = 0;
    let selectedGroupName = '';
    let selectedGroupBalance = 0;
    let orders = [];
    let totalCount = 0;
    let currentPage = 1;
    const itemsPerPage = 10;

    const groupTitle = document.getElementById('groupTitle');
    const currentDateEl = document.getElementById('currentDate');
    const ordersTableBody = document.getElementById('ordersTableBody');
    const backBtn = document.getElementById('backBtn');
    const startTimeInput = document.getElementById('startTime');
    const endTimeInput = document.getElementById('endTime');
    const filterBtn = document.getElementById('filterBtn');
    const resetBtn = document.getElementById('resetBtn');
    const exportBtn = document.getElementById('exportBtn');
    const orderModal = document.getElementById('orderModal');
    const closeOrderModalBtn = document.getElementById('closeOrderModal');
    const cancelOrderBtn = document.getElementById('cancelOrderBtn');
    const orderForm = document.getElementById('orderForm');
    const productsContainer = document.getElementById('productsContainer');
    const logoutBtn = document.getElementById('logoutBtn');
    const pageInfo = document.getElementById('pageInfo');
    const paginationContainer = document.getElementById('pagination');
    const mobilePrevBtn = document.getElementById('mobilePrevBtn');
    const mobileNextBtn = document.getElementById('mobileNextBtn');

    function setCurrentDate() {
        currentDateEl.textContent = new Date().toLocaleDateString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit'
        });
    }

    function setDefaultFilterTimeRange() {
        startTimeInput.value = '';
        endTimeInput.value = '';
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

    function closeOrderModal() {
        orderModal.classList.add('hidden');
    }

    function openOrderModal(order) {
        document.getElementById('orderId').value = String(order.id);
        document.getElementById('editOrderId').value = order.orderNo;
        document.getElementById('editUploader').value = order.uploaderLoginName || '-';
        document.getElementById('editRecipient').value = order.receiverName || '-';
        document.getElementById('editAddress').value = order.receiverAddress || '-';
        document.getElementById('editAmount').value = String(order.amount);
        document.getElementById('editTrackingNumber').value = order.trackingNumber || '';

        productsContainer.innerHTML = '';
        const orderItems = order.items || [];
        if (orderItems.length === 0) {
            productsContainer.innerHTML = '<div class="text-sm text-gray-500">无商品明细</div>';
        } else {
            orderItems.forEach(item => {
                const itemDiv = document.createElement('div');
                itemDiv.className = 'mb-2 p-2 bg-gray-50 rounded';
                itemDiv.innerHTML = `
                    <div class="flex items-center">
                        <div class="mr-2 text-gray-500"><i class="fa fa-cube"></i></div>
                        <div>
                            <div class="font-medium">${dashboardApp.escapeHtml(item.productName)}</div>
                            <div class="text-xs text-gray-500">编码: ${dashboardApp.escapeHtml(item.productCode)}</div>
                        </div>
                        <div class="ml-auto font-medium">x ${item.quantity}</div>
                    </div>
                `;
                productsContainer.appendChild(itemDiv);
            });
        }

        orderModal.classList.remove('hidden');
    }

    function renderOrders() {
        ordersTableBody.innerHTML = '';

        if (orders.length === 0) {
            ordersTableBody.innerHTML = '<tr><td colspan="8" class="px-6 py-4 text-center text-gray-500">暂无订单数据</td></tr>';
            return;
        }

        orders.forEach(order => {
            const productsText = (order.items || [])
                .map(item => `${item.productName} (${item.productCode}) x ${item.quantity}`)
                .join('<br>');

            const row = document.createElement('tr');
            row.className = 'hover:bg-gray-50 transition-all';
            row.innerHTML = `
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(order.orderNo)}</div>
                    <div class="text-xs text-gray-400">${dashboardApp.formatDateTime(order.createdAtUtc)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.uploaderLoginName || '-')}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.receiverName || '-')}</div>
                </td>
                <td class="px-6 py-4">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.receiverAddress || '-')}</div>
                </td>
                <td class="px-6 py-4">
                    <div class="text-sm text-gray-500">${productsText || '-'}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">¥${Number(order.amount).toFixed(2)}</div>
                    <button class="text-primary hover:text-blue-800 text-xs edit-amount" data-id="${order.id}">
                        <i class="fa fa-pencil"></i> 修改
                    </button>
                </td>
                <td class="px-6 py-4">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(order.trackingNumber || '-')}</div>
                    <button class="text-primary hover:text-blue-800 text-xs edit-tracking" data-id="${order.id}">
                        <i class="fa fa-pencil"></i> 输入
                    </button>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <button class="text-primary hover:text-blue-800 edit-order" data-id="${order.id}">
                        <i class="fa fa-edit"></i> 编辑
                    </button>
                </td>
            `;
            ordersTableBody.appendChild(row);
        });

        document.querySelectorAll('.edit-order').forEach(button => {
            button.addEventListener('click', event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = orders.find(item => item.id === orderId);
                if (order) {
                    openOrderModal(order);
                }
            });
        });

        document.querySelectorAll('.edit-amount').forEach(button => {
            button.addEventListener('click', async event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = orders.find(item => item.id === orderId);
                if (!order) {
                    return;
                }

                const value = prompt('请输入新的订单金额:', String(order.amount));
                if (value === null) {
                    return;
                }

                const amount = Number(value);
                if (!Number.isFinite(amount) || amount < 0) {
                    dashboardApp.showToast('请输入有效金额', 'error');
                    return;
                }

                await updateOrder(order.id, amount, order.trackingNumber || '');
            });
        });

        document.querySelectorAll('.edit-tracking').forEach(button => {
            button.addEventListener('click', async event => {
                const orderId = Number(event.currentTarget.dataset.id);
                const order = orders.find(item => item.id === orderId);
                if (!order) {
                    return;
                }

                const value = prompt('请输入快递单号:', order.trackingNumber || '');
                if (value === null) {
                    return;
                }

                await updateOrder(order.id, Number(order.amount), value.trim());
            });
        });
    }

    function renderPagination() {
        const totalPages = Math.max(1, Math.ceil(totalCount / itemsPerPage));
        const startItem = totalCount === 0 ? 0 : (currentPage - 1) * itemsPerPage + 1;
        const endItem = Math.min(currentPage * itemsPerPage, totalCount);

        pageInfo.innerHTML = `
            <p class="text-sm text-gray-700">
                显示 <span class="font-medium">${startItem}</span> 到 <span class="font-medium">${endItem}</span> 条，共 <span class="font-medium">${totalCount}</span> 条记录
            </p>
        `;

        mobilePrevBtn.disabled = currentPage <= 1;
        mobileNextBtn.disabled = currentPage >= totalPages;
        mobilePrevBtn.classList.toggle('opacity-50', mobilePrevBtn.disabled);
        mobileNextBtn.classList.toggle('opacity-50', mobileNextBtn.disabled);

        paginationContainer.innerHTML = '';
        if (totalPages <= 1) {
            return;
        }

        const nav = document.createElement('nav');
        nav.className = 'relative z-0 inline-flex rounded-md shadow-sm -space-x-px';
        nav.setAttribute('aria-label', 'Pagination');

        const appendButton = (label, page, active, disabled, edge) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = label;
            button.className = `relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium ${
                active ? 'bg-primary text-white' : 'bg-white text-gray-700 hover:bg-gray-50'
            } ${disabled ? 'opacity-50 cursor-not-allowed' : ''}`;
            if (edge === 'left') {
                button.classList.add('rounded-l-md');
            }
            if (edge === 'right') {
                button.classList.add('rounded-r-md');
            }
            if (!disabled && !active) {
                button.addEventListener('click', async () => {
                    currentPage = page;
                    await loadOrders();
                });
            }
            nav.appendChild(button);
        };

        appendButton('<', currentPage - 1, false, currentPage <= 1, 'left');
        for (let page = 1; page <= totalPages; page += 1) {
            appendButton(String(page), page, page === currentPage, false, null);
        }
        appendButton('>', currentPage + 1, false, currentPage >= totalPages, 'right');
        paginationContainer.appendChild(nav);
    }

    async function loadGroupInfo() {
        const group = await dashboardApp.apiRequest(`/api/business-groups/${selectedGroupId}`);
        selectedGroupName = group.name || selectedGroupName;
        selectedGroupBalance = Number(group.balance || 0);
        groupTitle.textContent = `${selectedGroupName} - 订单详情`;
    }

    async function loadOrders() {
        const query = new URLSearchParams({
            pageNumber: String(currentPage),
            pageSize: String(itemsPerPage)
        });

        const startIso = parseDateTimeLocalToIso(startTimeInput.value);
        const endIso = parseDateTimeLocalToIso(endTimeInput.value);
        if (startIso) {
            query.set('startTime', startIso);
        }
        if (endIso) {
            query.set('endTime', endIso);
        }

        const response = await dashboardApp.apiRequest(`/api/business-groups/${selectedGroupId}/orders?${query.toString()}`);
        orders = response.items || [];
        totalCount = response.totalCount || 0;
        currentPage = response.pageNumber || currentPage;
        renderOrders();
        renderPagination();
    }

    async function updateOrder(orderId, amount, trackingNumber) {
        try {
            await dashboardApp.apiRequest(`/api/orders/${orderId}`, {
                method: 'PUT',
                body: {
                    amount,
                    trackingNumber
                }
            });
            dashboardApp.showToast('订单已更新');
            await loadOrders();
            await loadGroupInfo();
        } catch (error) {
            dashboardApp.showToast(error.message || '更新失败', 'error');
        }
    }

    async function handleOrderSubmit(event) {
        event.preventDefault();

        const orderId = Number(document.getElementById('orderId').value);
        const amount = Number(document.getElementById('editAmount').value);
        const trackingNumber = document.getElementById('editTrackingNumber').value.trim();

        if (!Number.isFinite(amount) || amount < 0) {
            dashboardApp.showToast('请输入有效金额', 'error');
            return;
        }

        await updateOrder(orderId, amount, trackingNumber);
        closeOrderModal();
    }

    async function handleFilter() {
        currentPage = 1;
        await loadOrders();
    }

    async function handleReset() {
        setDefaultFilterTimeRange();
        currentPage = 1;
        await loadOrders();
    }

    function handleExport() {
        const exportOrders = orders.filter(order => (order.trackingNumber || '').trim());
        if (exportOrders.length === 0) {
            dashboardApp.showToast('当前页没有可导出的已填单号订单', 'error');
            return;
        }

        const totalAmount = exportOrders.reduce((sum, order) => sum + Number(order.amount || 0), 0);
        const remainingBalance = selectedGroupBalance - totalAmount;

        let csvContent = '订单号,上传人账号,收件人,收货地址,产品信息,订单金额,快递单号\n';
        exportOrders.forEach(order => {
            const products = (order.items || [])
                .map(item => `${item.productName}(${item.productCode})x${item.quantity}`)
                .join(';');
            csvContent += `"${order.orderNo}","${order.uploaderLoginName || ''}","${order.receiverName || ''}","${order.receiverAddress || ''}","${products}",${Number(order.amount || 0).toFixed(2)},"${order.trackingNumber || ''}"\n`;
        });

        csvContent += `\n总金额,${totalAmount.toFixed(2)}\n`;
        csvContent += `业务群余额,${selectedGroupBalance.toFixed(2)}\n`;
        csvContent += `余额减去总金额,${remainingBalance.toFixed(2)}\n`;
        csvContent += `导出订单数,${exportOrders.length}\n`;

        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `订单数据_${new Date().toISOString().slice(0, 10)}.csv`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
        dashboardApp.showToast('导出完成');
    }

    async function handleMobilePrev() {
        if (currentPage <= 1) {
            return;
        }
        currentPage -= 1;
        await loadOrders();
    }

    async function handleMobileNext() {
        const totalPages = Math.max(1, Math.ceil(totalCount / itemsPerPage));
        if (currentPage >= totalPages) {
            return;
        }
        currentPage += 1;
        await loadOrders();
    }

    backBtn.addEventListener('click', () => {
        window.location.href = 'business.html';
    });
    filterBtn.addEventListener('click', handleFilter);
    resetBtn.addEventListener('click', handleReset);
    exportBtn.addEventListener('click', handleExport);
    closeOrderModalBtn.addEventListener('click', closeOrderModal);
    cancelOrderBtn.addEventListener('click', closeOrderModal);
    orderForm.addEventListener('submit', handleOrderSubmit);
    logoutBtn.addEventListener('click', () => dashboardApp.logout());
    mobilePrevBtn.addEventListener('click', handleMobilePrev);
    mobileNextBtn.addEventListener('click', handleMobileNext);
    orderModal.addEventListener('click', event => {
        if (event.target === orderModal) {
            closeOrderModal();
        }
    });

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        const filter = dashboardApp.getOrderFilter();
        if (!filter || !filter.businessGroupId) {
            window.location.href = 'business.html';
            return;
        }

        selectedGroupId = Number(filter.businessGroupId);
        selectedGroupName = filter.businessGroupName || '业务群';

        setCurrentDate();
        setDefaultFilterTimeRange();

        try {
            await loadGroupInfo();
            await loadOrders();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载订单失败', 'error');
        }
    });
})();
