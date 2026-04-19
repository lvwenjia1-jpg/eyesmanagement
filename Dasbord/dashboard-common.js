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

            // In same-origin deployment, stale debug base URLs are the #1 integration trap.
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

    function showToast(message, type) {
        const toast = document.createElement('div');
        const background = type === 'error' ? 'bg-red-500' : 'bg-green-500';
        toast.className = `fixed bottom-4 right-4 ${background} text-white px-4 py-2 rounded-md shadow-lg z-50 transition-all opacity-0`;
        toast.textContent = message;
        document.body.appendChild(toast);

        setTimeout(() => {
            toast.classList.remove('opacity-0');
            toast.classList.add('opacity-100');
        }, 10);

        setTimeout(() => {
            toast.classList.remove('opacity-100');
            toast.classList.add('opacity-0');
            setTimeout(() => toast.remove(), 250);
        }, 2500);
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
        logout,
        setOrderFilter,
        getOrderFilter
    };
})();
