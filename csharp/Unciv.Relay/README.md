# Unciv.Relay: the relay server

<!-- toc -->
## Contents

- [What it is](#what-it-is)
- [Quick start](#quick-start)
- [TLS modes](#tls-modes)
- [Behind nginx](#behind-nginx)
- [Sharing port 443 by SNI](#sharing-port-443-by-sni)
- [Docker and systemd](#docker-and-systemd)
- [Files in the data dir](#files-in-the-data-dir)
- [Limits and abuse handling](#limits-and-abuse-handling)
- [The directory API](#the-directory-api)
- [Logs and privacy](#logs-and-privacy)
- [Stopping and reloading](#stopping-and-reloading)
- [What is verified](#what-is-verified)
<!-- /toc -->

## What it is

A host that cannot open a port (NAT, CGNAT, a firewall) dials OUT to the relay, which splices each joiner to that host. The game's own end-to-end TLS 1.3 (pinned host key fingerprint)
runs through the splice, so the relay can neither read nor change game data, passwords or tokens. It can also list public games in a lobby directory. It has no accounts, no database and
no game logic, and it never opens an outbound connection of any kind (it cannot be used as a proxy). The design, protocol and threat model are in `migration/design/RELAY.md`.
The software has **no default relay**: nobody's relay is used unless a player or a server operator types its address.

## Quick start

```
# a relay with a domain and a Let's Encrypt certificate
Unciv.Relay --cert /etc/letsencrypt/live/relay.example.org/fullchain.pem --key /etc/letsencrypt/live/relay.example.org/privkey.pem

# no domain: a generated certificate; players pin the printed fingerprint
Unciv.Relay --self-signed --public-name 203.0.113.10
```

It prints `LISTENING tunnel 47830` (and `LISTENING http 47831` for the optional read-only HTTP side) and, in the self-signed mode, `RELAY-FINGERPRINT <fp>`: give players
`unciv-relay://203.0.113.10:47830?fp=<fp>`. `--help` lists every option; every option can also be a key of the same name in `DATA-DIR/relay.json` (or `--config FILE`), the command line wins.
`relay.example.org` is a placeholder in every example here.

`--public-name` is the name hosts sign (they bind the relay name they dialled into their signature, so a relay cannot replay a host's login to another relay). With `--cert` it defaults to the
certificate's DNS names; with `--self-signed` and `--tls none` you must give it (an IP literal is fine).

## TLS modes

| Mode | Flags | When |
|---|---|---|
| Certificate | `--cert FILE --key FILE` | You have a domain and a certificate. Re-read on SIGHUP and when the files change, so certbot renewal needs no restart. |
| Self-signed | `--self-signed --public-name NAME` | No domain. Clients pin the fingerprint. The certificate is created once as `relay.pfx` in the data dir. |
| Behind a proxy | `--tls none --proxy-protocol --public-name NAME` | nginx stream terminates TLS. Listens on loopback only unless you give `--bind`. |

## Behind nginx

`deploy/nginx/relay.conf.example` is the file to adapt. Run the relay with `--tls none --proxy-protocol --public-name relay.example.org --tunnel-port 47832`; nginx `stream` listens on the public
port, terminates TLS (ALPN `unciv-relay/1`, nginx 1.21.4 or newer) and forwards bytes with a PROXY protocol header so the relay's per-address limits and bans see the real client.
The relay accepts the header only from `--trusted-proxy` addresses (default loopback); anything else is closed. `X-Forwarded-For` on the HTTP side is trusted from the same list.
WebSocket was rejected for v1: raw TCP needs no framing. Cloudflare-style HTTP proxies do not carry raw TCP; do not put one in front.

## Sharing port 443 by SNI

If restrictive networks matter more than isolation from your website, nginx can route by the TLS server name on port 443 (module `ngx_stream_ssl_preread_module`, `nginx -V` must show
`--with-stream_ssl_preread_module`). The relay then keeps its own certificate (TLS passthrough): run it with `--cert/--key --proxy-protocol --bind 127.0.0.1 --tunnel-port 47833` and see the commented
block at the end of the first stream section of `deploy/nginx/relay.conf.example` (a `map $ssl_preread_server_name` choosing the relay for `relay.example.org` and your web server otherwise).
Everything behind `proxy_protocol on` must understand the header; if your web server does not, give the relay its own port instead (the default 47830, the recommendation).

## Docker and systemd

`deploy/Dockerfile` (build from `csharp/`: `docker build -f Unciv.Relay/deploy/Dockerfile -t unciv-relay .`) and `deploy/compose.yml` (non-root user, read-only root, volume for the data dir,
ports published on `127.0.0.1` only). `deploy/unciv-relay.service` is a systemd unit (`DynamicUser`, `ProtectSystem=strict`, `NoNewPrivileges`, `Restart=on-failure`; `systemctl reload` sends SIGHUP).
Firewall: open the tunnel port (47830 by default, or 443); keep the HTTP port (47831) on loopback behind nginx.
Self-contained builds for Linux (x64) and Windows (x64) come from `dotnet publish Unciv.Relay -c Release -r linux-x64 --self-contained true` (the release tooling does this).

## Files in the data dir

`relay.json` (options), `relay-secret` (32 random bytes, created on first run: keep it, it makes room codes and peer ids stable across restarts), `relay.pfx` (self-signed only),
`bans.txt`, `allow-keys.txt`, `deny-names.txt`, `logs/relay-YYYY-MM-DD.log`. The three list files are re-read on SIGHUP and when they change.

- `bans.txt`: one entry per line. An IP or CIDR (`203.0.113.0/24`, `2001:db8::/32`), `key <fingerprint>` (64 hex characters or the 26-character form of an invite) or `name <regex>` (matched against listing names). `#` starts a comment.
  A banned address is refused at connect; a newly banned key or name closes the live room.
- `allow-keys.txt`: with `--listing allowlist` only these host key fingerprints may list games.
- `deny-names.txt`: one regex per line; a listing whose name or host name matches is rejected.

## Limits and abuse handling

Every limit of RELAY.md section 6 is a flag (see `--help`) with the design's default: 200 rooms, 600 splices, 2 rooms and 40 connections per source network (an IPv4 address or an IPv6 /64), 16 joiners and 8 pending joins
per room, 2 MiB/s per splice with a 48 MiB burst, 20 GiB per room per day, 120 s idle, 30 handshakes per minute per network; 10 unknown-room lookups per minute earn a 10 minute ban. Players behind CGNAT share a network:
raise `--max-rooms-per-network` and `--max-connections-per-network` if that bites. `--max-mbps` and `--max-gib-per-month` are off by default; set them if the VM is metered.
To act on an abuse report: find the room in the log (`grep room=K7QF-3MXP4A logs/*`), add the IP or `key` line to `bans.txt` and `systemctl reload unciv-relay`. Set `--contact` so players can reach you (shown by `/v1/info`).

## The directory API

Read-only JSON, served over the tunnel (op `get`) and, if enabled, over HTTP: `GET /v1/info`, `GET /v1/games?q=&ruleset=&mod=&hasSlots=1&passwordRequired=0|1&started=0|1&version=&limit=100&cursor=`, `GET /v1/games/{room}`,
`GET /healthz`, and `GET /metrics` (only with `--metrics`). Registration, update and removal of listings are ops on the host's authenticated control channel, not HTTP endpoints. Each listing carries the host's certificate and signature;
clients verify them, so the relay cannot forge a listing. `--listing open|allowlist|off`.

## Logs and privacy

One line per event: time, level, event, `room=`, `fp=` (first 8 characters), `peer=`, `reason=`, `bytes=`, `ip=`. Never a splice byte, a preamble body, a name, a password or a token. Connection events keep the source IP so
abusers can be found; the relay deletes log files older than `--log-retention-days` (7) itself. The relay operator can see IPs, rooms, times and byte counts, not game data (RELAY.md section 9).

## Stopping and reloading

SIGTERM or SIGINT: stop accepting, tell every host `closing: restart` (they reconnect with backoff and take their room back), wait `--drain-seconds` (10) for running splices, exit 0.
SIGHUP: reload the certificate and the three list files. On Windows only Ctrl+C is delivered.

## What is verified

The protocol, limits, directory, bans, drain, reload and the four TLS modes (certificate, self-signed, plain, plain behind a PROXY protocol test proxy) are covered by automated tests in `Unciv.App.Tests/Relay`.
UNVERIFIED in this repository's CI: the Dockerfile build and container start (no Docker on the build machine), the systemd unit, the Windows build, and the nginx example (`nginx -t` runs only when nginx is installed).
The deployment ticket (T356) exercises them on the real server.
