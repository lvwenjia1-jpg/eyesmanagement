# Dashboard acceptance report

## Run time
- Date: 2026-04-19
- Target: `http://47.107.154.255:98`
- Mode: same-origin deployment (`/dashboard/` + `/api/...`)

## Passed checks
- Auth login API works with empty token allowed.
- Auth me API works with explicit `loginName`.
- Users list API works.
- Users create/delete CRUD path works.
- Machines list API works.
- Machines create/delete CRUD path works.
- Business groups list/detail API works.
- Orders list API works.

## Safety-skipped checks
- Business group balance update endpoint was not executed to avoid mutating production finance data.
- Order update endpoint was not executed to avoid mutating production order state.

## Manual checks required after deployment
1. Open `http://47.107.154.255:98/dashboard/`.
2. Login with a valid account.
3. Verify refresh does not redirect to login unexpectedly.
4. Run Users CRUD from UI.
5. Run Machines CRUD from UI.
6. Update one business group balance using intended test value.
7. Update one order amount/tracking and verify persistence.
8. Verify export action completes.

## Deployment blockers
- Docker build validation is not completed in this workspace because Docker daemon is not running locally.
- `docker compose build mainapi` failed with docker engine connection error.
