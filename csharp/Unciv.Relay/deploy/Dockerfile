# Unciv.Relay container (design/RELAY.md section 8, T374). The build context is the csharp/ directory of the repository:
#   cd csharp && docker build -f Unciv.Relay/deploy/Dockerfile -t unciv-relay .
# or, with the options as environment variables, `docker compose up -d --build` in csharp/Unciv.Relay/deploy (see SETUP.md).
# Multi-stage: the SDK image builds, the small runtime image runs. It is architecture independent (x86-64 and ARM64 servers alike).
# Runs as a non-root user; all state lives in /data (relay.json, relay-secret, relay.pfx, bans.txt, logs/).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Only what the relay needs, so that an edit elsewhere in the repository does not invalidate the build cache.
COPY Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY Unciv.Core/Unciv.Core.csproj Unciv.Core/
COPY Unciv.Relay/Unciv.Relay.csproj Unciv.Relay/
RUN dotnet restore Unciv.Relay
COPY Unciv.Core Unciv.Core
# Unciv.Core embeds the AI data tables from migration/ai-spec/data (see its csproj)
COPY migration/ai-spec/data migration/ai-spec/data
COPY Unciv.Relay/*.cs Unciv.Relay/
RUN dotnet publish Unciv.Relay -c Release --no-restore -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN useradd --system --uid 10001 --no-create-home --shell /usr/sbin/nologin relay \
    && mkdir /data && chown relay /data
COPY --from=build /out /app
COPY --chmod=755 Unciv.Relay/deploy/healthcheck.sh /app/healthcheck.sh
USER relay
WORKDIR /data
VOLUME /data
# Options are UNCIV_RELAY_<OPTION> variables (see .env.example); these are the container's fixed ones.
# Compose publishes the two ports on the host; the relay listens on all interfaces INSIDE the container.
ENV UNCIV_RELAY_DATA_DIR=/data \
    UNCIV_RELAY_BIND=0.0.0.0 \
    UNCIV_RELAY_HTTP_BIND=0.0.0.0 \
    UNCIV_RELAY_TUNNEL_PORT=47830 \
    UNCIV_RELAY_HTTP_PORT=47831 \
    DOTNET_EnableDiagnostics=0
EXPOSE 47830 47831
# GET /healthz over the HTTP side; with UNCIV_RELAY_NO_HTTP only a TCP connect to the tunnel port. (The runtime image has bash but no curl.)
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 CMD ["/app/healthcheck.sh"]
ENTRYPOINT ["dotnet", "/app/Unciv.Relay.dll"]
# No command: the options come from the environment. Without a TLS mode the relay exits with "choose a TLS mode"; pass a command to override, e.g. --self-signed --public-name 203.0.113.10.
