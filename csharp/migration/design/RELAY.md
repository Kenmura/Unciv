# Relay server and lobby browser: host without open ports, find public games (design for T336; implemented by T337, T338, T339)

<!-- toc -->
## Contents

- [1. Goals, non-goals, anti-goals](#1-goals-non-goals-anti-goals)
- [2. Architecture](#2-architecture)
- [3. Relay tunnel protocol (version 1)](#3-relay-tunnel-protocol-version-1)
- [4. Invite codes with a relay](#4-invite-codes-with-a-relay)
- [5. How it fits the host, the dedicated server and host migration](#5-how-it-fits-the-host-the-dedicated-server-and-host-migration)
- [6. Threat model, limits and defaults](#6-threat-model-limits-and-defaults)
- [7. Lobby directory](#7-lobby-directory)
- [8. Deployment: standalone and behind nginx](#8-deployment-standalone-and-behind-nginx)
- [9. Trust model (state it to players; extends SERVER.md 6.6)](#9-trust-model-state-it-to-players-extends-servermd-66)
- [10. Logging, privacy and retention (defaults, owner question Q8)](#10-logging-privacy-and-retention-defaults-owner-question-q8)
- [11. Rejected alternatives](#11-rejected-alternatives)
- [12. Go/no-go and acceptance criteria](#12-gono-go-and-acceptance-criteria)
- [13. Owner questions (each with the recommended default) and follow-up tickets](#13-owner-questions-each-with-the-recommended-default-and-follow-up-tickets)
<!-- /toc -->

Status: design with acceptance criteria. Owner request 2026-10-30: users host a game without opening ports, and find hosted games in a lobby browser; the owner has a domain and an OPTIONAL nginx server. This reverses the SERVER.md out-of-scope row "NAT hole punching / relay" and decision D8 (section 8 of `design/SERVER.md`): that row ruled out depending on someone else's rendezvous infrastructure; the owner now runs their own. The no-accounts, no-upstream-server, host-run-game decisions (D2, D4) are unchanged: the relay hosts no game, runs no game logic and has no user accounts.

Read with: `design/SERVER.md` sections 6.3-6.6 (TLS 1.3, host key, fingerprint in the invite, TOFU, trust model), `design/HOST-MIGRATION.md` (successor endpoints, `HostSuccessor`), the in-process host (T205) and the dedicated server (T265, `Unciv.Server`).

## 1. Goals, non-goals, anti-goals

**Goals**
1. A host behind NAT, CGNAT or a firewall hosts with no router configuration: the host process dials OUT to the relay.
2. The relay only splices bytes. The existing end-to-end TLS 1.3 stream (SERVER.md 6.4) runs *through* it, so the pinned host-key fingerprint still authenticates the host and the relay can neither read nor alter game data, passwords, tokens or identity proofs.
3. Public games can be listed in a lobby directory and browsed in the client; listing is opt-in per game (default off).
4. The relay runs standalone (one executable, no nginx) and behind the owner's nginx; both are first-class and tested.
5. Cheap to run: one small VM, no database, no accounts; state is in memory and rebuilt by hosts reconnecting.

**Non-goals (no ticket; stated so nobody assumes them)**
- Accounts, logins, ratings, friend lists, chat outside a game, relay-side game logic or replay.
- Several relays with federation or automatic failover. One relay URL per client setting (owner question Q13 states the default).
- Direct (hole punched) paths after the relay is used to meet. Rejected, section 11.
- Hiding the *relay operator's* view of metadata (who connects to which room, byte counts, timing). Stated in the trust model (section 9).

**Anti-goals (must never happen; each has a test in section 12)**
- Defaulting to, or silently using, any relay the owner does not run. The source ships with NO default relay URL.
- The relay opening connections to addresses chosen by a client (open proxy).
- The relay holding game plaintext, the lobby password, invite tokens or player keys.
- A listing the host did not sign being shown as authentic.

## 2. Architecture

```
 host process (in-process Host button or Unciv.Server)          relay (Unciv.Relay)                 joiner client
   |  TCP+TLS (outer, relay certificate)  --control channel-->   room registry  <--TCP+TLS (outer)--  join{room}
   |<-- {incoming, id} --------------------------------------   |
   |  TCP+TLS (outer)  --data channel: accept{room,id}-------->  splice(joiner conn, data conn)
   |==== inner TLS 1.3 server handshake (host key) ================ end to end ===== inner TLS client, fp pinned ====|
   |        existing Hello/Join/Auth/... frames, unchanged
```

- The *outer* TLS (client to relay) protects the tiny relay preamble and hides which room is used from the network, and is what nginx or a certificate terminates. The *inner* TLS is the unchanged SERVER.md 6.4 session between joiner and host. Nesting costs a few KiB of handshake and about 1 CPU-microsecond per record; game traffic is tens of KiB per turn.
- **Nothing in the game protocol changes.** The host-side accept loop gets one more source of accepted connections (a `RelayListener` next to `TcpTlsListener`) that yields the same raw `Stream` into the same inner TLS server handshake and `ITransport` wrapping; the joiner side gets a `RelayDialer` that returns a `Stream` for the same inner TLS client.
- The relay holds no secrets that matter: its own TLS certificate (a public CA certificate or, standalone without a domain, a self-signed one whose fingerprint the client pins), and an in-memory table `room -> host control connection`.
- The relay never opens an outbound connection of any kind (no DNS lookups of client input, no dialing). It only accepts. This is what makes "no open proxy" structural rather than a filter (R3).

## 3. Relay tunnel protocol (version 1)

One TCP listener (default `47830`), TLS 1.2/1.3 with ALPN `unciv-relay/1`. After the TLS handshake the client sends ONE preamble line, then the connection is either a control message stream or becomes raw bytes.

**Framing of the preamble.** A single UTF-8 JSON object terminated by `\n`, at most 1024 bytes (the relay reads byte by byte up to the cap and 5 s; a longer or slower preamble closes the connection with `too-large` / `timeout`). Replies are the same: one JSON line. After a successful `join` or `accept` reply line, everything that follows in both directions is opaque bytes (the inner TLS).

| op (client to relay) | fields | meaning |
|---|---|---|
| `host` | `v:1`, `cert` (host certificate, DER base64), `gameId`, `sig` (see below) | Opens the *control channel* of the room for this host key and game id (see 'Room codes'). |
| `accept` | `v:1`, `room`, `id`, `sig` | The host opens a *data channel* for the joiner `id` announced on the control channel. |
| `join` | `v:1`, `room`, optional `fp` (expected fingerprint, early refusal only) | A joiner asks for a splice to the room's host. |
| `get` | `v:1`, `path` (`/v1/info`, `/v1/games?...`, `/v1/games/{room}`) | Read-only directory request over the tunnel port (section 7); the reply is one JSON line, then the connection closes. |

The preamble cap is 2 KiB (a `host` line carries a certificate of about 600 bytes plus a signature); control-channel lines (`list`, `update`) are capped at 4 KiB (`too-large`).

**Host authentication = the host key, not a relay secret.** After the outer TLS the relay sends `{"challenge":"<16 random bytes b64>","v":1}` on EVERY connection (fresh each time); the client then sends its one preamble line. `join` and `get` ignore the challenge. For `host`, `sig` is ECDSA P-256/SHA-256 by the host certificate's private key over `"unciv-relay-v1|host|" + relayHost + "|" + challenge + "|" + gameId`; for `accept`, over `"unciv-relay-v1|accept|" + relayHost + "|" + challenge + "|" + room + "|" + id`. `gameId` is the game the host runs; `relayHost` is the name the host dialled, so a malicious relay A cannot forward relay B's challenge to register the host's room on B. Both strings are domain separated from `unciv-promotion-v1` and the auth signature (T273). The relay derives `fp` from `cert` (full SHA-256 of the public key; the invite carries only its first 128 bits, so the client compares the 128-bit prefix where it holds an invite and the full value where it holds a listing). Consequences: a room is bound to one host key and game; reclaiming it after a reconnect needs the same key; nobody can squat or hijack another host's room; no relay-issued secret to store or leak. A challenge is single use and dies with the 5 s preamble window, so a captured line cannot be replayed.

**Control channel.** After `host` succeeds the relay replies `{"ok":true,"room":"K7QF-3MXP4A","fp":"...","limits":{...},"ttl":90}`. The relay then writes events as JSON lines and expects pings: `{"ev":"incoming","id":"<24 hex>","peer":"<opaque>"}`, `{"ev":"closing","code":"..."}`; the host sends `{"op":"ping"}` every 20 s (the relay replies `{"ev":"pong"}`; 90 s of silence ends the room, matching `HostLimits.IdleTimeoutMillis`) and the listing messages of section 7 (`list`, `update`, `unlist`). One control channel per room; a second `host` for the same fp supersedes the first (the old one gets `closing: superseded`), so a host that restarts or loses its network takes the room back immediately.

**Accept flow.** `id` is single-use, bound to its room and valid only for the 10 s `host-timeout`; a late, duplicate or foreign `accept` gets `{"ok":false,"code":"bad-id"}` and is closed. The host may refuse an `incoming` without dialing: `{"op":"reject","id":"..","code":"busy"}` (the joiner gets `room-full`), so it can apply its per-`peer` limits before spending an outbound TLS connection and a signature. The relay also limits joins to 10 per minute per source network per room. `peer` is carried by `incoming` only.

**Joiner.** `join` returns `{"ok":true}` only after the host's matching `accept` arrives (at most 10 s, else `{"ok":false,"code":"host-timeout"}`), then the connection is spliced to the data channel. Errors (all `{"ok":false,"code":C}` and then close): `room-unknown`, `host-gone`, `room-full`, `rate-limited`, `banned`, `relay-full`, `bad-signature`, `unsupported-version`, `too-large`, `timeout`, `wrong-host` (the optional `fp` did not match: a convenience for a stale invite, never a trust decision), `listing-rejected`, `listing-disabled`.

**Room codes.** Relay-assigned (deterministically, see below), 10 symbols of Crockford base32 (50 bits) shown as `K7QF-3MXP4A`, case-insensitive, no ambiguous characters. The code is a rendezvous name, not a credential (the inner TLS fingerprint authenticates the host, the password and token the admission), so enumeration yields nothing but a knock on a host that rate-limits strangers; the 50 bits plus the failed-lookup limits (section 6) make scanning pointless. A host that reconnects with its key gets the same code back as long as the relay still has the room or the room was last seen less than `room-ttl` (24 h, in memory) ago; after a relay restart the first reconnect re-creates the room under the same code if free (the code is the first 50 bits of `HMAC(relaySecret, fp || gameId || counter)` with `counter` 0 unless that code is taken by another key, then the next counter, stable while the room exists; a stable relay secret in the data dir keeps invites valid across relay restarts, and a host running two games or two local instances, T334, gets one room per game id instead of superseding itself; without the secret the code changes and the Host screen shows the new invite). There is no client-chosen room: the host reconnects with the same key and game id. Games of one host key are linkable by the relay operator (section 9).

**Peer identity for the host.** Through the relay every inbound connection comes from the relay's address, so the host's per-IP limits, failed-password back-off (SERVER.md 6.4) and bans would see one client. The `incoming` event and the `accept` reply carry `peer`: an opaque 16-hex `HMAC(HMAC(relaySecret, room), joinerNetwork)` (stable across relay restarts with a stable secret, different per room), where `joinerNetwork` is the IPv4 address or the IPv6 /64 prefix. The same keying (/64 for IPv6) applies to every per-IP limit, ban and rate in this document; otherwise one host rotating addresses would escape them. The host uses it as the key for those limits on relay connections and never learns the joiner's address (a privacy gain against malicious hosts). The relay is the only party that sees raw IPs.

**Transport details.** TCP keepalive on, `NoDelay`, 16 KiB splice buffers, back-pressure per direction (a slow side blocks only its own pump, never the relay; a side that cannot drain within the idle cap is closed). Half-close propagates. Version negotiation: `v` is checked first; a newer client gets `unsupported-version` with the relay's `max` so the UI can say "relay too old".

## 4. Invite codes with a relay

Today: `unciv://<host>:<port>/<fp-base32>[/<token>]` (SERVER.md 6.4). New forms, all parsed by the same `Invite` parser:

- `unciv://<relayhost>[:<port>]/r/<room>/<fp-base32>[/<token>]` ("relay invite"): fully authenticated like a direct invite: the joiner dials the relay named in the invite, joins `<room>`, and pins `<fp>` in the inner TLS before sending a single game byte. No first-use window.
- `<room>` alone (typed `K7QF-3MXP4A`): resolved against the relay URL from the client setting; no fingerprint, so TOFU exactly as for a typed `address:port` (UI shows the fingerprint, stores it in `known-hosts.json`, hard stop on change).
- A relay named in an invite that differs from the configured relay is allowed (a friend may run another relay) but the client asks once "Connect through relay `relay.example.net`?" before the first connection and remembers the answer per relay host (the relay learns the joiner's IP and the room; nothing else). Never auto-added to settings.
- The Host screen shows up to two copy buttons when both paths are active: *Copy invite (via relay)* and *Copy invite (direct address)*. The relay invite is the default copy.

`known-hosts.json` is keyed by `host:port` for direct joins and by `relayhost:port/room` for relay joins (never by the relay's address alone, which every room on it shares); each entry stores the fingerprint, and a fingerprint lookup finds a host that moved between relays, rooms or direct ports and keeps its pin (T338; old files stay valid).

## 5. How it fits the host, the dedicated server and host migration

- **In-process host (T205).** `Host` starts the same `GameHost` and, when a relay URL is configured, also a `RelayListener` (default on when a relay is configured; a "Reach me through the relay" checkbox, saved). The local TCP listener stays as is (LAN, forwarded port, overlay VPN); both can be live at once. Closing the game closes the control channel (`unlist` first).
- **Dedicated server (T265).** `--relay URL` (also `--relay-fingerprint FP` for a self-signed standalone relay, `--list`, `--list-name`, `--list-password-flag` follows the lobby password). `--print-invite` prints both invites.
- **Relay reconnects.** The control channel reconnects with backoff 1, 2, 4 ... 30 s (jittered) and reclaims its room with its host key. Established splices are TCP connections through the relay, so a relay restart ends them like any network drop: clients `Resume` through the normal path (SERVER.md 5.2); nothing in the game protocol knows about the relay.
- **Host migration (HOST-MIGRATION.md).** A successor behind NAT needs a reachable endpoint, so `SuccessorInfo` gains an endpoint kind: `direct{host,port}` (today) or `relay{relay, room}`; the fingerprint is carried either way and stays pinned. The standby successor keeps an *unlisted* room registered on the same relay for the whole standby period (so the host's re-probe, `StandbyListener`, and clients' reconnect can reach it), and promotion makes that room the game's room (listing, if any, is re-published by the new host under its own key; the old host's listing disappears with its control channel). The relay is not part of the epoch fence: it is a pipe, `hostEpoch` and `stale-epoch` still decide who may host. Filed as its own ticket (section 13) because it touches the migration wire types.
- **Resume under another address.** Unchanged: the invite is re-copied.

## 6. Threat model, limits and defaults

Assets: the relay's bandwidth and CPU, the owner's reputation (the relay hosts nobody's content), players' privacy and data, the integrity of what the browser shows.
Attackers: anonymous internet clients (no accounts exist), malicious hosts, malicious joiners, a compromised relay.

| # | Threat | Control (default) | Where tested |
|---|---|---|---|
| T1 | **Open proxying**: use the relay to reach arbitrary hosts/ports. | The relay never dials. A join only reaches a data channel that the *room's authenticated host* opened to the relay. No URL, address or port in any message; the code has no outbound socket API calls (a source-policy test greps the relay project for `Connect`, `ConnectAsync`, `GetHostEntry`, `HttpClient`). | R3 |
| T2 | **Bandwidth abuse** (using the relay as a file mirror or flood amplifier). | Splice is 1:1 (no amplification). Per splice 2 MiB/s sustained, burst 48 MiB (a large snapshot is up to 32 MiB, SERVER.md 6.1); per room 20 GiB per 24 h (then `rate-limited` for new joins, existing splices continue); idle 120 s without a byte in either direction closes (legit clients ping every 20 s); a splice whose first bytes joiner-to-host are not a TLS record (0x16, 0x03) within 10 s is closed (a cheap filter against non-TLS use only: a cooperating host and joiner can still use the relay as a generic TLS pipe, so the byte caps are the real control). Global egress cap `--max-mbps` and `--max-gib-per-month` (both default 0 = off; Q7 asks the owner to set them if the VM is metered) as safety valves. | R5, R6 |
| T3 | **Connection and memory exhaustion.** | Max 200 rooms, 600 concurrent splices, 4 control channels and 2 rooms per source network (IPv4 address or IPv6 /64) and 40 connections per source network (all flags, because players behind CGNAT share addresses; see Q7/Q9), 16 joiners and 8 pending joins per room, per-room 3 per IP; preamble 5 s / 1 KiB; every buffer pre-sized and bounded (a room costs under 16 KiB idle, a splice 2 x 16 KiB); accept loop never blocks on one slow peer. | R4 |
| T4 | **Slowloris, junk, TLS floods.** | Handshake 5 s timeout, per-IP handshake rate 30/min, TLS handshakes run on pooled tasks. SYN floods are the OS's/nginx's job (`limit_conn`, Section 8). | R4 |
| T5 | **Room enumeration / knocking.** | 50-bit codes; per IP 10 `room-unknown` per minute then 10 minutes `banned`; host-side per-peer limits via the opaque `peer` id. | R5 |
| T6 | **Room hijack / squatting.** | Rooms bound to the host key by signature; supersede only by the same key. | R2 |
| T7 | **Relay as MITM of game traffic.** | Inner TLS pinned to the fingerprint (invite) or TOFU with hard-stop on change (known-hosts). The relay cannot present a different certificate undetected. Passwords/tokens only inside the inner channel. Documented limit: for a *first* join to a stranger from the public browser the fingerprint comes from the relay (section 9). | R1 |
| T8 | **Listing spam** (thousands of fake rooms, scam names, URLs, slurs). | A listing exists only while an authenticated host control channel is alive (max 200 rooms, 2 per IP, 1 per key); listing fields are fixed-schema, no free-text description or URL; name 3-40 chars, host name 1-32, NFC-normalised, control/bidi/zero-width characters and anything matching the relay's `deny-names` regexes rejected (`listing-rejected`); `--listing open|allowlist|off` (default `open`; `allowlist` requires the key fingerprint in `allow-keys.txt`; `off` = tunnel only, no directory); listing rate 6 register/min per IP. | R7 |
| T9 | **Impersonation** ("Official Unciv Server", lookalike names, copying another listing). | Every listing is signed by the host key and carries the cert; clients display the short fingerprint and "first time you see this host" or "known host (played with before)". A copied listing cannot be joined as the original (its fp is that of whoever signs it; the signature check ties name to key). The relay reserves names via `deny-names`; no claim that names are unique. Player counts are host claims and labelled as reported. | R7, T339 |
| T10 | **Malicious or illegal use / abuse reports.** | Operator ban list (`bans.txt`: IP/CIDR, key fingerprint, name regex; reloaded on SIGHUP or file change; applies at connect and removes live rooms); `/v1/info` shows an operator-configured `contact` string; logs allow finding an IP by room and time (section 10). | R8 |
| T11 | **Compromised relay.** | Cannot read or alter inner traffic or forge a host listing (signature). Can: refuse service, replay old listings (harmless), observe metadata, point *first-time* browser joins at a host of its choosing (it can list whoever it likes; the player joins a stranger's game exactly as in any public lobby). | section 9 |
| T12 | **Joiner privacy.** | The host never sees a joiner IP (opaque peer id); relay logs keep IPs for 7 days by default (Q8). | R9 |
| T13 | **Directory scraping / HTTP flood.** | Read-only GET, 30 req/min per IP, page max 100 listings, 5 s server cache, ETag; behind nginx `limit_req` too. Body limits on every request (the API takes no request bodies). | R7 |
| T14 | **Version skew.** | `v` in every preamble; `/v1/` path; listings carry `proto` and `version` and the browser greys out incompatible games. | R10 |
| T15 | **Clock/time issues.** | Only the relay's clock decides expiry; listings carry no signed time; the relay accepts only strictly increasing `seq` per room. | R7 |

All numbers above are the defaults of `relay.json` / CLI flags (`--max-rooms`, `--max-splices`, `--splice-rate`, `--room-bytes-per-day`, ...); tests set them low rather than depending on the defaults.

## 7. Lobby directory

**What a listing is.** The host sends, on the control channel, `{"op":"list","body":"<json string>","sig":"<b64>"}` (and `update` with the same shape, `unlist` with none). `body` is a JSON *string* that the relay forwards verbatim, so there is no canonicalisation problem: the signature is ECDSA P-256/SHA-256 by the host key over `"unciv-listing-v1\n" + body`. Body fields (all required unless marked): `v:1`, `seq` (int, strictly increasing within one control channel; a new `host` connection resets it), `name`, `hostName`, `players` (humans joined), `slots` (human slots), `ai`, `ruleset` (name), `rulesetHash`, `mods` (list, max 16 names of at most 64 chars; empty array allowed), `speed`, `map` (size/type string), `passwordRequired` (bool), `turnMode`, `started` (bool; `slots` may be 0 when full), `version` (client version string), `proto` (protocol number). Total at most 1 KiB. The relay validates the schema and the text rules (T8), checks the signature against the connection's host key and `seq`, and stores `{room, fp, cert, body, sig, firstSeen}`.

**No password, token or address is ever in a listing.** `passwordRequired` is only a flag; the password is checked by the host inside the inner TLS channel, so the relay and the directory never see it. A listing never contains the host's IP, the relay offers no way to resolve a room to an address.

**Public vs private.** A room always exists (it is what makes an invite work); a *listing* exists only when the host asks (checkbox "List this game publicly", off by default, per game, needs the relay's `listing != off`). A private game is reachable by invite or room code only. A password game can be listed (flag shown); a game with an invite-token-only policy is not listable (the token would be needed to join; the UI explains this).

**Heartbeat and expiry.** The control channel's 20 s ping IS the heartbeat. A listing disappears: at once on `unlist`, when the control channel ends (graceful close, or 90 s of silence), when the host stops sending `update` for 30 minutes while the game runs (an `update` carries the changed counts; the host sends one on every join/leave and every 5 minutes; it keeps a stale-listing from hanging if the host's UI froze). Hosts update `players`/`slots`/`started` at every join and leave, rate-limited to 1 per 5 s (extra changes coalesce).

**Directory API (read-only, JSON; served by the tunnel op `get` and, if enabled, by the HTTP listener, which adds `Cache-Control: max-age=5`).**
- `GET /v1/info` -> `{name, version, proto, listing, contact, limits{maxRooms,...}, rooms, listed}`; the client shows `name`/`contact` in the browser header.
- `GET /v1/games?q=&ruleset=&mod=&hasSlots=1&passwordRequired=0|1&started=0&version=&limit=100&cursor=` -> `{games:[{room, fp, body, sig, cert, ageSeconds, relayRttHint?}], next}`; sorted by `firstSeen` descending then room; filters are exact/substring on the verified fields (done by the relay on `body` fields it parsed at registration). `ageSeconds` is the relay's, unsigned and informational.
- `GET /v1/games/{room}` -> one listing or 404.
- `GET /healthz` -> `200 ok` with rooms/splices counts (no secrets); `GET /metrics` (Prometheus text, off unless `--metrics`).
- No write endpoints exist: registration happens on the tunnel, where the host's key is already authenticated and where the relay knows the host is alive. This simplifies abuse control (one authenticated channel, no tokens to leak) and is a deliberate change to the T337 deliverable list ("register, heartbeat, remove" are tunnel ops).

**Client verification.** For every listing the client checks: cert parses; `SHA-256(public key)` equals `fp`; the signature over `body` verifies; `v`, `proto` compatible; strings re-validated by the same text rules (a malicious relay must not smuggle bidi/control characters into the UI); the fingerprint is shown shortened. Joining uses the relay's `room` and pins `fp` in the inner TLS.

## 8. Deployment: standalone and behind nginx

`Unciv.Relay` is one self-contained executable (like `Unciv.Server`, T266) with a data dir (`relay.json`, `relay-secret`, `relay.pfx` when self-signed, `bans.txt`, `allow-keys.txt`, `deny-names.txt`). **The client setting is one URL `unciv-relay://host[:port][?fp=<relay fp>]`** (default port 47830). The **tunnel** port (default TCP 47830) serves everything the client needs: the tunnel ops AND the read-only directory (op `get`, section 7), under the same outer TLS and the same optional pin, so it works in all four TLS modes. The **HTTP** listener (default `127.0.0.1:47831`, `--http-tls` to expose) is optional: health checks, `/metrics`, curl, nginx, a future web client. An invite naming a relay other than the configured one shows the "Connect through relay X?" prompt; for a self-signed relay the outer certificate is trusted for nothing (the inner pin authenticates the host), so the client accepts it after that prompt. Options for TLS on the tunnel:

| Mode | Flags | Use |
|---|---|---|
| Standalone, public CA cert | `--cert fullchain.pem --key privkey.pem` (re-read on SIGHUP and when the file changes, so certbot renewal works) | Owner has a domain, no nginx. HTTP port: `--http-tls` serves HTTPS with the same cert, or keep it on loopback. |
| Standalone, no domain | `--self-signed` (generates `relay.pfx`, prints `RELAY-FINGERPRINT ...`) | Friends running a relay on a VPS/IP; clients use `unciv-relay://ip:47830?fp=...` and pin it. |
| Behind nginx stream, TLS terminated by nginx | `--tls none --proxy-protocol --tunnel-port 47832` (relay on `127.0.0.1:47832`; nginx owns the public port 47830) | Recommended when nginx and a certificate already exist: renewal stays in nginx. |
| Behind nginx stream, TLS passthrough by SNI | `--cert/--key` as standalone | Only if the owner wants to share port 443 with the website and keep the certificate on the relay (needs `ssl_preread`). |

Trusting the client address: `--proxy-protocol` accepts PROXY protocol v1/v2 headers **only** from `--trusted-proxy` CIDRs (default `127.0.0.1/32, ::1/128`); the HTTP side reads `X-Forwarded-For` only from the same list. Without a trusted proxy the socket address is used. A header from an untrusted source closes the connection. (Wrong here = every limit sees one IP, or an attacker spoofs IPs to evade bans.)

**nginx stream vs WebSocket (decision: nginx `stream`, raw TCP).**
- *Chosen: `stream`.* The relay speaks TCP+TLS; nginx just forwards bytes (or terminates TLS and forwards bytes), adding no framing, no per-message cost and no new client stack (the client already has `TcpClient`+`SslStream`); long-lived connections only need `proxy_timeout 1h`; the inner TLS is a stream, which maps naturally onto a stream proxy; `proxy_protocol on` conveys the client IP; one `server` block can share 443 by SNI (`ssl_preread`, described in the operator guide of T337) if wanted. The client needs no HTTP at all (directory over the tunnel op `get`); an optional HTTP `location` (or subdomain) exposes the same data to curl and monitors.
- *Rejected for v1: WebSocket.* It would add masking and framing overhead on every byte, an HTTP upgrade state machine in the relay and clients, nginx `proxy_read_timeout`/buffering tuning to stop idle cuts, and proxy-buffer surprises, for no benefit to native clients. Its one real advantage is the web export (browsers have no raw TCP, SERVER.md 6.3a) and CDN friendliness; that is a separate, optional gateway ticket that waits for owner question Q11 (section 13). The framing of section 3 is carried over a WebSocket unchanged, so the design does not block it.
- Cloudflare-style HTTP proxies in front do not carry raw TCP on free plans; Q14 default is "not in front".

Example `nginx.conf` fragments (shipped as `deploy/nginx/relay.conf.example`, the domain is a placeholder, never the owner's):

```nginx
# /etc/nginx/nginx.conf, top level (NOT inside http {})
stream {
    limit_conn_zone $binary_remote_addr zone=relay_per_ip:10m;
    server {
        listen 47830 ssl;                      # or 443 with an ssl_preread map, see doc
        listen [::]:47830 ssl;
        ssl_certificate     /etc/letsencrypt/live/relay.example.org/fullchain.pem;
        ssl_certificate_key /etc/letsencrypt/live/relay.example.org/privkey.pem;
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_alpn unciv-relay/1;                # needs nginx >= 1.21.4
        proxy_pass 127.0.0.1:47832;            # the relay's plain loopback tunnel port (--tls none --tunnel-port 47832)
        proxy_protocol on;
        proxy_timeout 1h;
        proxy_connect_timeout 5s;
        limit_conn relay_per_ip 40;
    }
}
# inside http { ... } for the optional HTTP directory (declare once at http level:
#   limit_req_zone $binary_remote_addr zone=relay_http:10m rate=30r/m;)
# then in the server block:
location /unciv-relay/ {
    proxy_pass http://127.0.0.1:47831/;
    proxy_set_header X-Forwarded-For $remote_addr;
    limit_req zone=relay_http burst=20 nodelay;
}
```
(The exact file is a deliverable of T337; the fragment only shows the shape. With `--tls none` the relay's tunnel listener is plain TCP on loopback, here 47832, and nginx owns the public port and the certificate.)

**Operations.** `Dockerfile` and `compose.yml` (non-root user, read-only root, `127.0.0.1` binds by default, volume for the data dir), a `systemd` unit (`DynamicUser`, `ProtectSystem=strict`, `NoNewPrivileges`, `Restart=on-failure`), graceful shutdown on SIGTERM (stop accepting, send `closing: restart` to every control channel so hosts reconnect, drain 10 s, exit), SIGHUP reloads certs and ban files, `/healthz` for the load balancer or uptime monitor, structured one-line logs. Firewall: allow the tunnel port; the HTTP port stays on loopback behind nginx.

## 9. Trust model (state it to players; extends SERVER.md 6.6)

- The relay operator can see: your IP, which room you join, when, for how long, how many bytes. It cannot see game data, passwords, tokens, keys or who you are in game.
- The relay operator cannot join your game as you, read your traffic, or silently swap your host: a pinned fingerprint (invite or known host) fails closed.
- **Limit that must be stated in the UI help:** when you join a *stranger's public listing* for the first time, the fingerprint comes from the relay's listing (signed by the host key, but the relay chose which listings to show). It is equivalent to TOFU: you trust "this is the host that the lobby shows", exactly as any public lobby; the app shows the fingerprint and "first time you see this host". Joining a friend by invite is fully authenticated.
- Hosts learn the relay's address, never your IP.
- Games run by one host key are linkable by the relay operator (same fingerprint).

## 10. Logging, privacy and retention (defaults, owner question Q8)

Structured, one line per event: `ts level event room fpShort peerHash reasonCode bytes`. Never logged: any splice content, preamble bodies beyond the op name, passwords, tokens, `body` fields other than the room name hash. Connection logs include the source IP (needed to ban abusers) and are rotated away after 7 days (`--log-retention-days`, enforced by the relay's own rotation, not left to logrotate defaults). No persistent per-user state, no analytics, no cookies. The operator sets `contact` and an optional `motd` in `/v1/info`. Whether this suffices for the owner's jurisdiction is the owner's call (Q8).

## 11. Rejected alternatives

| Alternative | Why not |
|---|---|
| UPnP / NAT-PMP | Many routers and every CGNAT do not support it; remains the optional SERVER.md ticket, orthogonal. |
| UDP hole punching / STUN / ICE | Needs a rendezvous server anyway, fails on symmetric NAT and CGNAT (the common case the owner wants to fix), needs UDP transport and a second TLS-like stack; the relay is always-works. |
| WebRTC data channels | Heavy dependency, per-platform native builds (Android/Windows/macOS), still needs signaling plus TURN for the hard cases, DTLS instead of the pinned-key TLS already designed. |
| A third-party relay (Steam, Epic, Tailscale Funnel, ngrok, public TURN/bore) | Never the default (owner rule). A user may point the setting at any relay they trust, nothing more. Overlay VPNs remain documented as before. |
| Relay terminates TLS and speaks the game protocol | The relay would see everything and be a trust anchor; defeats goal 2. |
| Accounts for hosts | Contradicts D4; abuse is handled by keys, limits and bans. |
| Registration over HTTP POST with tokens | Another authentication scheme to build and leak; the tunnel already authenticates by host key and proves liveness. |
| Built-in ACME client in the relay | Needs port 80/443 and a dependency; certbot or nginx already do it (`--cert` reloads on change). |
| Free-text listing descriptions | A spam/abuse magnet with no gameplay value in v1. |
| Relay-issued room secrets instead of host-key signatures | One more secret to store; the host key already identifies the host. |

## 12. Go/no-go and acceptance criteria

**Go**, as an optional, owner-run service; with no relay URL configured the client and server behave exactly as before. Delivered by T337 (relay), T338 (client side), T339 (browser). The R-numbers below are the contract the implementation tickets test against; each needs an automated test unless marked manual.

Test ownership: T337 proves the relay side (R2-R9, R12, R15) with a small test-only host/joiner stub and a loopback relay; T338 owns the full-game versions of R1 and R11, R9 on the host side, and the R13 source-policy test; T339 owns R7's client verifier use and R16.

- **R1 (end to end).** Host and two joiners connect through an in-process relay on loopback (random ports, `RelayServer` startable in tests), play a simultaneous game to a matching state hash; the joiner's pinned fingerprint is verified; a test relay that substitutes its own certificate (MITM relay) is refused with `HostKeyMismatch` and no `Hello` byte is delivered; a recorded relay-side capture of both directions of a splice contains no game plaintext (searches for the display name, ruleset name and password).
- **R2 (host auth).** A `host` or `accept` line with a bad or replayed signature, a signature for another room, or a different key reclaiming an existing room is refused (`bad-signature`); a signature made for another op, id, room, relay host or challenge is refused; the same key reclaims its room and supersedes the old control channel; room codes are 10 base32 symbols.
- **R3 (no open proxy).** There is no code path that dials; a source-policy test over `Unciv.Relay` fails on `Socket.Connect*`, `TcpClient(` with a host, `Dns.`, `HttpClient`. A joiner naming an unknown room gets `room-unknown`; no message accepts an address.
- **R4 (limits).** Tests with lowered limits prove each of: max rooms, rooms and control channels per IP, connections per IP, joiners and pending joins per room, handshake and preamble timeouts and the 1 KiB cap (`too-large`), idle close (driven by a fake clock), and that exceeding one never affects an established splice of another room.
- **R5 (bandwidth).** Splice rate cap and per-room daily byte cap hold (fake clock): a burst up to the burst size passes (a 32 MiB snapshot through the relay succeeds at default limits), sustained transfer over the cap is throttled, the daily cap returns `rate-limited` for new joins only; a splice that does not start with a TLS ClientHello within 10 s is closed; 10 failed room lookups per minute from one source network get `banned` for 10 minutes.
- **R6 (back-pressure).** A joiner that stops reading cannot grow relay memory beyond the per-splice buffers and is closed at the idle cap; the relay stays responsive (a third client completes a handshake meanwhile).
- **R7 (directory).** A listing with a valid signature appears in `GET /v1/games` and disappears on `unlist`, on control-channel close and after 90 s without a ping and after 30 minutes without an `update` (fake clock); an over-long control line (4 KiB) is refused; stale `seq`, bad signature, wrong schema, over-size body, forbidden characters, deny-listed name, or more than the per-IP/per-key listing limit is rejected with `listing-rejected`; `--listing off` rejects with `listing-disabled`; `allowlist` admits only listed keys; filters and paging return the expected rows; read limit 30/min per IP; a client verifier rejects a relay-altered body, a swapped cert and a mismatched fp (unit tests in `Unciv.Client.Net`).
- **R8 (bans).** A banned IP/CIDR, key fingerprint or name pattern is refused at connect and an existing room of a newly banned key is closed after the reload.
- **R9 (peer id).** The host sees a stable opaque `peer` per joiner IP within a room, different across rooms, never the IP; the host's failed-password back-off applies per `peer` on relay connections (test: five bad passwords from joiner A do not lock out joiner B).
- **R10 (versions).** `unsupported-version` is reported with a clear UI message; listings with another `proto` appear greyed out and are not joinable.
- **R11 (reconnect).** Killing the relay (TCP reset) makes the host reconnect with backoff and reclaim the same room; a stable `relay-secret` keeps the code across a relay restart; existing sessions `Resume` and the game hash still matches.
- **R12 (nginx and standalone).** The same suite of connectivity tests (host + joiner through the relay) runs against (a) the relay with `--cert`, (b) `--self-signed` with a pinned relay fingerprint, (c) `--tls none --proxy-protocol` behind a test proxy that injects PROXY protocol headers (the relay uses the injected address for its limits; a header from an untrusted source is refused). The shipped nginx example is syntax-checked with `nginx -t` when nginx is installed (skipped with a message otherwise) and exercised manually on the owner's server by the deployment ticket (section 13).
- **R13 (no default).** A fresh client/server configuration has no relay URL; the Host screen offers "Reach me through a relay" only after one is set (with a link to the setting), and no source file, test fixture or default config contains a hostname other than `relay.example.org`/loopback (a source-policy test).
- **R14 (host migration).** `SuccessorInfo` carries `relay{relay, room}`; the standby successor's unlisted room is probed through the relay; promotion through a local relay reconnects every client with `Resume` and the same keys (extends the T273 harness scenario).
- **R15 (operations).** SIGTERM drains and tells hosts `closing: restart`; SIGHUP reloads certs and ban files; `/healthz` and `/v1/info` respond; logs contain no payload and the retention setting deletes old files (tests on the log sink); the Dockerfile builds and the container starts (manual when no docker is available; documented as UNVERIFIED then).
- **R16 (browser UI, T339).** The Browse games screen lists, filters, refreshes and joins as in the T339 deliverable list; empty, offline, relay-too-old, no-relay-configured and error states are distinct and tested; password prompt; mod/ruleset mismatch message (or T247 mod-sync offer); the host-listing checkbox is off by default; layout verified at the matrix window sizes (T311/T312/T316) with screenshots.

## 13. Owner questions (each with the recommended default) and follow-up tickets

The same list is in T336's Notes. Work proceeds on the defaults; the deployment ticket waits for the answers.

| Q | Question | Recommended default |
|---|---|---|
| Q1 | Which domain or subdomain should the relay answer on? | A dedicated subdomain `relay.<your-domain>` with A/AAAA records to the server; never the site's apex. |
| Q2 | Is the relay on the same machine as your nginx? | Yes: relay on loopback, nginx in front. If not, the relay runs standalone (`--cert/--key`) on its own VM. |
| Q3 | Does your nginx have the `stream` and `stream_ssl` modules (`nginx -V` shows `--with-stream --with-stream_ssl_module`; `--with-stream_ssl_preread_module` only if you want to share port 443; nginx 1.21.4 or newer for `ssl_alpn`)? | Assume `stream_ssl`; without it, run standalone. |
| Q4 | Dedicated public TCP port, or share 443 by SNI? | Dedicated port 47830 (no SNI map, no risk to the website). Choose 443 only if restrictive networks matter more than isolation. |
| Q5 | Who owns the TLS certificate for the relay name? | nginx's existing Let's Encrypt certificate for the subdomain, TLS terminated by nginx (`--tls none --proxy-protocol`). |
| Q6 | OS and process manager of the machine? | Linux with systemd (unit shipped); Docker/compose optional. |
| Q7 | Monthly transfer and CPU headroom, do you want a global bandwidth/monthly cap, and may players behind CGNAT share the per-network limits (2 rooms, 40 connections)? | One small VM; cap off (`--max-mbps 0`), per-room 20 GiB/day, 200 rooms. Estimated need is far below 100 GiB/month for 200 active friend games. |
| Q8 | Logging and retention: how long may IP addresses be kept, and who is the contact for abuse? | Connection logs with IPs 7 days, no payload ever; contact string set to an email you choose (shown in the browser). |
| Q9 | Listing policy: open to anyone who hosts, or only keys you allow? | `open`, listing off by default per game, max 200 listings, 2 per IP; switch to `allowlist` if abuse appears. |
| Q10 | Should YOUR release builds pre-fill your relay URL? | No default in the source. Your own packaging drops an uncommitted `relay-default.json` next to the client so your players get it pre-filled and visible (and editable) in settings. |
| Q11 | Do you want the web export (browser) to join and host through the relay (needs a WebSocket gateway)? | No for now; the ticket stays Available and waits for this answer. |
| Q12 | Should game listings be visible to people without the game (a plain web page of the directory)? | No; the JSON API is public but there is no web page. |
| Q13 | One relay for everyone, or several (a list in settings)? | One relay URL setting; people running their own paste theirs. |
| Q14 | Do you want a CDN/proxy such as Cloudflare in front? | No (raw TCP is not proxied on free plans, and it would see metadata). |

Follow-up tickets filed by T336 (all phase 3, see `phases.txt`):
- T337, T338, T339: the implementation, updated by this design (tunnel-based registration; `RelayListener`/`RelayDialer`; opaque peer ids; fingerprint-keyed known-hosts; room-code and relay-invite parsing; listing verification).
- **T354** host migration over the relay: R14.
- **T355** verify the nested TLS and the relay path on Windows, macOS and Android (UNVERIFIED manual steps like T249).
- **T356** deploy and verify on the owner's real server (waits for Q1-Q6, Q8): produce the owner's concrete nginx config, run the manual connectivity matrix, write down the result.
- **T357** WebSocket gateway for the web export (waits for Q11).
