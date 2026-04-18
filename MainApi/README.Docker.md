# MainApi Docker Deployment

## One-Command Deploy on Ubuntu

If Ubuntu should not keep source code, use the delivery package flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-MainApiDockerTar.ps1
```

Then upload and unzip `artifacts/docker/mainapi-docker-package.zip` on Ubuntu, enter:

```bash
cd mainapi-docker-package/deploy/ubuntu/docker
bash deploy.sh
```

If source code is present on Ubuntu, you can still deploy from the `MainApi` directory:

```bash
bash deploy.sh
```

If MySQL is external (independent DB server), use:

```bash
bash deploy-external-mysql.sh
```

This mode only starts `mainapi` container and connects to your external MySQL.
Default template already points to:

```text
Server=47.107.154.255;Port=3306;Database=mainapi;User ID=mainapi;Password=1234;SslMode=None;AllowPublicKeyRetrieval=True;CharSet=utf8mb4;
```

The script will:

1. Check and install Docker
2. Check and install Docker Compose plugin
3. Create `.env` from `.env.example` if missing
4. Run `docker compose up -d --build`

Default database account/password in `.env.example`:

- `MYSQL_USER=test`
- `MYSQL_PASSWORD=1234`

## Manual Run

```bash
cp .env.example .env
docker compose up -d --build
```

External MySQL manual run:

```bash
cp .env.external-mysql.example .env.external-mysql
docker compose -f docker-compose.external-mysql.yml --env-file .env.external-mysql up -d --build
```

Logs:

```bash
docker compose logs -f mainapi
docker compose logs -f mysql
```

Stop:

```bash
docker compose down
```

## Endpoints

- Swagger: `http://localhost:8080/swagger`
- Health: `http://localhost:8080/api/system/status`

## Architecture

- `mainapi`: ASP.NET Core API
- `mysql`: MySQL 8.4
- Volume: `mysql_data` -> `/var/lib/mysql`
