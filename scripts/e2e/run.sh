#!/usr/bin/env bash
set -euo pipefail

: "${E2E_DATABASE_NAME:?Set E2E_DATABASE_NAME to a dedicated database containing _e2e}"
if [[ "${E2E_DATABASE_NAME,,}" != *"_e2e"* ]]; then
  echo "Refusing E2E run: database name must contain _e2e (got: $E2E_DATABASE_NAME)" >&2
  exit 2
fi
: "${E2E_DB_CONNECTION_STRING:?Set E2E_DB_CONNECTION_STRING for the same dedicated _e2e database}"
: "${E2E_API_BASE_URL:?Set E2E_API_BASE_URL to the already-running API}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
dotnet run --project backend/tests/Smes.E2E/Smes.E2E.csproj --configuration Release
