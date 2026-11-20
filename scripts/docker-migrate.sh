#!/usr/bin/env bash
set -euo pipefail

# Invoked by the opt-in Compose tools service. Source is read-only; build/tool output
# stays in this disposable container, and credentials are supplied through its environment.
case "${1:-Assessment}" in
  All|all) area=All ;;
  Assessment|assessment) area=Assessment ;;
  Identity|identity) area=Identity ;;
  Classroom|classroom) area=Classroom ;;
  Payment|payment) area=Payment ;;
  Content|content) area=Content ;;
  *) echo 'Usage: docker compose --profile tools run --rm migrations [All|Assessment|Identity|Classroom|Content|Payment]' >&2; exit 2 ;;
esac
if [[ "$#" -gt 1 ]]; then
  echo 'Only a service name is accepted; this command only updates to the latest migration.' >&2
  exit 2
fi

areas=("$area")
if [[ "$area" == All ]]; then
  areas=(Identity Classroom Assessment Content Payment)
fi
for area in "${areas[@]}"; do
  connection_key="ConnectionStrings__${area}Database"
  if [[ -z "${!connection_key:-}" ]]; then
    echo "Missing $connection_key configuration." >&2
    exit 2
  fi
done

mkdir -p /work/src /tmp/ef-tools
tar -C /source --exclude='*/bin' --exclude='*/obj' -cf - src | tar -C /work -xf -
dotnet tool install --tool-path /tmp/ef-tools dotnet-ef --version 9.0.11 --verbosity quiet
for area in "${areas[@]}"; do
  project="/work/src/Services/$area/ToeicSpace.$area.Infrastructure"
  startup="$project"
  cd "$project"
  echo "Updating $area database..."
  /tmp/ef-tools/dotnet-ef database update --context "${area}DbContext" \
    --project "$project" --startup-project "$startup" --configuration Release
done
