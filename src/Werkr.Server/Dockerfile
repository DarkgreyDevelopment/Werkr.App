# syntax=docker/dockerfile:1
# -------------------------------------------------------------------
#  Werkr.Server — Blazor Server UI
#
#  Two build modes (controlled by BUILD_MODE arg):
#    source (default) — builds from source using the SDK image
#    deb              — installs pre-built .deb (ServerBundle) from Publish/
#
#  For .deb mode, run publish.ps1 first:
#    pwsh scripts/publish.ps1 -Application ServerBundle -Platform linux -Architecture x64 -BuildDebInstallers
#    docker compose build --build-arg BUILD_MODE=deb
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

COPY src/Werkr.ServiceDefaults/Werkr.ServiceDefaults.csproj           src/Werkr.ServiceDefaults/
COPY src/Werkr.Common.Configuration/Werkr.Common.Configuration.csproj src/Werkr.Common.Configuration/
COPY src/Werkr.Common/Werkr.Common.csproj                             src/Werkr.Common/
COPY src/Werkr.Core/Werkr.Core.csproj                                 src/Werkr.Core/
COPY src/Werkr.Data/Werkr.Data.csproj                                 src/Werkr.Data/
COPY src/Werkr.Data.Identity/Werkr.Data.Identity.csproj               src/Werkr.Data.Identity/
COPY src/Werkr.Server/Werkr.Server.csproj                             src/Werkr.Server/

# Proto files needed during restore/build for gRPC codegen
COPY src/Werkr.Agent/Protos/      src/Werkr.Agent/Protos/

RUN dotnet restore src/Werkr.Server/Werkr.Server.csproj \
      -r linux-${TARGETARCH}

COPY src/ src/
RUN dotnet publish src/Werkr.Server/Werkr.Server.csproj \
      -c Release -r linux-${TARGETARCH} -o /app/publish \
      --sc true

# ==========================
#  Source runtime
# ==========================
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS source

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system werkr \
    && useradd --system --no-create-home -g werkr werkr

WORKDIR /app

# Create volume-mount directories while still root so ownership
# is baked into the image layer. Docker named volumes copy these
# permissions on first mount — no runtime chown/gosu needed.
RUN mkdir -p /app/keys /app/config /app/certs \
    && chown -R werkr:werkr /app/keys /app/config /app/certs

COPY --from=source-build --chown=werkr:werkr /app/publish .

EXPOSE 8443

ENV ASPNETCORE_URLS=https://+:8443 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production

USER werkr
ENTRYPOINT ["./Werkr.Server"]

# ==========================
#  Deb runtime
# ==========================
FROM debian:bookworm-slim AS deb

RUN apt-get update && apt-get install -y --no-install-recommends \
        curl libicu72 libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY Publish/Werkr.ServerBundle.*.deb /tmp/
RUN dpkg -x /tmp/*.deb / && rm /tmp/*.deb

RUN groupadd --system werkr \
    && useradd --system --no-create-home -g werkr werkr \
    && mkdir -p /opt/werkr/serverbundle/keys /opt/werkr/serverbundle/config /opt/werkr/serverbundle/certs \
    && chown -R werkr:werkr /opt/werkr/serverbundle/keys /opt/werkr/serverbundle/config /opt/werkr/serverbundle/certs

USER werkr
WORKDIR /opt/werkr/serverbundle

EXPOSE 8443

ENV ASPNETCORE_URLS=https://+:8443 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["./Werkr.Server"]

# ==========================
#  Final (selects mode via BUILD_MODE arg)
# ==========================
FROM ${BUILD_MODE} AS final
