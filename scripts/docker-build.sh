#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  docker-build.sh — Build Werkr Docker images
#
#  Usage:
#    ./scripts/docker-build.sh                  # source build (default)
#    ./scripts/docker-build.sh --deb            # publish .deb then build
#    ./scripts/docker-build.sh server            # build server only
#    ./scripts/docker-build.sh agent             # build agent only
#    ./scripts/docker-build.sh api               # build api only
#    ./scripts/docker-build.sh --push            # build and push all
#    ./scripts/docker-build.sh --deb --push      # publish, build, push
# ---------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REGISTRY="${DOCKER_REGISTRY:-ghcr.io/werkr}"
TAG="${DOCKER_TAG:-latest}"
PUSH=false
TARGET=""
BUILD_MODE="source"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --deb)    BUILD_MODE="deb"; shift ;;
        --push)   PUSH=true; shift ;;
        --tag)    TAG="$2"; shift 2 ;;
        --registry) REGISTRY="$2"; shift 2 ;;
        server|api|agent) TARGET="$1"; shift ;;
        *)        echo "Unknown argument: $1"; exit 1 ;;
    esac
done

# If .deb mode, run publish.ps1 first to produce the .deb packages
if [[ "$BUILD_MODE" == "deb" ]]; then
    echo "==> Publishing .deb packages via publish.ps1..."
    pwsh "$SCRIPT_DIR/publish.ps1" \
        -Application All \
        -Platform linux \
        -Architecture x64 \
        -BuildDebInstallers \
        -SkipCompression
    echo "==> .deb packages ready in Publish/"
fi

build_image() {
    local name="$1"
    local dockerfile="$2"
    local image="${REGISTRY}/werkr-${name}:${TAG}"

    echo "==> Building ${image} (mode: ${BUILD_MODE})"
    docker build \
        -t "${image}" \
        -f "${REPO_ROOT}/${dockerfile}" \
        --build-arg BUILD_MODE="${BUILD_MODE}" \
        "${REPO_ROOT}"

    if [ "$PUSH" = true ]; then
        echo "==> Pushing ${image}"
        docker push "${image}"
    fi
}

# Build requested image(s)
case "${TARGET:-all}" in
    server) build_image "server" "src/Werkr.Server/Dockerfile" ;;
    api)    build_image "api"    "src/Werkr.Api/Dockerfile" ;;
    agent)  build_image "agent"  "src/Werkr.Agent/Dockerfile" ;;
    all)
        build_image "server" "src/Werkr.Server/Dockerfile"
        build_image "api"    "src/Werkr.Api/Dockerfile"
        build_image "agent"  "src/Werkr.Agent/Dockerfile"
        ;;
esac

echo "Done."
