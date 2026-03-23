# syntax=docker/dockerfile:1
# -------------------------------------------------------------------
#  Werkr.Agent — Background agent with gRPC, SQLite, PowerShell
#
#  Two build modes (controlled by BUILD_MODE arg):
#    source (default) — builds from source using the SDK image
#    deb              — installs pre-built .deb from Publish/
#
#  For .deb mode, run publish.ps1 first:
#    pwsh scripts/publish.ps1 -Application Agent -Platform linux -Architecture x64 -BuildDebInstallers
#    docker compose build --build-arg BUILD_MODE=deb
#
#  Or use the wrapper script:
#    ./scripts/docker-build.sh --deb
#
#  Build context: repository root (Werkr_Final/)
# -------------------------------------------------------------------

ARG BUILD_MODE=source

# ==========================
#  Source build stage
# ==========================
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS source-build
ARG TARGETARCH
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json Werkr.slnx ./

COPY src/Werkr.ServiceDefaults/Werkr.ServiceDefaults.csproj       src/Werkr.ServiceDefaults/
COPY src/Werkr.Common.Configuration/Werkr.Common.Configuration.csproj src/Werkr.Common.Configuration/
COPY src/Werkr.Common/Werkr.Common.csproj                         src/Werkr.Common/
COPY src/Werkr.Core/Werkr.Core.csproj                             src/Werkr.Core/
COPY src/Werkr.Data/Werkr.Data.csproj                             src/Werkr.Data/
COPY src/Werkr.Agent/Werkr.Agent.csproj                           src/Werkr.Agent/

# Proto files needed during restore/build for gRPC codegen
COPY src/Werkr.Api/Protos/        src/Werkr.Api/Protos/
COPY src/Werkr.Agent/Protos/      src/Werkr.Agent/Protos/

RUN dotnet restore src/Werkr.Agent/Werkr.Agent.csproj \
      -r linux-${TARGETARCH}

COPY src/ src/
RUN dotnet publish src/Werkr.Agent/Werkr.Agent.csproj \
      -c Release -r linux-${TARGETARCH} -o /app/publish \
      --sc true

# ==========================
#  Source runtime
# ==========================
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS source

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system werkr \
    && useradd --system --no-create-home -g werkr werkr \
    && mkdir -p /var/lib/werkr /home/werkr/.config/werkr/secrets \
    && chown -R werkr:werkr /var/lib/werkr /home/werkr

WORKDIR /app

RUN mkdir -p /app/certs /app/job-output \
    && chown -R werkr:werkr /app/certs /app/job-output

COPY --from=source-build --chown=werkr:werkr /app/publish .

EXPOSE 8443

ENV ASPNETCORE_URLS=https://+:8443 \
    ASPNETCORE_ENVIRONMENT=Production \
    WERKR_DATA_DIR=/var/lib/werkr \
    DOTNET_ENVIRONMENT=Production \
    HOME=/home/werkr

USER werkr
ENTRYPOINT ["./Werkr.Agent"]

# ==========================
#  Deb runtime
# ==========================
FROM debian:bookworm-slim AS deb

RUN apt-get update && apt-get install -y --no-install-recommends \
        curl libicu72 libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY Publish/Werkr.Agent.*.deb /tmp/
RUN dpkg -x /tmp/*.deb / && rm /tmp/*.deb

RUN groupadd --system werkr \
    && useradd --system --no-create-home -g werkr werkr \
    && mkdir -p /var/lib/werkr /opt/werkr/agent/certs /home/werkr/.config/werkr/secrets \
    && chown -R werkr:werkr /var/lib/werkr /opt/werkr/agent/certs /home/werkr

USER werkr
WORKDIR /opt/werkr/agent

EXPOSE 8443

ENV ASPNETCORE_URLS=https://+:8443 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production \
    WERKR_DATA_DIR=/var/lib/werkr \
    HOME=/home/werkr

ENTRYPOINT ["./Werkr.Agent"]

# ==========================
#  Final (selects mode via BUILD_MODE arg)
# ==========================
FROM ${BUILD_MODE} AS final
