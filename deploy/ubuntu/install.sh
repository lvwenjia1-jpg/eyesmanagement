#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="mainapi"
APP_DIR="/opt/eyesmanagement/mainapi"
ENV_DIR="/etc/eyesmanagement"
ENV_FILE="$ENV_DIR/mainapi.env"
SYSTEMD_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
NGINX_SITE="/etc/nginx/sites-available/eyesmanagement.conf"
NGINX_SITE_LINK="/etc/nginx/sites-enabled/eyesmanagement.conf"
NGINX_MAP="/etc/nginx/conf.d/nginx-upgrade-map.conf"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PUBLISH_DIR="${1:-$REPO_DIR/publish/mainapi}"
SERVER_NAME="${2:-_}"
RUN_AS_USER="${RUN_AS_USER:-www-data}"
RUN_AS_GROUP="${RUN_AS_GROUP:-www-data}"

if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "发布目录不存在: $PUBLISH_DIR"
  echo "请先执行: dotnet publish MainApi/MainApi.csproj -c Release -o publish/mainapi"
  exit 1
fi

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "缺少命令: $1"
    exit 1
  fi
}

require_cmd systemctl
require_cmd nginx
require_cmd install
require_cmd rsync

sudo mkdir -p "$APP_DIR" "$ENV_DIR"
sudo rsync -av --delete "$PUBLISH_DIR"/ "$APP_DIR"/

if [[ ! -f "$ENV_FILE" ]]; then
  sudo install -m 640 "$SCRIPT_DIR/mainapi.env.example" "$ENV_FILE"
  echo "已创建环境变量文件: $ENV_FILE"
  echo "请先编辑其中的密钥和管理员密码，再重新运行本脚本。"
fi

TMP_SERVICE="$(mktemp)"
sed \
  -e "s|^User=.*|User=${RUN_AS_USER}|" \
  -e "s|^Group=.*|Group=${RUN_AS_GROUP}|" \
  "$SCRIPT_DIR/mainapi.service" > "$TMP_SERVICE"
sudo install -m 644 "$TMP_SERVICE" "$SYSTEMD_FILE"
rm -f "$TMP_SERVICE"

TMP_NGINX="$(mktemp)"
sed "s|server_name _;|server_name ${SERVER_NAME};|" "$SCRIPT_DIR/nginx-mainapi.conf" > "$TMP_NGINX"
sudo install -m 644 "$TMP_NGINX" "$NGINX_SITE"
rm -f "$TMP_NGINX"

sudo install -m 644 "$SCRIPT_DIR/nginx-upgrade-map.conf" "$NGINX_MAP"
sudo ln -sf "$NGINX_SITE" "$NGINX_SITE_LINK"

sudo systemctl daemon-reload
sudo systemctl enable "$SERVICE_NAME"
sudo systemctl restart "$SERVICE_NAME"
sudo nginx -t
sudo systemctl reload nginx

cat <<EOF
部署完成。
- 应用目录: $APP_DIR
- 环境变量: $ENV_FILE
- systemd 服务: $SYSTEMD_FILE
- nginx 配置: $NGINX_SITE
- 访问地址: http://${SERVER_NAME}

如需 HTTPS，可执行：
  sudo apt-get install -y certbot python3-certbot-nginx
  sudo certbot --nginx -d your-domain.com -d www.your-domain.com
EOF
