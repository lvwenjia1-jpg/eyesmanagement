(function () {
    let businessGroups = [];

    const businessGroupsContainer = document.getElementById('businessGroupsContainer');
    const balanceModal = document.getElementById('balanceModal');
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

    function openBalanceModal(group) {
        document.getElementById('groupId').value = String(group.id);
        document.getElementById('groupName').value = group.name;
        document.getElementById('balance').value = String(group.balance);
        balanceModal.classList.remove('hidden');
    }

    function renderBusinessGroups() {
        businessGroupsContainer.innerHTML = '';

        if (businessGroups.length === 0) {
            businessGroupsContainer.innerHTML = '<div class="col-span-full bg-white rounded-lg shadow p-8 text-center text-gray-500">暂无业务群数据</div>';
            return;
        }

        businessGroups.forEach(group => {
            const card = document.createElement('div');
            card.className = 'group-card';
            card.innerHTML = `
                <div class="flex items-center justify-between mb-4">
                    <h3 class="text-xl font-bold text-gray-800">${dashboardApp.escapeHtml(group.name)}</h3>
                    <div class="text-2xl text-primary">
                        <i class="fa fa-shopping-cart"></i>
                    </div>
                </div>
                <div class="mb-4">
                    <div class="text-sm text-gray-500 mb-1">余额</div>
                    <div class="flex items-center">
                        <span class="text-2xl font-bold text-gray-800">¥${Number(group.balance).toLocaleString()}</span>
                        <button class="ml-2 text-primary hover:text-blue-700 edit-balance" data-id="${group.id}">
                            <i class="fa fa-pencil"></i>
                        </button>
                    </div>
                </div>
                <div>
                    <div class="text-sm text-gray-500 mb-1">订单量</div>
                    <div class="text-2xl font-bold text-gray-800">${group.orderCount}</div>
                </div>
                <div class="mt-4 pt-4 border-t border-gray-200">
                    <button class="w-full bg-primary hover:bg-blue-600 text-white py-2 rounded-md transition-all view-orders" data-id="${group.id}">
                        查看订单
                    </button>
                </div>
            `;
            businessGroupsContainer.appendChild(card);
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

                dashboardApp.setOrderFilter({
                    businessGroupId: group.id,
                    businessGroupName: group.name
                });
                window.location.href = 'orders.html';
            });
        });
    }

    async function loadBusinessGroups() {
        const query = new URLSearchParams({
            pageNumber: '1',
            pageSize: '200'
        });
        const response = await dashboardApp.apiRequest(`/api/business-groups?${query.toString()}`);
        businessGroups = response.items || [];
        renderBusinessGroups();
    }

    async function handleBalanceSubmit(event) {
        event.preventDefault();
        const groupId = Number(document.getElementById('groupId').value);
        const balance = Number(document.getElementById('balance').value);

        if (!Number.isFinite(balance) || balance < 0) {
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

    closeBalanceModalButton.addEventListener('click', closeBalanceModal);
    cancelBalanceBtn.addEventListener('click', closeBalanceModal);
    balanceForm.addEventListener('submit', handleBalanceSubmit);
    logoutBtn.addEventListener('click', () => dashboardApp.logout());

    balanceModal.addEventListener('click', event => {
        if (event.target === balanceModal) {
            closeBalanceModal();
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
