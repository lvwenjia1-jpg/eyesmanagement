(function () {
    const ALL_BUSINESS_GROUP_ID = 0;
    const ALL_BUSINESS_GROUP_NAME = '全部业务群';
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
        orderNoInput: document.getElementById('orderNo'),
        receiverNameInput: document.getElementById('receiverName'),
        hasTrackingNumberOnly: document.getElementById('hasTrackingNumberOnly'),
        includeCancelledOrders: document.getElementById('includeCancelledOrders'),
        filterBtn: document.getElementById('filterBtn'),
        resetBtn: document.getElementById('resetBtn'),
        syncTrackingBtn: document.getElementById('syncTrackingBtn'),
        exportBtn: document.getElementById('exportBtn'),
        legendTotalCount: document.getElementById('legendTotalCount'),
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
        closeProductsDetailFooterBtn: document.getElementById('closeProductsDetailFooterBtn'),
        saveOrderBtn: document.getElementById('saveOrderBtn')
    };

    function setCurrentDate() {
        elements.currentDateEl.textContent = new Date().toLocaleDateString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit'
        });
    }

    function hasSelectedBusinessGroup() {
        return Number.isFinite(state.selectedGroupId) && state.selectedGroupId >= ALL_BUSINESS_GROUP_ID;
    }

    function setDefaultFilterTimeRange() {
        elements.startTimeInput.value = '';
        elements.endTimeInput.value = '';
        if (elements.orderNoInput) {
            elements.orderNoInput.value = '';
        }

        if (elements.receiverNameInput) {
            elements.receiverNameInput.value = '';
        }

        if (elements.hasTrackingNumberOnly) {
            elements.hasTrackingNumberOnly.checked = false;
        }

        if (elements.includeCancelledOrders) {
            elements.includeCancelledOrders.checked = true;
        }
    }

    function formatDateTimeLocal(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        return `${year}-${month}-${day}T${hours}:${minutes}`;
    }

    function getDefaultStartDateTime() {
        const date = new Date();
        date.setMonth(date.getMonth() - 1);
        date.setHours(0, 0, 0, 0);
        return formatDateTimeLocal(date);
    }

    function getDefaultEndDateTime() {
        // datetime-local 不支持 24:00，使用“次日 00:00”表示“当天 24:00”。
        const date = new Date();
        date.setHours(0, 0, 0, 0);
        date.setDate(date.getDate() + 1);
        return formatDateTimeLocal(date);
    }

    function ensureDateTimeInputDefaultOnOpen(input, defaultValueFactory) {
        if (!input) {
            return;
        }

        const applyDefaultWhenEmpty = () => {
            if (!input.value) {
                input.value = defaultValueFactory();
            }
        };

        input.addEventListener('pointerdown', applyDefaultWhenEmpty);
        input.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ' || event.key === 'ArrowDown') {
                applyDefaultWhenEmpty();
            }
        });
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

    function getSafeQuantity(item) {
        const quantity = Number(item && item.quantity);
        return Number.isFinite(quantity) && quantity > 0 ? quantity : 0;
    }

    function getRecognizedLineAmount(item) {
        const lineAmount = Number(item && item.lineAmount);
        if (Number.isFinite(lineAmount) && lineAmount > 0) {
            return lineAmount;
        }

        return getRecognizedUnitPrice(item) * getSafeQuantity(item);
    }

    function shouldShowPerItemRecognizedPrice(priceName) {
        return !isClearancePrice(priceName) && !isExtraChargePrice(priceName);
    }

    function parseGroupedPricingRule(priceName) {
        const text = getPricingDisplayText(priceName);
        if (!text || (!isClearancePrice(text) && !isExtraChargePrice(text))) {
            return null;
        }

        const match = isClearancePrice(text)
            ? text.match(/\/\s*(\d+)\s*副\s*\//)
            : text.match(/\/\s*(\d+)\s*$/);
        if (!match) {
            return null;
        }

        const requiredQuantity = Number(match[1]);
        if (!Number.isFinite(requiredQuantity) || requiredQuantity <= 1) {
            return null;
        }

        return {
            priceName: text,
            requiredQuantity
        };
    }

    function buildOrderItemDisplayEntries(items) {
        if (!Array.isArray(items) || items.length === 0) {
            return [];
        }

        const entries = [];
        const groupedEntriesByPriceName = new Map();
        items.forEach(item => {
            const groupedRule = parseGroupedPricingRule(item && item.priceName);

            if (groupedRule) {
                let entry = groupedEntriesByPriceName.get(groupedRule.priceName);
                if (!entry) {
                    entry = {
                        type: 'group',
                        priceName: groupedRule.priceName,
                        items: []
                    };
                    groupedEntriesByPriceName.set(groupedRule.priceName, entry);
                    entries.push(entry);
                }

                entry.items.push(item);
                return;
            }

            entries.push({
                type: 'single',
                item
            });
        });

        return entries;
    }

    function renderSingleOrderItemCard(item, compact) {
        const pricingText = getPricingDisplayText(item.priceName);
        const unitPrice = getRecognizedUnitPrice(item);
        const lineAmount = getRecognizedLineAmount(item);
        const quantity = getSafeQuantity(item);
        const showPerItemRecognizedPrice = shouldShowPerItemRecognizedPrice(item.priceName);

        if (compact) {
            return `
                <div class="rounded-lg border px-3 py-2 ${getItemCardClass(item.priceName)}">
                    <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0 flex-1 text-sm font-medium text-slate-800">${dashboardApp.escapeHtml(item.productName || '-')}</div>
                        ${lineAmount > 0 ? `<div class="shrink-0 text-xs font-medium text-emerald-700">${lineAmount} 元</div>` : ''}
                    </div>
                    <div class="mt-1 text-xs text-slate-500">${dashboardApp.escapeHtml(item.productCode || '-')} \u00b7 x ${quantity}</div>
                    ${unitPrice > 0 && pricingText ? `<div class="mt-2 inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${getPricingBadgeClass(item.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>` : ''}
                </div>
            `;
        }

        return `
            <div class="rounded-xl border p-4 shadow-sm ${getItemCardClass(item.priceName)}">
                <div class="flex items-start justify-between gap-4">
                    <div class="min-w-0 flex-1">
                        <div class="text-sm font-semibold text-slate-900">${dashboardApp.escapeHtml(item.productName || '-')}</div>
                        <div class="mt-1 text-xs text-slate-500">\u7f16\u7801\uff1a${dashboardApp.escapeHtml(item.productCode || '-')}</div>
                        ${unitPrice > 0 && pricingText ? `<div class="mt-2 inline-flex rounded-md px-2 py-1 text-xs font-medium ${getPricingBadgeClass(item.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>` : ''}
                        ${unitPrice > 0 && showPerItemRecognizedPrice ? `<div class="mt-2 text-xs font-medium text-emerald-700">\u8bc6\u522b\u4ef7\u683c\uff1a${unitPrice} \u5143</div>` : ''}
                    </div>
                    <div class="shrink-0 text-right">
                        ${lineAmount > 0 ? `<div class="text-sm font-semibold text-emerald-700">${lineAmount} 元</div>` : ''}
                        <div class="mt-1 rounded-lg bg-white/80 px-3 py-1 text-sm font-semibold text-slate-700">x ${quantity}</div>
                    </div>
                </div>
            </div>
        `;
    }

    function renderGroupedOrderItemCard(entry, compact) {
        const items = Array.isArray(entry.items) ? entry.items : [];
        const pricingText = getPricingDisplayText(entry.priceName);
        const totalQuantity = items.reduce((sum, item) => sum + getSafeQuantity(item), 0);
        const totalAmount = items.reduce((sum, item) => sum + getRecognizedLineAmount(item), 0);
        const visibleItems = compact ? items.slice(0, 3) : items;
        const hiddenCount = compact ? Math.max(0, items.length - visibleItems.length) : 0;
        const lineClass = compact
            ? 'flex items-center justify-between gap-3 rounded-md bg-white/70 px-2 py-1 text-xs'
            : 'flex items-center justify-between gap-3 rounded-lg bg-white/75 px-3 py-2 text-sm';

        const itemsHtml = visibleItems.map(item => {
            const quantity = getSafeQuantity(item);
            const unitPrice = getRecognizedUnitPrice(item);
            const showPerItemRecognizedPrice = shouldShowPerItemRecognizedPrice(entry.priceName);

            return `
                <div class="${lineClass}">
                    <div class="min-w-0 flex-1">
                        <div class="truncate font-medium text-slate-800">${dashboardApp.escapeHtml(item.productName || '-')}</div>
                        <div class="truncate text-slate-500">${dashboardApp.escapeHtml(item.productCode || '-')}</div>
                    </div>
                    <div class="shrink-0 text-right">
                        <div class="font-medium text-slate-700">x ${quantity}</div>
                        ${unitPrice > 0 && showPerItemRecognizedPrice ? `<div class="text-emerald-700">${unitPrice} \u5143</div>` : ''}
                    </div>
                </div>
            `;
        }).join('');

        if (compact) {
            return `
                <div class="rounded-lg border px-3 py-2 ${getItemCardClass(entry.priceName)}">
                    <div class="flex items-start justify-between gap-3">
                        <div class="min-w-0 flex-1">
                            <div class="inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${getPricingBadgeClass(entry.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>
                            <div class="mt-1 text-xs text-slate-500">${items.length} \u9879 \u00b7 x ${totalQuantity}</div>
                        </div>
                        ${totalAmount > 0 ? `<div class="shrink-0 text-xs font-medium text-emerald-700">${totalAmount} \u5143</div>` : ''}
                    </div>
                    <div class="mt-2 space-y-1">
                        ${itemsHtml}
                    </div>
                    ${hiddenCount > 0 ? `<div class="mt-2 text-xs text-slate-500">\u8fd8\u6709 ${hiddenCount} \u9879\uff0c\u70b9\u51fb\u67e5\u770b\u5168\u90e8</div>` : ''}
                </div>
            `;
        }

        return `
            <div class="rounded-xl border p-4 shadow-sm ${getItemCardClass(entry.priceName)}">
                <div class="flex items-start justify-between gap-4">
                    <div class="min-w-0 flex-1">
                        <div class="inline-flex rounded-md px-2 py-1 text-xs font-medium ${getPricingBadgeClass(entry.priceName)}">${dashboardApp.escapeHtml(pricingText)}</div>
                        <div class="mt-2 text-sm font-medium text-slate-700">${items.length} \u9879\uff0c\u603b\u6570\u91cf x ${totalQuantity}</div>
                        ${totalAmount > 0 ? `<div class="mt-1 text-xs font-medium text-emerald-700">\u5408\u8ba1\u8bc6\u522b\u4ef7\uff1a${totalAmount} \u5143</div>` : ''}
                    </div>
                    <div class="rounded-lg bg-white/80 px-3 py-1 text-sm font-semibold text-slate-700">${items.length} \u9879</div>
                </div>
                <div class="mt-3 space-y-2">
                    ${itemsHtml}
                </div>
            </div>
        `;
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

    function renderOrderItemsHtml(items) {
        if (!items || items.length === 0) {
            return '<div class="rounded-lg border border-dashed border-slate-300 bg-white px-4 py-6 text-sm text-slate-500">\u6682\u65e0\u5546\u54c1\u4fe1\u606f</div>';
        }

        return buildOrderItemDisplayEntries(items)
            .map(entry => entry.type === 'group'
                ? renderGroupedOrderItemCard(entry, false)
                : renderSingleOrderItemCard(entry.item, false))
            .join('');
    }

    function buildProductsPreview(order) {
        const items = Array.isArray(order.items) ? order.items : [];
        if (items.length === 0) {
            return '<div class="text-sm text-slate-500">\u6682\u65e0\u5546\u54c1\u4fe1\u606f</div>';
        }

        const displayEntries = buildOrderItemDisplayEntries(items);
        const previewEntries = displayEntries.slice(0, PREVIEW_ITEM_COUNT);
        const hasCompactOverflow = displayEntries.some(entry => entry.type === 'group' && entry.items.length > 3);
        const cards = previewEntries.map(entry => entry.type === 'group'
            ? renderGroupedOrderItemCard(entry, true)
            : renderSingleOrderItemCard(entry.item, true)).join('');

        const moreButton = displayEntries.length > PREVIEW_ITEM_COUNT || hasCompactOverflow
            ? `
                <button type="button" class="view-products-detail inline-flex items-center rounded-md bg-slate-100 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-200" data-id="${order.id}">
                    \u5171 ${items.length} \u6761\uff0c\u70b9\u51fb\u67e5\u770b\u5168\u90e8
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

    function setOrderInputReadOnly(inputId, isReadOnly) {
        const input = document.getElementById(inputId);
        input.readOnly = isReadOnly;
        input.classList.toggle('readonly-field', isReadOnly);
        input.classList.toggle('editable-field', !isReadOnly);
    }

    function setOrderEditability(hasTrackingNumber) {
        setOrderInputReadOnly('editRecipient', hasTrackingNumber);
        setOrderInputReadOnly('editAddress', hasTrackingNumber);
        setOrderInputReadOnly('editAmount', hasTrackingNumber);
        setOrderInputReadOnly('editReceiverMobile', hasTrackingNumber);
        setOrderInputReadOnly('editTrackingNumber', true);
        elements.saveOrderBtn.disabled = hasTrackingNumber;
        elements.saveOrderBtn.classList.toggle('opacity-60', hasTrackingNumber);
        elements.saveOrderBtn.classList.toggle('cursor-not-allowed', hasTrackingNumber);
    }

    function openOrderModal(order) {
        const items = Array.isArray(order.items) ? order.items : [];
        document.getElementById('orderId').value = String(order.id);
        document.getElementById('editOrderId').value = order.orderNo || '-';
        document.getElementById('editUploader').value = order.uploaderLoginName || '-';
        document.getElementById('editRecipient').value = order.receiverName || '-';
        document.getElementById('editAddress').value = order.receiverAddress || '';
        document.getElementById('editAmount').value = String(order.amount ?? 0);
        document.getElementById('editTrackingNumber').value = order.trackingNumber || '';
        document.getElementById('editReceiverMobile').value = order.receiverMobile || '';
        setOrderEditability(Boolean(String(order.trackingNumber || '').trim()));

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

        if (elements.legendTotalCount) {
            elements.legendTotalCount.textContent = `共 ${state.totalCount} 条记录`;
        }

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

        const pageItems = [];
        if (totalPages <= 7) {
            for (let page = 1; page <= totalPages; page += 1) {
                pageItems.push(page);
            }
        } else if (state.currentPage <= 4) {
            pageItems.push(1, 2, 3, 4, 5, 'ellipsis-right', totalPages);
        } else if (state.currentPage >= totalPages - 3) {
            pageItems.push(1, 'ellipsis-left', totalPages - 4, totalPages - 3, totalPages - 2, totalPages - 1, totalPages);
        } else {
            pageItems.push(1, 'ellipsis-left', state.currentPage - 1, state.currentPage, state.currentPage + 1, 'ellipsis-right', totalPages);
        }

        const pageButtons = pageItems.map(page => {
            if (typeof page !== 'number') {
                return '<span class="inline-flex h-10 w-8 items-center justify-center text-sm text-gray-500">...</span>';
            }

            const activeClass = page === state.currentPage
                ? 'bg-primary text-white border-primary'
                : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50';
            return `
                <button type="button" class="page-btn relative inline-flex h-10 w-10 items-center justify-center border px-2 text-sm font-medium ${activeClass}" data-page="${page}">
                    ${page}
                </button>
            `;
        });

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
                </td>
                <td class="px-6 py-4">
                    <div class="max-w-[11rem] break-all text-sm text-gray-500">${dashboardApp.escapeHtml(order.trackingNumber || '-')}</div>
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

    function resolveOrderFilter() {
        const storedFilter = dashboardApp.getOrderFilter();
        const query = new URLSearchParams(window.location.search);
        const queryBusinessGroupId = query.get('businessGroupId');
        const queryBusinessGroupName = query.get('businessGroupName');

        if (queryBusinessGroupId !== null) {
            const businessGroupId = Number(queryBusinessGroupId);
            const filter = {
                businessGroupId,
                businessGroupName: businessGroupId === ALL_BUSINESS_GROUP_ID
                    ? ALL_BUSINESS_GROUP_NAME
                    : (queryBusinessGroupName || storedFilter?.businessGroupName || '')
            };

            dashboardApp.setOrderFilter(filter);
            return filter;
        }

        if (Number(storedFilter?.businessGroupId) === ALL_BUSINESS_GROUP_ID) {
            const normalizedFilter = {
                businessGroupId: ALL_BUSINESS_GROUP_ID,
                businessGroupName: ALL_BUSINESS_GROUP_NAME
            };
            dashboardApp.setOrderFilter(normalizedFilter);
            return normalizedFilter;
        }

        return storedFilter;
    }

    async function loadGroupInfo() {
        const filter = resolveOrderFilter();
        const businessGroupId = Number(filter?.businessGroupId);
        if (!filter || Number.isNaN(businessGroupId) || businessGroupId < ALL_BUSINESS_GROUP_ID) {
            window.location.href = 'business.html';
            return false;
        }

        state.selectedGroupId = businessGroupId;
        state.selectedGroupName = state.selectedGroupId === ALL_BUSINESS_GROUP_ID
            ? ALL_BUSINESS_GROUP_NAME
            : (filter.businessGroupName || '订单详情');
        elements.groupTitle.textContent = `${state.selectedGroupName} · 订单详情`;

        if (state.selectedGroupId === ALL_BUSINESS_GROUP_ID) {
            state.selectedGroupBalance = 0;
            return true;
        }

        try {
            const group = await dashboardApp.apiRequest(`/api/business-groups/${state.selectedGroupId}`);
            state.selectedGroupBalance = Number(group?.balance || 0);
        } catch {
            state.selectedGroupBalance = 0;
        }

        return true;
    }

    async function loadOrders() {
        if (!hasSelectedBusinessGroup()) {
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
        const orderNo = String(elements.orderNoInput?.value || '').trim();
        const receiverName = String(elements.receiverNameInput?.value || '').trim();
        if (startTime) {
            query.set('startTime', startTime);
        }

        if (endTime) {
            query.set('endTime', endTime);
        }

        if (orderNo) {
            query.set('orderNo', orderNo);
        }

        if (receiverName) {
            query.set('receiverName', receiverName);
        }

        if (elements.hasTrackingNumberOnly?.checked) {
            query.set('hasTrackingNumber', 'true');
        }

        query.set('includeCancelledOrders', String(elements.includeCancelledOrders?.checked !== false));

        const response = await dashboardApp.apiRequest(`/api/business-groups/${state.selectedGroupId}/orders?${query.toString()}`);
        state.orders = Array.isArray(response.items) ? response.items : [];
        state.totalCount = Number(response.totalCount || 0);
        renderOrders();
        renderSortIndicators();
    }

    async function updateOrder(orderId, amount, receiverName, receiverAddress, receiverMobile, trackingNumber) {
        try {
            await dashboardApp.apiRequest(`/api/orders/${orderId}`, {
                method: 'PUT',
                body: {
                    amount,
                    receiverName,
                    receiverAddress,
                    receiverMobile,
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
        const receiverName = document.getElementById('editRecipient').value.trim();
        const receiverAddress = document.getElementById('editAddress').value.trim();
        const receiverMobile = document.getElementById('editReceiverMobile').value.trim();
        const trackingNumber = document.getElementById('editTrackingNumber').value.trim();

        if (!Number.isFinite(amount) || amount < 0) {
            await dashboardApp.showToast('请输入有效的订单金额。', 'error');
            return;
        }

        if (!receiverName) {
            await dashboardApp.showToast('收件人不能为空。', 'error');
            return;
        }

        if (!receiverAddress) {
            await dashboardApp.showToast('收货地址不能为空。', 'error');
            return;
        }

        if (!receiverMobile) {
            await dashboardApp.showToast('手机号不能为空。', 'error');
            return;
        }

        await updateOrder(orderId, amount, receiverName, receiverAddress, receiverMobile, trackingNumber);
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
        if (!hasSelectedBusinessGroup() || state.isSyncingTrackingNumbers) {
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
        if (!hasSelectedBusinessGroup()) {
            return;
        }

        const query = new URLSearchParams({
            businessGroupId: String(state.selectedGroupId)
        });

        const startTime = parseDateTimeLocalToIso(elements.startTimeInput.value);
        const endTime = parseDateTimeLocalToIso(elements.endTimeInput.value);
        const orderNo = String(elements.orderNoInput?.value || '').trim();
        const receiverName = String(elements.receiverNameInput?.value || '').trim();
        if (startTime) {
            query.set('startTime', startTime);
        }

        if (endTime) {
            query.set('endTime', endTime);
        }

        if (orderNo) {
            query.set('orderNo', orderNo);
        }

        if (receiverName) {
            query.set('receiverName', receiverName);
        }

        if (elements.hasTrackingNumberOnly?.checked) {
            query.set('hasTrackingNumber', 'true');
        }

        query.set('includeCancelledOrders', String(elements.includeCancelledOrders?.checked !== false));

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
            const exportFileNameHeader = response.headers.get('x-export-file-name') || '';
            const contentDisposition = response.headers.get('content-disposition') || '';
            const matchedFileName = contentDisposition.match(/filename\*=UTF-8''([^;]+)|filename=\"?([^\";]+)\"?/i);
            const encodedServerFileName = exportFileNameHeader || (matchedFileName && (matchedFileName[1] || matchedFileName[2])) || '';
            const serverFileName = decodeURIComponent(encodedServerFileName).trim();
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
        ensureDateTimeInputDefaultOnOpen(elements.startTimeInput, getDefaultStartDateTime);
        ensureDateTimeInputDefaultOnOpen(elements.endTimeInput, getDefaultEndDateTime);

        elements.backBtn.addEventListener('click', () => {
            window.location.href = 'business.html';
        });

        elements.filterBtn.addEventListener('click', () => {
            handleFilter();
        });

        elements.orderNoInput?.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                handleFilter();
            }
        });

        elements.receiverNameInput?.addEventListener('keydown', event => {
            if (event.key === 'Enter') {
                event.preventDefault();
                handleFilter();
            }
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
