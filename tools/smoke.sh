#!/usr/bin/env bash
# Starts the API, drives a whole lesson through it, stops it again.
#
# One command, because a harness you have to remember how to run is a harness nobody runs.
# Set ANTHROPIC_API_KEY first — without it the server falls back to its deterministic pair
# and the run proves only that the plumbing is connected, not that the product works.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
port="${LUMEN_PORT:-5299}"
base="http://localhost:${port}"
log="$(mktemp -t lumen-smoke-XXXXXX.log)"

if [[ -z "${ANTHROPIC_API_KEY:-}" ]]; then
  echo "! ANTHROPIC_API_KEY is not set — this will run in degraded mode." >&2
fi

echo "→ starting the API on ${port} (log: ${log})"
ASPNETCORE_URLS="${base}" dotnet run --project "${root}/Lumen.Api" --no-launch-profile >"${log}" 2>&1 &
api=$!
trap 'kill "${api}" 2>/dev/null || true' EXIT

for _ in $(seq 60); do
  if curl -fsS "${base}/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "${api}" 2>/dev/null; then echo "The API exited before it was ready:" >&2; cat "${log}" >&2; exit 1; fi
  sleep 1
done

python3 "${root}/tools/smoke.py" --base "${base}" "$@"
