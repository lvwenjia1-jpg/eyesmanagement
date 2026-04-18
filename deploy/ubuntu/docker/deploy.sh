#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

run_root() {
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  else
    sudo "$@"
  fi
}

echo "[1/5] Checking Docker..."
if ! command -v docker >/dev/null 2>&1; then
  echo "Docker not found. Installing..."
  if ! command -v curl >/dev/null 2>&1; then
    run_root apt-get update
    run_root apt-get install -y curl
  fi
  tmp_script="/tmp/get-docker.sh"
  curl -fsSL https://get.docker.com -o "$tmp_script"
  run_root sh "$tmp_script"
  rm -f "$tmp_script"
fi

echo "[2/5] Checking Docker Compose plugin..."
if ! run_root docker compose version >/dev/null 2>&1; then
  echo "Docker Compose plugin not found. Installing..."
  run_root apt-get update
  run_root apt-get install -y docker-compose-plugin
fi

if command -v systemctl >/dev/null 2>&1; then
  run_root systemctl enable docker >/dev/null 2>&1 || true
  run_root systemctl start docker >/dev/null 2>&1 || true
fi

echo "[3/5] Preparing environment..."
if [[ ! -f "mainapi.docker.env" ]]; then
  cp "mainapi.docker.env.example" "mainapi.docker.env"
  echo "mainapi.docker.env created from example"
fi

IMAGE_TAR=""
if [[ -n "${1:-}" ]]; then
  IMAGE_TAR="$1"
elif [[ -f "./mainapi-latest.tar" ]]; then
  IMAGE_TAR="./mainapi-latest.tar"
fi

if [[ -n "$IMAGE_TAR" ]]; then
  echo "Image tar detected: $IMAGE_TAR"
  if [[ ! -f "$IMAGE_TAR" ]]; then
    echo "Image tar not found: $IMAGE_TAR"
    exit 1
  fi

  echo "Loading image: $IMAGE_TAR"
  run_root docker load -i "$IMAGE_TAR"
else
  echo "No image tar specified."
  echo "Will use existing local image or pull from registry."
fi

echo "[4/5] Starting containers..."
run_root docker compose up -d

echo "[5/5] Deployment finished."
run_root docker compose ps
echo
echo "Main API:  http://SERVER_IP:${MAINAPI_PORT:-8080}/api/system/status"
echo "Swagger:   http://SERVER_IP:${MAINAPI_PORT:-8080}/swagger"
