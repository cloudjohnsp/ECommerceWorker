#!/usr/bin/env bash
set -euo pipefail

configuration=Release
skip_image=false

usage() {
  echo "Usage: $0 [--configuration Debug|Release] [--skip-image]" >&2
}

while (($#)); do
  case "$1" in
    --configuration) configuration="$2"; shift 2 ;;
    --skip-image) skip_image=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

[[ "$configuration" == Debug || "$configuration" == Release ]] || {
  echo "Configuration must be Debug or Release." >&2
  exit 2
}

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd -- "$script_dir/.." && pwd)"

if command -v dotnet >/dev/null 2>&1; then
  dotnet_bin=dotnet
elif command -v dotnet.exe >/dev/null 2>&1; then
  dotnet_bin=dotnet.exe
else
  echo "dotnet was not found." >&2
  exit 1
fi

if [[ "$skip_image" == false ]]; then
  if command -v docker >/dev/null 2>&1; then
    docker_bin=docker
  elif command -v docker.exe >/dev/null 2>&1; then
    docker_bin=docker.exe
  else
    echo "Docker was not found. Install or start Docker Desktop and try again." >&2
    exit 1
  fi
fi

invoke_gate_step() {
  echo "==> $1"
  shift
  "$@"
}

cd "$root"
invoke_gate_step "Restore Worker" "$dotnet_bin" restore ECommerceWorker.slnx
invoke_gate_step "Verify Worker formatting" "$dotnet_bin" format ECommerceWorker.slnx --verify-no-changes --no-restore
invoke_gate_step "Build Worker" "$dotnet_bin" build ECommerceWorker.slnx --configuration "$configuration" --no-restore
invoke_gate_step "Test Worker with PostgreSQL" env RUN_POSTGRES_INTEGRATION_TESTS=true "$dotnet_bin" test ECommerceWorker.slnx --configuration "$configuration" --no-build --no-restore

if [[ "$skip_image" == false ]]; then
  revision="$(git rev-parse HEAD)"
  tag="ecommerce-worker:sha-$revision"
  [[ -z "$(git status --porcelain)" ]] || tag+="-dirty"
  invoke_gate_step "Build Worker image" "$docker_bin" build --label "org.opencontainers.image.revision=$revision" --tag "$tag" .
fi

echo "Worker local gate passed."
