# Dashboard integration checklist (same host/port as MainApi)

## Scope and boundary
- Keep MainApi business/controller logic unchanged.
- Keep Dashboard page workflow unchanged.
- Only adjust frontend API adapter, auth session handling, and deployment config.

## Effective deployment mode
- Deploy Dashboard static files and MainApi under the same origin.
- Access Dashboard from `http://47.107.154.255:98/dashboard/`.
- Keep frontend requests as relative paths (`/api/...`).

## CORS and proxy notes
- Same origin mode does not require CORS for browser requests.
- MainApi currently has permissive CORS enabled; this is harmless in same origin mode.
- Reverse proxy is optional in this mode.
- If you still use Nginx in front, route both `/dashboard/` and `/api/` to the same MainApi upstream.
- If previous debugging left an old `dashboard.apiBaseUrl` in browser local storage, clear it before acceptance testing.

## Auth contract alignment
- `/api/auth/login` may return an empty `token`.
- `/api/auth/me` requires query `loginName`.
- Frontend session validity should rely on `currentUser.loginName`, not token presence.
- Existing `Authorization` header behavior can be kept, but must not be required for auth success.

## API contract map used by Dashboard
- `POST /api/auth/login`: login with `loginName`, `password`.
- `GET /api/auth/me?loginName=...`: validate current session user.
- `GET/POST /api/users`, `PUT/DELETE /api/users/{id}`.
- `GET/POST /api/machines`, `PUT/DELETE /api/machines/{id}`.
- `GET /api/business-groups`, `GET /api/business-groups/{id}`, `PUT /api/business-groups/{id}/balance`.
- `GET /api/business-groups/{businessGroupId}/orders`, `PUT /api/orders/{id}`.

## Acceptance flow
1. Login.
2. Users CRUD.
3. Machines CRUD.
4. Business group balance update.
5. Order query with time filter.
6. Order amount/tracking update.
7. Export.

## Pass criteria
- No page-level business flow changes.
- All dashboard requests hit MainApi under the same origin.
- Login and page guard work even when token is empty.
- Delete operations correctly handle `204 No Content`.
