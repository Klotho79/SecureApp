#!/bin/bash
# Runs on the Pi HOST (not inside the relay container) — triggered by secureapp-deploy.path
# whenever the relay's /admin/deploy endpoint drops a marker file into its mounted /data volume.
# Real `docker compose` access lives only here, never inside the container itself — see the
# comment above the /admin/deploy handler in Program.cs for why.
set -euo pipefail

DEPLOY_DIR="/home/dvorakv1/SecureApp/relay/SecureApp.Relay"
MARKER="$DEPLOY_DIR/data/deploy-requested"
LOG="$DEPLOY_DIR/data/deploy.log"

if [ ! -f "$MARKER" ]; then
    exit 0
fi
rm -f "$MARKER"

{
    echo "=== Deploy triggered at $(date -u +%FT%TZ) ==="
    cd "$DEPLOY_DIR"
    docker compose build
    docker compose up -d
    echo "=== Deploy finished at $(date -u +%FT%TZ) ==="
} >> "$LOG" 2>&1
