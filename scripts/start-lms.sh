#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
export COMPOSE_DISABLE_ENV_FILE=1
env_file=/dev/null
if (( $# > 0 )); then
  if [[ $# != 2 || $1 != --env-file || ! -f $2 || ! -r $2 ]]; then
    echo 'Usage: scripts/start-lms.sh [--env-file PATH]' >&2
    exit 2
  fi
  env_file=$2
fi
compose=(docker compose --env-file "$env_file" -f docker-compose.yml -p toeic-space-lms-be --profile full)
dbs=(identity-db classroom-db assessment-db content-db payment-db notification-db)

if (( $# == 0 )); then
  missing=()
  for service in IDENTITY CLASSROOM ASSESSMENT CONTENT PAYMENT NOTIFICATION; do
    for field in NAME PORT USER PASSWORD ROOT_PASSWORD; do
      key="${service}_DB_${field}"
      if [[ -z "${!key:-}" ]]; then missing+=("$key"); fi
    done
  done
  for key in JWT_PRIVATE_KEY JWT_PUBLIC_KEY JWT_ISSUER JWT_AUDIENCE IDENTITY_OTP_HMAC_SECRET; do
    if [[ -z "${!key:-}" ]]; then missing+=("$key"); fi
  done
  if (( ${#missing[@]} )); then
    printf 'Export the following required variables in this terminal: %s\n' "${missing[*]}" >&2
    exit 2
  fi

  for service in IDENTITY CLASSROOM ASSESSMENT CONTENT PAYMENT NOTIFICATION; do
    key="${service}_DB_NAME"
    printf '%s database: %s\n' "$service" "${!key}"
  done
fi

"${compose[@]}" config --quiet
"${compose[@]}" up -d --wait "${dbs[@]}"
"${compose[@]}" --profile tools run --rm --no-deps migrations All
"${compose[@]}" up -d --build
