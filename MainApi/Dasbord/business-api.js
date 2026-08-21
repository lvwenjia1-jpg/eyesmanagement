(function () {
    const ALL_BUSINESS_GROUP_ID = 0;
    const ALL_BUSINESS_GROUP_NAME = '全部业务群';

    let businessGroups = [];

    const businessGroupsContainer = document.getElementById('businessGroupsContainer');
    const balanceModal = document.getElementById('balanceModal');
    const createGroupModal = document.getElementById('createGroupModal');
    const openCreateGroupModalButton = document.getElementById('openCreateGroupModal');
    const closeCreateGroupModalButton = document.getElementById('closeCreateGroupModal');
    const cancelCreateGroupBtn = document.getElementById('cancelCreateGroupBtn');
    const createGroupForm = document.getElementById('createGroupForm');
    const closeBalanceModalButton = document.getElementById('closeBalanceModal');
    const cancelBalanceBtn = document.getElementById('cancelBalanceBtn');
    const balanceForm = document.getElementById('balanceForm');
    const currentDateEl = document.getElementById('currentDate');
    const logoutBtn = document.getElementById('logoutBtn');

    function setCurrentDate() {
        currentDateEl.textContent = new Date().toLocaleDateString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit'
        });
    }

    function closeBalanceModal() {
        balanceModal.classList.add('hidden');
    }

    function closeCreateGroupModal() {
        createGroupModal.classList.add('hidden');
        createGroupForm.reset();
        document.getElementById('createGroupBalance').value = '0';
    }

    function openBalanceModal(group) {
        document.getElementById('groupId').value = String(group.id);
        document.getElementById('groupName').value = group.name;
        document.getElementById('balance').value = String(group.balance);
        balanceModal.classList.remove('hidden');
    }

    function openCreateGroupModal() {
        createGroupModal.classList.remove('hidden');
        document.getElementById('createGroupName').focus();
    }

    function openOrders(group) {
        dashboardApp.setOrderFilter({
            businessGroupId: group.id,
            businessGroupName: group.name
        });
        const query = new URLSearchParams({
            businessGroupId: String(group.id),
            businessGroupName: group.name
        });
        window.location.href = `orders.html?${query.toString()}`;
    }

    function createAllBusinessGroup(totalOrderCount) {
        return {
            id: ALL_BUSINESS_GROUP_ID,
            name: ALL_BUSINESS_GROUP_NAME,
            orderCount: Number(totalOrderCount || 0),
            isAllBusinessGroup: true
        };
    }

    function renderGroupCard(group) {
        const canDeleteBusinessGroup = dashboardApp.isAdmin(dashboardApp.getCurrentUserRole());
        const card = document.createElement('div');
        card.className = group.isAllBusinessGroup
            ? 'group-card border-2 border-primary/25 bg-gradient-to-br from-sky-50 via-white to-blue-50 shadow-md'
            : 'group-card';

        card.innerHTML = group.isAllBusinessGroup
            ? `
                <div class="absolute right-0 top-0 rounded-bl-xl bg-primary px-3 py-1 text-xs font-semibold tracking-wide text-white">
                    默认
                </div>
                <div class="flex items-start justify-between mb-5 pr-14">
                    <div>
                        <div class="mb-2 inline-flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                            <i class="fa fa-th-large text-lg"></i>
                        </div>
                        <h3 class="text-xl font-bold text-gray-800">${dashboardApp.escapeHtml(group.name)}</h3>
                        <p class="mt-2 text-sm leading-6 text-slate-500">聚合显示所有业务群订单，便于统一筛选、导出和管理。</p>
                    </div>
                </div>
                <div class="rounded-2xl border border-sky-100 bg-white/80 px-4 py-4">
                    <div class="text-sm text-slate-500 mb-1">订单量</div>
                    <div class="text-3xl font-bold tracking-tight text-slate-900">${Number(group.orderCount || 0)}</div>
                </div>
                <div class="mt-4 pt-4 border-t border-sky-100">
                    <button class="w-full bg-primary hover:bg-blue-600 text-white py-2.5 rounded-md transition-all view-orders" data-id="${group.id}">
                        查看订单
                    </button>
                </div>
            `
            : `
                <div class="flex items-center justify-between mb-4">
                    <h3 class="text-xl font-bold text-gray-800">${dashboardApp.escapeHtml(group.name)}</h3>
                    <div class="flex items-center gap-3">
                        ${canDeleteBusinessGroup ? `
                        <button class="text-red-500 hover:text-red-700 delete-group" data-id="${group.id}" title="删除业务群">
                            <i class="fa fa-trash"></i>
                        </button>
                        ` : ''}
                    </div>
                </div>
                <div class="mb-4">
                    <div class="text-sm text-gray-500 mb-1">余额</div>
                    <div class="flex items-center">
                        <span class="text-2xl font-bold text-gray-800">¥${Number(group.balance || 0).toLocaleString()}</span>
                        <button class="ml-2 text-primary hover:text-blue-700 edit-balance" data-id="${group.id}">
                            <i class="fa fa-pencil"></i>
                        </button>
                    </div>
                </div>
                <div>
                    <div class="text-sm text-gray-500 mb-1">订单量</div>
                    <div class="text-2xl font-bold text-gray-800">${Number(group.orderCount || 0)}</div>
                </div>
                <div class="mt-4 pt-4 border-t border-gray-200">
                    <button class="w-full bg-primary hover:bg-blue-600 text-white py-2 rounded-md transition-all view-orders" data-id="${group.id}">
                        查看订单
                    </button>
                </div>
            `;

        return card;
    }

    function renderBusinessGroups() {
        businessGroupsContainer.innerHTML = '';

        if (businessGroups.length === 0) {
            businessGroupsContainer.innerHTML = '<div class="col-span-full bg-white rounded-lg shadow p-8 text-center text-gray-500">暂无业务群数据</div>';
            return;
        }

        businessGroups.forEach(group => {
            businessGroupsContainer.appendChild(renderGroupCard(group));
        });

        document.querySelectorAll('.edit-balance').forEach(button => {
            button.addEventListener('click', event => {
                event.stopPropagation();
                const id = Number(event.currentTarget.dataset.id);
                const group = businessGroups.find(item => item.id === id);
                if (group) {
                    openBalanceModal(group);
                }
            });
        });

        document.querySelectorAll('.view-orders').forEach(button => {
            button.addEventListener('click', event => {
                const id = Number(event.currentTarget.dataset.id);
                const group = businessGroups.find(item => item.id === id);
                if (!group) {
                    return;
                }

                openOrders(group);
            });
        });

        document.querySelectorAll('.delete-group').forEach(button => {
            button.addEventListener('click', async event => {
                event.stopPropagation();
                const id = Number(event.currentTarget.dataset.id);
                const group = businessGroups.find(item => item.id === id);
                if (!group) {
                    return;
                }

                const confirmed = await dashboardApp.showConfirm(`确认删除业务群“${group.name}”吗？已有订单会保留，但不再归属到该业务群。`, {
                    title: '删除业务群',
                    type: 'error',
                    confirmText: '删除'
                });
                if (!confirmed) {
                    return;
                }

                try {
                    await dashboardApp.apiRequest(`/api/business-groups/${group.id}`, {
                        method: 'DELETE'
                    });
                    await loadBusinessGroups();
                    await dashboardApp.showToast('业务群已删除');
                } catch (error) {
                    await dashboardApp.showToast(error.message || '删除业务群失败', 'error');
                }
            });
        });
    }

    async function loadBusinessGroups() {
        const query = new URLSearchParams({
            pageNumber: '1',
            pageSize: '200'
        });

        const response = await dashboardApp.apiRequest(`/api/business-groups?${query.toString()}`);
        const groups = Array.isArray(response.items) ? response.items : [];

        let totalOrderCount = groups.reduce((sum, group) => sum + Number(group.orderCount || 0), 0);
        try {
            const allOrdersResponse = await dashboardApp.apiRequest('/api/business-groups/0/orders?pageNumber=1&pageSize=1');
            totalOrderCount = Number(allOrdersResponse.totalCount || 0);
        } catch {
            // Fall back to the summed counts if aggregate querying is temporarily unavailable.
        }

        businessGroups = [createAllBusinessGroup(totalOrderCount), ...groups];
        renderBusinessGroups();
    }

    async function handleBalanceSubmit(event) {
        event.preventDefault();
        const groupId = Number(document.getElementById('groupId').value);
        const balance = Number(document.getElementById('balance').value);

        if (!Number.isFinite(balance)) {
            dashboardApp.showToast('请输入有效余额', 'error');
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/business-groups/${groupId}/balance`, {
                method: 'PUT',
                body: { balance }
            });
            closeBalanceModal();
            dashboardApp.showToast('余额已更新');
            await loadBusinessGroups();
        } catch (error) {
            dashboardApp.showToast(error.message || '更新余额失败', 'error');
        }
    }

    async function handleCreateGroupSubmit(event) {
        event.preventDefault();
        const name = document.getElementById('createGroupName').value.trim();
        const balance = Number(document.getElementById('createGroupBalance').value);

        if (!name) {
            dashboardApp.showToast('请输入业务群名称', 'error');
            return;
        }

        if (!Number.isFinite(balance)) {
            dashboardApp.showToast('请输入有效余额', 'error');
            return;
        }

        try {
            await dashboardApp.apiRequest('/api/business-groups', {
                method: 'POST',
                body: { name, balance }
            });
            closeCreateGroupModal();
            dashboardApp.showToast('业务群已创建');
            await loadBusinessGroups();
        } catch (error) {
            dashboardApp.showToast(error.message || '新增业务群失败', 'error');
        }
    }

    closeBalanceModalButton.addEventListener('click', closeBalanceModal);
    cancelBalanceBtn.addEventListener('click', closeBalanceModal);
    balanceForm.addEventListener('submit', handleBalanceSubmit);
    openCreateGroupModalButton.addEventListener('click', openCreateGroupModal);
    closeCreateGroupModalButton.addEventListener('click', closeCreateGroupModal);
    cancelCreateGroupBtn.addEventListener('click', closeCreateGroupModal);
    createGroupForm.addEventListener('submit', handleCreateGroupSubmit);
    logoutBtn.addEventListener('click', () => dashboardApp.logout());

    balanceModal.addEventListener('click', event => {
        if (event.target === balanceModal) {
            closeBalanceModal();
        }
    });

    createGroupModal.addEventListener('click', event => {
        if (event.target === createGroupModal) {
            closeCreateGroupModal();
        }
    });

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        setCurrentDate();
        try {
            await loadBusinessGroups();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载业务群失败', 'error');
        }
    });
})();
