# MainApi Ubuntu Docker (No Source on Server)

This deployment flow does **not** require source code on Ubuntu.

## 1. Build Delivery Package on Build Machine

From repository root (Windows PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-MainApiDockerTar.ps1
```

Output zip:

```text
artifacts/docker/mainapi-docker-package.zip
```

## 2. Upload to Ubuntu

Upload only this zip to Ubuntu, then unzip:

```bash
unzip mainapi-docker-package.zip -d mainapi-docker-package
cd mainapi-docker-package/deploy/ubuntu/docker
```

No repository source code is needed on Ubuntu.

## 3. One-Command Deploy

### Option A: External MySQL (Recommended for your setup)

```bash
cp mainapi.external-mysql.env.example mainapi.external-mysql.env
nano mainapi.external-mysql.env
bash deploy-external-mysql.sh
```

This mode starts only `mainapi` container and uses your standalone MySQL.
Default DB is already set to:

```text
Server=47.107.154.255;Port=3306;Database=mainapi;User ID=mainapi;Password=1234;SslMode=None;AllowPublicKeyRetrieval=True;CharSet=utf8mb4;
```

### Option B: MainApi + MySQL on same Ubuntu

```bash
bash deploy.sh
```

`deploy.sh` will:

1. Check/install Docker
2. Check/install Docker Compose plugin
3. Create `mainapi.docker.env` from example if missing
4. Auto-load `mainapi-latest.tar` in the current directory
5. Start containers (`mainapi` + `mysql`)

## 4. Configure Environment (Optional)

Before deployment (Option B), edit:

```bash
nano mainapi.docker.env
```

At minimum, change:

- `Jwt__SigningKey`
- `BootstrapAdmin__Password`

For external MySQL mode (Option A), edit:

```bash
nano mainapi.external-mysql.env
```

At minimum, change:

- `Jwt__SigningKey`
- `BootstrapAdmin__Password`

## 5. Verify

```bash
docker compose ps
docker compose logs -f mainapi
curl http://127.0.0.1:98/api/system/status
curl http://127.0.0.1:98/swagger
```
