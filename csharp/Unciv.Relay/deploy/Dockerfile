# Unciv.Relay container (design/RELAY.md section 8). Build from the csharp/ directory:
#   docker build -f Unciv.Relay/deploy/Dockerfile -t unciv-relay .
# Runs as a non-root user with all state in /data (relay.json, relay-secret, relay.pfx, bans.txt, logs/).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Unciv.Relay -c Release -r linux-x64 --self-contained true -o /out

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN useradd --system --uid 10001 --no-create-home --shell /usr/sbin/nologin relay \
    && mkdir /data && chown relay /data
COPY --from=build /out /app
USER relay
WORKDIR /data
VOLUME /data
# Tunnel and HTTP directory. The compose file publishes them on 127.0.0.1 only.
EXPOSE 47830 47831
ENTRYPOINT ["/app/Unciv.Relay", "--data-dir", "/data", "--http-bind", "0.0.0.0"]
# Default mode behind a TLS-terminating proxy; override the command for --cert/--key or --self-signed.
CMD ["--tls", "none", "--bind", "0.0.0.0", "--proxy-protocol", "--trusted-proxy", "172.16.0.0/12", "--public-name", "relay.example.org"]
