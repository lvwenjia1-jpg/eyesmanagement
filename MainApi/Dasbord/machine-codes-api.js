(function () {
    let machineCodes = [];
    let totalCount = 0;
    let currentPage = 1;
    const itemsPerPage = 10;
    let currentKeyword = '';
    let editingMachineCode = null;

    const machineCodesTableBody = document.getElementById('machineCodesTableBody');
    const addMachineCodeBtn = document.getElementById('addMachineCodeBtn');
    const machineCodeModal = document.getElementById('machineCodeModal');
    const closeModal = document.getElementById('closeModal');
    const cancelBtn = document.getElementById('cancelBtn');
    const machineCodeForm = document.getElementById('machineCodeForm');
    const modalTitle = document.getElementById('modalTitle');
    const searchInput = document.getElementById('searchInput');
    const searchBtn = document.getElementById('searchBtn');
    const logoutBtn = document.getElementById('logoutBtn');
    const paginationContainer = document.getElementById('pagination');
    const pageInfo = document.getElementById('pageInfo');
    const mobilePrevBtn = document.getElementById('mobilePrevBtn');
    const mobileNextBtn = document.getElementById('mobileNextBtn');

    function openModal() {
        machineCodeModal.classList.remove('hidden');
    }

    function closeMachineCodeModal() {
        machineCodeModal.classList.add('hidden');
    }

    function renderMachineCodes() {
        machineCodesTableBody.innerHTML = '';

        if (machineCodes.length === 0) {
            machineCodesTableBody.innerHTML = '<tr><td colspan="4" class="px-6 py-4 text-center text-gray-500">暂无机器码数据</td></tr>';
            return;
        }

        machineCodes.forEach(machineCode => {
            const row = document.createElement('tr');
            row.className = 'hover:bg-gray-50 transition-all';
            row.innerHTML = `
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm font-medium text-gray-900">${dashboardApp.escapeHtml(machineCode.code)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.escapeHtml(machineCode.description)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="text-sm text-gray-500">${dashboardApp.formatDateTime(machineCode.createdAtUtc)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <button class="text-primary hover:text-blue-800 mr-3 edit-btn" data-id="${machineCode.id}">
                        <i class="fa fa-pencil"></i> 编辑
                    </button>
                    <button class="text-danger hover:text-red-800 delete-btn" data-id="${machineCode.id}">
                        <i class="fa fa-trash"></i> 删除
                    </button>
                </td>
            `;
            machineCodesTableBody.appendChild(row);
        });

        document.querySelectorAll('.edit-btn').forEach(btn => {
            btn.addEventListener('click', handleEditMachineCode);
        });

        document.querySelectorAll('.delete-btn').forEach(btn => {
            btn.addEventListener('click', handleDeleteMachineCode);
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
                    await loadMachineCodes();
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

    async function loadMachineCodes() {
        const query = new URLSearchParams({
            pageNumber: String(currentPage),
            pageSize: String(itemsPerPage),
            isActive: 'true'
        });
        if (currentKeyword) {
            query.set('keyword', currentKeyword);
        }

        const response = await dashboardApp.apiRequest(`/api/machines?${query.toString()}`);
        machineCodes = response.items || [];
        totalCount = response.totalCount || 0;
        currentPage = response.pageNumber || currentPage;
        renderMachineCodes();
        renderPagination();
    }

    function handleAddMachineCode() {
        editingMachineCode = null;
        modalTitle.textContent = '添加机器码';
        machineCodeForm.reset();
        openModal();
    }

    function handleEditMachineCode(event) {
        const machineCodeId = Number(event.currentTarget.dataset.id);
        const machineCode = machineCodes.find(item => item.id === machineCodeId);
        if (!machineCode) {
            return;
        }

        editingMachineCode = machineCode;
        modalTitle.textContent = '编辑机器码';
        document.getElementById('machineCodeId').value = String(machineCode.id);
        document.getElementById('code').value = machineCode.code;
        document.getElementById('description').value = machineCode.description;
        openModal();
    }

    async function handleDeleteMachineCode(event) {
        const machineCodeId = Number(event.currentTarget.dataset.id);
        if (!confirm('确定要删除此机器码吗？')) {
            return;
        }

        try {
            await dashboardApp.apiRequest(`/api/machines/${machineCodeId}`, { method: 'DELETE' });
            dashboardApp.showToast('机器码已删除');
            await loadMachineCodes();
        } catch (error) {
            dashboardApp.showToast(error.message || '删除失败', 'error');
        }
    }

    async function handleFormSubmit(event) {
        event.preventDefault();

        const code = document.getElementById('code').value.trim();
        const description = document.getElementById('description').value.trim();

        if (!code || !description) {
            dashboardApp.showToast('请填写完整机器码和描述', 'error');
            return;
        }

        try {
            if (editingMachineCode) {
                await dashboardApp.apiRequest(`/api/machines/${editingMachineCode.id}`, {
                    method: 'PUT',
                    body: {
                        code,
                        description,
                        isActive: editingMachineCode.isActive
                    }
                });
                dashboardApp.showToast('机器码已更新');
            } else {
                await dashboardApp.apiRequest('/api/machines', {
                    method: 'POST',
                    body: { code, description }
                });
                dashboardApp.showToast('机器码已创建');
            }

            closeMachineCodeModal();
            await loadMachineCodes();
        } catch (error) {
            dashboardApp.showToast(error.message || '保存失败', 'error');
        }
    }

    async function handleSearch() {
        currentKeyword = searchInput.value.trim();
        currentPage = 1;
        await loadMachineCodes();
    }

    async function handleMobilePrev() {
        if (currentPage <= 1) {
            return;
        }
        currentPage -= 1;
        await loadMachineCodes();
    }

    async function handleMobileNext() {
        const totalPages = Math.max(1, Math.ceil(totalCount / itemsPerPage));
        if (currentPage >= totalPages) {
            return;
        }
        currentPage += 1;
        await loadMachineCodes();
    }

    addMachineCodeBtn.addEventListener('click', handleAddMachineCode);
    closeModal.addEventListener('click', closeMachineCodeModal);
    cancelBtn.addEventListener('click', closeMachineCodeModal);
    machineCodeForm.addEventListener('submit', handleFormSubmit);
    searchBtn.addEventListener('click', handleSearch);
    searchInput.addEventListener('keyup', event => {
        if (event.key === 'Enter') {
            handleSearch();
        }
    });
    mobilePrevBtn.addEventListener('click', handleMobilePrev);
    mobileNextBtn.addEventListener('click', handleMobileNext);
    logoutBtn.addEventListener('click', () => dashboardApp.logout());

    machineCodeModal.addEventListener('click', event => {
        if (event.target === machineCodeModal) {
            closeMachineCodeModal();
        }
    });

    document.addEventListener('DOMContentLoaded', async () => {
        if (!dashboardApp.requireAuth('login.html')) {
            return;
        }

        try {
            await loadMachineCodes();
        } catch (error) {
            dashboardApp.showToast(error.message || '加载机器码失败', 'error');
        }
    });
})();
