# Dashboard <-> MainApi contract freeze

## Baseline
- Source of truth: `http://47.107.154.255:98/swagger/index.html`
- JSON spec: `http://47.107.154.255:98/swagger/v1/swagger.json`
- Deployment mode: same origin (`http://47.107.154.255:98/dashboard/` + `/api/...`)
- Boundary: do not change MainApi business/controller behavior

## Cross-origin and proxy decision
- Same host + same port + same protocol means same origin in browser.
- In this mode, Dashboard requests `/api/...` do not require CORS.
- No frontend dev proxy is required in production.
- Optional reverse proxy (Nginx) is only a traffic entry concern; route both `/dashboard/` and `/api/` to MainApi.

## Auth contract (highest priority)
### POST `/api/auth/login`
- Used by: `Dasbord/login.html`
- Request body:
```json
{
  "loginName": "admin",
  "password": "123456"
}
```
- Success response (`200`):
```json
{
  "token": "",
  "expiresAtUtc": "2026-04-18T16:06:54.2159839Z",
  "user": {
    "id": 1,
    "loginName": "admin",
    "erpId": "ERP001",
    "role": "admin",
    "isActive": true,
    "createdAtUtc": "2026-04-10T08:50:05Z"
  }
}
```
- Notes:
- `token` may be empty string.
- Frontend must not use token presence as the login-state source of truth.

### GET `/api/auth/me?loginName=...`
- Used by: `Dasbord/dashboard-common.js`, `Dasbord/login.html`
- Required query: `loginName`
- Success response (`200`): `UserResponse`
- Typical error:
- Missing `loginName` returns `400` with validation error payload.

## Users contract
### GET `/api/users`
- Used by: `Dasbord/index-api.js`
- Query:
- `keyword` (optional)
- `role` (optional)
- `isActive` (optional)
- `pageNumber` (required in frontend flow)
- `pageSize` (required in frontend flow)
- Success (`200`): `UserResponsePagedResponse`

### POST `/api/users`
- Used by: `Dasbord/index-api.js`
- Body required fields: `loginName`, `password`, `erpId`
- Success (`200`): `UserResponse`
- Typical error:
- Duplicate `loginName` -> `400` validation problem

### PUT `/api/users/{id}`
- Used by: `Dasbord/index-api.js`
- Body required fields: `loginName`, `erpId`
- Optional fields: `password`, `isActive`
- Success (`200`): `UserResponse`

### DELETE `/api/users/{id}`
- Used by: `Dasbord/index-api.js`
- Success: `204 No Content`

## Machines contract
### GET `/api/machines`
- Used by: `Dasbord/machine-codes-api.js`
- Query:
- `keyword` (optional)
- `isActive` (optional; frontend defaults to `true`)
- `pageNumber`, `pageSize`
- Success (`200`): `MachineResponsePagedResponse`

### POST `/api/machines`
- Used by: `Dasbord/machine-codes-api.js`
- Body required fields: `code`, `description`
- Success (`200`): `MachineResponse`
- Typical error:
- Duplicate `code` -> `400` validation problem

### PUT `/api/machines/{id}`
- Used by: `Dasbord/machine-codes-api.js`
- Body required fields: `code`, `description`
- Optional fields: `isActive`
- Success (`200`): `MachineResponse`

### DELETE `/api/machines/{id}`
- Used by: `Dasbord/machine-codes-api.js`
- Success: `204 No Content`

## Business groups contract
### GET `/api/business-groups`
- Used by: `Dasbord/business-api.js`
- Query: `keyword` (optional), `pageNumber`, `pageSize`
- Success (`200`): `BusinessGroupResponsePagedResponse`

### GET `/api/business-groups/{id}`
- Used by: `Dasbord/orders-api.js`
- Success (`200`): `BusinessGroupResponse`

### PUT `/api/business-groups/{id}/balance`
- Used by: `Dasbord/business-api.js`
- Body:
```json
{
  "balance": 1000
}
```
- Success (`200`): `BusinessGroupResponse`

## Orders contract
### GET `/api/business-groups/{businessGroupId}/orders`
- Used by: `Dasbord/orders-api.js`
- Query:
- `startTime` (optional, ISO datetime)
- `endTime` (optional, ISO datetime)
- `pageNumber`, `pageSize`
- Success (`200`): `DashboardOrderSummaryResponsePagedResponse`
- Notes:
- Swagger shows `StartTime`/`EndTime`; frontend sends `startTime`/`endTime`.
- ASP.NET Core query binding is case-insensitive, so this is compatible.

### PUT `/api/orders/{id}`
- Used by: `Dasbord/orders-api.js`
- Body:
```json
{
  "amount": 88.5,
  "trackingNumber": "SF123456789CN"
}
```
- Success (`200`): `DashboardOrderDetailResponse`

## Required frontend adaptations (completed)
- Session guard uses `currentUser.loginName` instead of token.
- Keep Authorization header behavior, but not as a login prerequisite.
- Add a dedicated `getCurrentUserProfile()` that calls `/api/auth/me?loginName=...`.

## Acceptance path
1. Login with valid account.
2. Refresh `index.html` and verify no redirect loop.
3. Run Users CRUD.
4. Run Machines CRUD.
5. Run BusinessGroup balance update.
6. Run Orders filter/update/export.
7. Confirm delete actions handle `204` correctly.
