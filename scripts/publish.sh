#!/usr/bin/env bash
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: ./scripts/publish.sh <rid>"
  exit 1
fi

RID="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="${SCRIPT_DIR}/../artifacts/publish/${RID}"

dotnet publish   "${SCRIPT_DIR}/../src/Autofire.App/Autofire.App.csproj"   -c Release   -r "${RID}"   --self-contained false   -p:PublishSingleFile=true   -p:DebugType=embedded   -o "${PUBLISH_DIR}"
