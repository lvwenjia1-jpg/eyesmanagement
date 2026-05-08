(function () {
    const API_URL_KEY = 'dashboard.apiBaseUrl';
    const TOKEN_KEY = 'dashboard.authToken';
    const USER_KEY = 'dashboard.currentUser';
    const EXPIRY_KEY = 'dashboard.tokenExpiry';
    const MACHINE_CODE_KEY = 'dashboard.machineCode';
    const ORDER_FILTER_KEY = 'dashboard.orderFilter';

    function normalizeBaseUrl(url) {
        return (url || '').trim().replace(/\/+$/, '');
    }

    function defaultApiBaseUrl() {
        if (window.location.protocol === 'http:' || window.location.protocol === 'https:') {
            return normalizeBaseUrl(window.location.origin);
        }

        return 'https://localhost:7018';
    }

    function getApiBaseUrl() {
        const saved = normalizeBaseUrl(localStorage.getItem(API_URL_KEY) || '');
        const fallback = defaultApiBaseUrl();
        if (!saved) {
            return fallback;
        }

        if (window.location.protocol === 'http:' || window.location.protocol === 'https:') {
            const currentOrigin = normalizeBaseUrl(window.location.origin);
            const path = (window.location.pathname || '').toLowerCase();
            const underDashboard = path === '/dashboard' || path.startsWith('/dashboard/');

            if (underDashboard && saved !== currentOrigin) {
                localStorage.setItem(API_URL_KEY, currentOrigin);
                return currentOrigin;
            }
        }

        return saved;
    }

    function setApiBaseUrl(url) {
        localStorage.setItem(API_URL_KEY, normalizeBaseUrl(url) || defaultApiBaseUrl());
    }

    function getToken() {
        return sessionStorage.getItem(TOKEN_KEY) || '';
    }

    function getCurrentUser() {
        try {
            return JSON.parse(sessionStorage.getItem(USER_KEY) || 'null');
        } catch {
            return null;
        }
    }

    function getCurrentLoginName() {
        const user = getCurrentUser();
        const loginName = user && typeof user.loginName === 'string'
            ? user.loginName.trim()
            : '';
        return loginName;
    }

    function isAuthenticated() {
        return Boolean(getCurrentLoginName());
    }

    function setAuthSession(loginResponse, machineCode) {
        const token = loginResponse && typeof loginResponse.token === 'string'
            ? loginResponse.token
            : '';
        const user = loginResponse ? loginResponse.user : null;
        sessionStorage.setItem(TOKEN_KEY, token);
        if (user) {
            sessionStorage.setItem(USER_KEY, JSON.stringify(user));
        } else {
            sessionStorage.removeItem(USER_KEY);
        }
        sessionStorage.setItem(EXPIRY_KEY, loginResponse && loginResponse.expiresAtUtc ? loginResponse.expiresAtUtc : '');
        sessionStorage.setItem(MACHINE_CODE_KEY, machineCode || '');
    }

    function clearAuthSession() {
        sessionStorage.removeItem(TOKEN_KEY);
        sessionStorage.removeItem(USER_KEY);
        sessionStorage.removeItem(EXPIRY_KEY);
        sessionStorage.removeItem(MACHINE_CODE_KEY);
        sessionStorage.removeItem(ORDER_FILTER_KEY);
    }

    function requireAuth(redirectUrl) {
        if (!isAuthenticated()) {
            clearAuthSession();
            window.location.href = redirectUrl || 'login.html';
            return false;
        }

        return true;
    }

    async function getCurrentUserProfile() {
        const loginName = getCurrentLoginName();
        if (!loginName) {
            throw new Error('Missing loginName in current session.');
        }

        const query = new URLSearchParams({ loginName });
        const profile = await apiRequest(`/api/auth/me?${query.toString()}`, { auth: false });
        sessionStorage.setItem(USER_KEY, JSON.stringify(profile));
        return profile;
    }

    async function apiRequest(path, options) {
        const settings = options || {};
        const headers = new Headers(settings.headers || {});
        const token = getToken();
        const isJsonBody = settings.body && typeof settings.body !== 'string';

        if (settings.auth !== false && token) {
            headers.set('Authorization', `Bearer ${token}`);
        }

        if (isJsonBody) {
            headers.set('Content-Type', 'application/json');
        }

        const response = await fetch(`${getApiBaseUrl()}${path}`, {
            method: settings.method || 'GET',
            headers,
            body: isJsonBody ? JSON.stringify(settings.body) : settings.body
        });

        if (response.status === 401 && settings.auth !== false) {
            clearAuthSession();
            window.location.href = 'login.html';
            throw new Error('登录已失效，请重新登录。');
        }

        if (response.status === 204) {
            return null;
        }

        const contentType = response.headers.get('content-type') || '';
        const payload = contentType.includes('application/json')
            ? await response.json()
            : await response.text();

        if (!response.ok) {
            const message = payload && payload.message
                ? payload.message
                : payload && payload.title
                    ? payload.title
                    : typeof payload === 'string' && payload
                        ? payload
                        : '请求失败。';
            throw new Error(message);
        }

        return payload;
    }

    function formatDateTime(value) {
        if (!value) {
            return '-';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        return date.toLocaleString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    function formatDate(value) {
        if (!value) {
            return '-';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        return date.toLocaleDateString('zh-CN');
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    const dialogQueue = {
        current: Promise.resolve()
    };

    function queueDialog(task) {
        const nextTask = dialogQueue.current
            .catch(() => undefined)
            .then(task);
        dialogQueue.current = nextTask.catch(() => undefined);
        return nextTask;
    }

    function buildDialogTone(type) {
        return type === 'error'
            ? {
                icon: 'fa-exclamation-circle',
                iconClass: 'bg-red-100 text-red-600',
                confirmClass: 'bg-red-600 hover:bg-red-700 focus:ring-red-300'
            }
            : {
                icon: 'fa-check-circle',
                iconClass: 'bg-emerald-100 text-emerald-600',
                confirmClass: 'bg-primary hover:bg-blue-700 focus:ring-blue-300'
            };
    }

    function openDialog(options) {
        const settings = options || {};
        const tone = buildDialogTone(settings.type);
        const title = settings.title || '提示';
        const message = settings.message || '';
        const mode = settings.mode || 'alert';
        const confirmText = settings.confirmText || '确定';
        const cancelText = settings.cancelText || '取消';
        const defaultValue = typeof settings.defaultValue === 'string' ? settings.defaultValue : '';

        return new Promise(resolve => {
            const overlay = document.createElement('div');
            overlay.className = 'fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/45 p-4';

            const inputHtml = mode === 'prompt'
                ? `
                    <div class="mt-4">
                        <input id="dashboardDialogInput" type="text" value="${escapeHtml(defaultValue)}"
                               class="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:border-primary focus:outline-none focus:ring-2 focus:ring-blue-200">
                    </div>
                `
                : '';

            const cancelHtml = mode === 'alert'
                ? ''
                : `<button type="button" id="dashboardDialogCancel" class="inline-flex items-center rounded-md border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50">` +
                    `${escapeHtml(cancelText)}</button>`;

            overlay.innerHTML = `
                <div class="w-full max-w-md overflow-hidden rounded-2xl bg-white shadow-2xl">
                    <div class="flex items-start gap-4 p-6">
                        <div class="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-full ${tone.iconClass}">
                            <i class="fa ${tone.icon} text-lg"></i>
                        </div>
                        <div class="min-w-0 flex-1">
                            <h3 class="text-lg font-semibold text-slate-900">${escapeHtml(title)}</h3>
                            <div class="mt-2 whitespace-pre-wrap break-words text-sm leading-6 text-slate-600">${escapeHtml(message)}</div>
                            ${inputHtml}
                        </div>
                    </div>
                    <div class="flex items-center justify-end gap-3 border-t border-slate-200 bg-slate-50 px-6 py-4">
                        ${cancelHtml}
                        <button type="button" id="dashboardDialogConfirm" class="inline-flex items-center rounded-md px-4 py-2 text-sm font-medium text-white shadow-sm focus:outline-none focus:ring-2 ${tone.confirmClass}">
                            ${escapeHtml(confirmText)}
                        </button>
                    </div>
                </div>
            `;

            const cleanup = result => {
                overlay.remove();
                document.removeEventListener('keydown', onKeydown);
                resolve(result);
            };

            const onKeydown = event => {
                if (event.key === 'Escape') {
                    cleanup(mode === 'prompt' ? null : false);
                    return;
                }

                if (event.key === 'Enter' && mode === 'prompt') {
                    event.preventDefault();
                    const input = overlay.querySelector('#dashboardDialogInput');
                    cleanup(input ? input.value : defaultValue);
                }
            };

            overlay.querySelector('#dashboardDialogConfirm')?.addEventListener('click', () => {
                if (mode === 'prompt') {
                    const input = overlay.querySelector('#dashboardDialogInput');
                    cleanup(input ? input.value : defaultValue);
                    return;
                }

                cleanup(true);
            });

            overlay.querySelector('#dashboardDialogCancel')?.addEventListener('click', () => {
                cleanup(mode === 'prompt' ? null : false);
            });

            overlay.addEventListener('click', event => {
                if (event.target === overlay) {
                    cleanup(mode === 'prompt' ? null : false);
                }
            });

            document.addEventListener('keydown', onKeydown);
            document.body.appendChild(overlay);

            if (mode === 'prompt') {
                const input = overlay.querySelector('#dashboardDialogInput');
                if (input) {
                    input.focus();
                    input.select();
                }
            } else {
                overlay.querySelector('#dashboardDialogConfirm')?.focus();
            }
        });
    }

    function showToast(message, type) {
        return queueDialog(() => openDialog({
            title: type === 'error' ? '操作失败' : '操作提示',
            message,
            type: type || 'success',
            mode: 'alert',
            confirmText: '确定'
        }));
    }

    function showConfirm(message, options) {
        const settings = options || {};
        return queueDialog(() => openDialog({
            title: settings.title || '请确认',
            message,
            type: settings.type || 'success',
            mode: 'confirm',
            confirmText: settings.confirmText || '确定',
            cancelText: settings.cancelText || '取消'
        }));
    }

    function showPrompt(message, defaultValue, options) {
        const settings = options || {};
        return queueDialog(() => openDialog({
            title: settings.title || '请输入',
            message,
            type: settings.type || 'success',
            mode: 'prompt',
            defaultValue: defaultValue || '',
            confirmText: settings.confirmText || '确定',
            cancelText: settings.cancelText || '取消'
        }));
    }

    function logout() {
        clearAuthSession();
        window.location.href = 'login.html';
    }

    function setOrderFilter(filter) {
        sessionStorage.setItem(ORDER_FILTER_KEY, JSON.stringify(filter || null));
    }

    function getOrderFilter() {
        try {
            return JSON.parse(sessionStorage.getItem(ORDER_FILTER_KEY) || 'null');
        } catch {
            return null;
        }
    }

    function getSidebarActiveKey() {
        const path = (window.location.pathname || '').toLowerCase();
        const fileName = path.split('/').pop() || '';
        switch (fileName) {
            case 'index.html':
                return 'users';
            case 'business.html':
                return 'business';
            case 'orders.html':
                return 'orders';
            case 'price-rules.html':
                return 'prices';
            case 'product-catalog.html':
                return 'catalog';
            case 'settings.html':
                return 'settings';
            case 'machine-codes.html':
                return 'machines';
            default:
                return '';
        }
    }

    function applyDashboardShellLayout() {
        const sidebar = document.getElementById('dashboardSidebar');
        if (!sidebar) {
            return;
        }

        const shell = sidebar.parentElement;
        const main = shell ? shell.querySelector('main') : null;

        document.body.classList.add('md:h-screen', 'md:overflow-hidden');

        if (shell) {
            shell.classList.add('md:h-screen', 'md:overflow-hidden');
        }

        sidebar.classList.add('md:h-screen', 'md:overflow-y-auto');

        if (main) {
            main.classList.add('md:min-h-0', 'md:overflow-y-auto');
        }
    }

    function renderDashboardSidebar() {
        const sidebar = document.getElementById('dashboardSidebar');
        if (!sidebar) {
            return;
        }

        const activeKey = getSidebarActiveKey();
        const linkClass = key => key === activeKey
            ? 'flex items-center px-4 py-3 bg-primary bg-opacity-80 text-white'
            : 'flex items-center px-4 py-3 hover:bg-gray-700 text-gray-300 hover:text-white transition-all';

        sidebar.innerHTML = `
            <div class="p-4 flex items-center justify-center md:justify-start">
                <i class="fa fa-cogs text-2xl mr-2"></i>
                <h1 class="text-xl font-bold">管理系统</h1>
            </div>
            <nav class="mt-6">
                <a href="index.html" class="${linkClass('users')}">
                    <i class="fa fa-users mr-3"></i>
                    <span>用户管理</span>
                </a>
                <a href="business.html" class="${linkClass('business')}">
                    <i class="fa fa-shopping-bag mr-3"></i>
                    <span>业务群管理</span>
                </a>
                <a href="orders.html" class="${linkClass('orders')}">
                    <i class="fa fa-list-alt mr-3"></i>
                    <span>订单管理</span>
                </a>
                <a href="price-rules.html" class="${linkClass('prices')}">
                    <i class="fa fa-tags mr-3"></i>
                    <span>价格管理</span>
                </a>
                <a href="product-catalog.html" class="${linkClass('catalog')}">
                    <i class="fa fa-barcode mr-3"></i>
                    <span>商品编码管理</span>
                </a>
                <a href="settings.html" class="${linkClass('settings')}">
                    <i class="fa fa-sliders mr-3"></i>
                    <span>周期设置</span>
                </a>
                <a href="machine-codes.html" class="${linkClass('machines')}">
                    <i class="fa fa-key mr-3"></i>
                    <span>机器码管理</span>
                </a>
                <a href="#" id="logoutBtn" class="flex items-center px-4 py-3 hover:bg-gray-700 text-gray-300 hover:text-white transition-all">
                    <i class="fa fa-sign-out mr-3"></i>
                    <span>退出登录</span>
                </a>
            </nav>
        `;

        const logoutBtn = document.getElementById('logoutBtn');
        if (logoutBtn) {
            logoutBtn.addEventListener('click', event => {
                event.preventDefault();
                logout();
            });
        }
    }

    renderDashboardSidebar();

    window.dashboardApp = {
        getApiBaseUrl,
        setApiBaseUrl,
        getToken,
        getCurrentUser,
        getCurrentLoginName,
        isAuthenticated,
        setAuthSession,
        clearAuthSession,
        requireAuth,
        getCurrentUserProfile,
        apiRequest,
        formatDateTime,
        formatDate,
        escapeHtml,
        showToast,
        showConfirm,
        showPrompt,
        logout,
        setOrderFilter,
        getOrderFilter
    };

    applyDashboardShellLayout();
})();
