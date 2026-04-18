#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PUBLISH_DIR="${1:-}"
IMAGE_TAG="${MAINAPI_IMAGE_TAG:-eyesmanagement/mainapi:latest}"
DOCKERFILE_PATH="$SCRIPT_DIR/Dockerfile.publish"

if [[ -z "$PUBLISH_DIR" ]]; then
  echo "Usage: ./run-from-publish.sh /path/to/publish/mainapi"
  exit 1
fi

if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "Publish directory not found: $PUBLISH_DIR"
  exit 1
fi

if [[ ! -f "$PUBLISH_DIR/MainApi.dll" ]]; then
  echo "MainApi.dll was not found in: $PUBLISH_DIR"
  echo "Run: dotnet publish MainApi/MainApi.csproj -c Release -o publish/mainapi"
  exit 1
fi

if [[ ! -f "$DOCKERFILE_PATH" ]]; then
  echo "Dockerfile.publish was not found in: $DOCKERFILE_PATH"
  echo "Upload deploy/ubuntu/docker/Dockerfile.publish together with this script."
  exit 1
fi

if [[ ! -f "mainapi.docker.env" ]]; then
  cp "mainapi.docker.env.example" "mainapi.docker.env"
  echo "Created mainapi.docker.env. Update secrets first, then rerun."
  exit 1
fi

echo "Building image from publish directory: $PUBLISH_DIR"
docker build -f "$DOCKERFILE_PATH" -t "$IMAGE_TAG" "$PUBLISH_DIR"

echo "Starting mainapi container..."
docker compose up -d
docker compose ps

echo
echo "Done."
echo "Check status:"
echo "  curl http://127.0.0.1:${MAINAPI_PORT:-8080}/api/system/status"
echo "  curl http://127.0.0.1:${MAINAPI_PORT:-8080}/swagger"
