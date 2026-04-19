# MainApi Docker (Simple)

This project now uses a minimal Ubuntu Docker deployment flow:

1. Edit MySQL connection in `appsettings.json`:

```json
"ConnectionStrings": {
  "MainDb": "Server=47.107.154.255;Port=3306;Database=mainapi;User ID=mainapi;Password=1234;SslMode=None;AllowPublicKeyRetrieval=True;CharSet=utf8mb4;"
}
```

2. Start container:

```bash
bash deploy.sh
```

3. Check service:

```bash
docker compose ps
docker compose logs -f mainapi
```

Endpoints:

- `http://SERVER_IP:98/api/system/status`
- `http://SERVER_IP:98/swagger`
- `http://SERVER_IP:98/dashboard/`
