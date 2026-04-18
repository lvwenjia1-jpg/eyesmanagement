(function () {
    let users = [];
    let totalCount = 0;
    let currentPage = 1;
    const itemsPerPage = 10;
    let currentKeyword = '';
    let editingUserId = null;
    let editingUser = null;

    const userTableBody = document.getElementById('userTableBody');
    const addUserBtn = document.getElementById('addUserBtn');
    const userModal = document.getElementById('userModal');
    const closeModal = document.getElementById('closeModal');
    const cancelBtn = document.getElementById('cancelBtn');
    const userForm = document.getElementById('userForm');
    const modalTitle = document.getElementById('modalTitle');
    const searchInput = document.getElementById('searchInput');
    const searchBtn = document.getElementById('searchBtn');
    const paginationContainer = document.getElementById('pagination');
    const pageInfo = document.getElementById('pageInfo');
    const mobilePrevBtn = document.getElementById('mobilePrevBtn');
    const mobileNextBtn = document.getElementById('mobileNextBtn');
    const logoutBtn = document.getElementById('logoutBtn');

    function openModal() {
        userModal.classList.remove('hidden');
    }

    function closeUserModal() {
        userModal.classList.add('hidden');
    }

    function renderUsers() {
        userTableBody.innerHTML = '';

        if (users.length === 0) {
            userTableBody.innerHTML = '<tr><td colspan="4" class="px-6 py-4 text-center text-gray-500">暂无用户数据</td></tr>';
            return;
        }

        users.forEach(user => {
            const statusBadge = user.isActive
                ? '<span class="inline-flex items-center px-2 py-0.5 rounded text-xs bg-green-100 text-green-700 ml-2">启用</span>'
                : '<span class="inline-flex items-center px-2 py-0.5 rounded text-xs bg-gray-200 text-gray-600 ml-2">禁用</span>';

            const row = document.createElement('tr');
            row.className = 'hover:bg-gray-50 transition-all';
            row.innerHTML = `
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(user.loginName)} ${statusBadge}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">********</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(user.erpId)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${user.id}">
                        <i class="fa fa-pencil"></i> 编辑
                    </button>
                    <button class="text-danger hover:text-red-800 delete-btn" data-id="${user.id}">
                        <i class="fa fa-trash"></i> 删除
                    </button>
                </td>
            `;
            userTableBody.appendChild(row);
        });

        document.querySelectorAll('.edit-btn').forEach(btn => {
            btn.addEventListener('click', handleEditUser);
        });

        document.querySelectorAll('.delete-btn').forEach(btn => {
            btn.addEventListener('click', handleDeleteUser);
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
                    await loadUsers();
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

    async function loadUsers() {
        const query = new URLSearchParams({
            pageNumber: String(currentPage),
            pageSize: String(itemsPerPage)
        });
        if (currentKeyword) {
            query.set('keyword', currentKeyword);
        }

        const response = await dashboardApp.apiRequest(`/api/users?${query.toString()}`);
        users = response.items || [];
        totalCount = response.totalCount || 0;
        currentPage = response.pageNumber || currentPage;
        renderUsers();
        renderPagination();
    }

    function handleAddUser() {
        editingUserId = null;
        editingUser = null;
        modalTitle.textContent = '添加用户';
        userForm.reset();
        document.getElementById('password').required = true;
        openModal();
    }

    function handleEditUser(event) {
        const userId = Number(event.currentTarget.dataset.id);
        const user = users.find(item => item.id === userId);
        if (!user) {
            return;
        }

        editingUserId = user.id;
        editingUser = user;
        modalTitle.textContent = '编辑用户';
        document.getElementById('userId').value = String(user.id);
        document.getElementById('username').value = user.loginName;
        document.getElementById('password').value = '';
        document.getElementById('password').required = false;
        document.getElementById('erpId').value = user.erpId;
        openModal();
    }

    async function handleDeleteUser(event) {
        const userId = Number(event.currentTarget.dataset.id);
        if (!confirm('确定要删除此用户吗？')) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/users/${userId}`, { method: 'DELETE' });
            dashboardApp.showToast('用户已删除');
            await loadUsers();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除失败', 'error');
        }
    }

    async function handleFormSubmit(event) {
        event.preventDefault();

        const loginName = document.getElementById('username').value.trim();
        const password = document.getElementById('password').value;
        const erpId = document.getElementById('erpId').value.trim();

        if (!loginName || !erpId) {
            dashboardApp.showToast('请完整填写账号和 ERP ID', 'error');
            return;
        }

        try {
            if (editingUserId) {
                await dashboardApp.apiRequest(`/api/users/${editingUserId}`, {
                    method: 'PUT',
                    body: {
                        loginName,
                        password,
                        erpId,
                        isActive: editingUser ? editingUser.isActive : true
                    }
                });
                dashboardApp.showToast('用户信息已更新');
            } else {
                if (!password.trim()) {
                    dashboardApp.showToast('新增用户必须填写密码', 'error');
                    return;
                }

                await dashboardApp.apiRequest('/api/users', {
                    method: 'POST',
                    body: {
                        loginName,
                        password,
                        erpId
                    }
                });
                dashboardApp.showToast('用户已创建');
            }

            closeUserModal();
            await loadUsers();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存失败', 'error');
        }
    }

    async function handleSearch() {
        currentKeyword = searchInput.value.trim();
        currentPage = 1;
        await loadUsers();
    }

    async function handleMobilePrev() {
        if (currentPage <= 1) {
            return;
        }
        currentPage -= 1;
        await loadUsers();
    }

    async function handleMobileNext() {
        const totalPages = Math.max(1, Math.ceil(totalCount / itemsPerPage));
        if (currentPage >= totalPages) {
            return;
        }
        currentPage += 1;
        await loadUsers();
    }

    addUserBtn.addEventListener('click', handleAddUser);
    closeModal.addEventListener('click', closeUserModal);
    cancelBtn.addEventListener('click', closeUserModal);
    userForm.addEventListener('submit', handleFormSubmit);
    searchBtn.addEventListener('click', handleSearch);
    searchInput.addEventListener('keyup', event => {
        if (event.key === 'Enter') {
            handleSearch();
        }
    });
    mobilePrevBtn.addEventListener('click', handleMobilePrev);
    mobileNextBtn.addEventListener('click', handleMobileNext);
    logoutBtn.addEventListener('click', () => dashboardApp.logout());
    userModal.addEventListener('click', event => {
        if (event.target === userModal) {
            closeUserModal();
        }
    });

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        try {
            await loadUsers();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载用户失败', 'error');
        }
    });
})();
