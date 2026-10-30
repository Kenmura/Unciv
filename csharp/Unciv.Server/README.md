# Unciv.Server: run guide for the dedicated server

<!-- toc -->
## Contents

- [Get it](#get-it)
- [Host a game](#host-a-game)
- [Ports and firewall](#ports-and-firewall)
- [Data directory](#data-directory)
- [Invite and TLS fingerprint pinning](#invite-and-tls-fingerprint-pinning)
- [Resume with --game-id](#resume-with---game-id)
- [Stopping and exit codes](#stopping-and-exit-codes)
- [systemd unit example](#systemd-unit-example)
- [Smoke test of a published build](#smoke-test-of-a-published-build)
<!-- /toc -->

`Unciv.Server` hosts one multiplayer game on a TCP+TLS port with no window and no host player: friends join with an invite, pick a seat and ready up, and the server runs the game
(simultaneous turns, AI for empty seats). It is the same host code as the **Host** button of the client (`design/SERVER.md` section 6.5), packaged as a program for an always-on machine.
There are no accounts, no web UI and no remote admin console.

## Get it

- **Release archive.** `Unciv-server-linux-x64.tar.gz` or `Unciv-server-win-x64.zip` from the GitHub release (check them against `SHA256SUMS`; in a local release the Windows folder may be absent if its build failed, which only warns). Each unpacks to one folder
  `Unciv-server-<rid>/` with the executable, the bundled .NET runtime (nothing to install), `data/jsons` (the rulesets, found next to the executable), `LICENSE` and this guide as `README.md`.
- **Local release build.** `python3 -I migration/tools/release.py T###` writes `csharp-releases/<ticket>-<sha>/server-linux-x64/` and `server-win-x64/` (in the main checkout).
- **From source.** `dotnet publish Unciv.Server -c Release -r linux-x64 --self-contained true -o out` (or `-r win-x64`), then copy `android/assets/jsons` to `out/data/jsons`,
  or pass `--jsons DIR`. `dotnet run --project Unciv.Server -- --help` also works from a checkout.
- **Status.** The Linux build is smoke-tested in CI (`--help`, start, `LISTENING` line, SIGTERM, exit 0). The **Windows build is UNVERIFIED**: it is published and archived but has never been run.

## Host a game

```
./Unciv.Server --port 47821 --data-dir /srv/unciv --game-id friends --max-players 4 \
    --auto-start --min-humans 2 --password-file /srv/unciv/password --address play.example.org --print-invite
```

1. The server prints `LISTENING 47821`, then `INVITE unciv://play.example.org:47821/<fingerprint>[/<token>]`. Send that invite to your friends: in the client's join screen
   they paste it and join. Without `--address` the invite names `localhost`, which only works on the same machine.
2. Everybody picks a seat and readies up. With `--auto-start` the game begins once every seated human is ready and at least `--min-humans` have joined. Without it **the lobby never
   starts** (a dedicated server has no host player and no admin console), so use `--auto-start` for every real game.
3. Stop it with Ctrl+C or SIGTERM (see Stopping). Start it again with the same `--data-dir` and `--game-id` to continue (see Resume).

`--help` lists every option. The ones you need most:

| Option | Meaning |
| --- | --- |
| `--port N` | TCP port, default 47821 (0 = any free one; the bound port is the `LISTENING` line). |
| `--data-dir DIR` | Host key and saved games live here, default `./unciv-server-data`. |
| `--game-id ID` | Save the game as `DATA-DIR/games/ID` and resume it if it exists. Without it nothing is saved and the server warns. |
| `--password-file F` | Join password from the first line of F. Prefer it to `--password`, which is visible in `ps`. |
| `--max-players N`, `--ai-fill` / `--no-ai-fill` | Seats, and whether empty seats become AI. |
| `--auto-start`, `--min-humans N` | Start by itself when the humans are ready. |
| `--settings FILE` | Map, speed, difficulty, mods, turn timer as `LobbySettings` JSON; the options above override it. |
| `--address HOST` | Name or address written into the invite. |
| `--log-level L` | `debug`, `info` (default), `warn`, `error`; logs go to stdout. |

## Ports and firewall

- One TCP port, default **47821**, carrying TLS. Nothing else is opened (no UDP, no web port). The listener is dual-stack (IPv4 and IPv6).
- **Same LAN:** allow the port in the host's firewall and use the LAN address in `--address`.
- **Over the internet:** forward TCP 47821 on your router to the host machine (or use a public IPv6 address with the firewall open), then give friends your public address or a DNS name.
  Test from another network. How to reach a host, including a router port forward and the overlay-VPN alternative (Tailscale, ZeroTier, WireGuard: no router changes, works behind CGNAT,
  put the overlay address in `--address`), is in `migration/design/SERVER.md` section 6.5 ("Reaching the host"). There is no relay and no phone-home service.
- **Linux firewalls:** `sudo ufw allow 47821/tcp`, or `sudo firewall-cmd --permanent --add-port=47821/tcp && sudo firewall-cmd --reload`.
- **Windows:** allow the program when the Windows Defender Firewall prompts (private networks for a LAN game), or
  `netsh advfirewall firewall add rule name="Unciv server" dir=in action=allow protocol=TCP localport=47821`. (UNVERIFIED: the Windows build was never run.)
- Port numbers below 1024 need privileges on Linux: keep a high port and forward it.

## Data directory

```
DATA-DIR/
  host-key.pfx            the host's TLS identity (self-signed ECDSA P-256), created on first start
  games/
    .ID.lock              held while a server runs this game: a second server on the same game refuses to start (exit 1)
    ID/
      game.json           lobby settings (without the password), seats and their player keys, host key fingerprint, turn timer policy
      commands.log        append-only log of every accepted command
      snapshot-<seq>.json.gz   full game state (a normal Unciv save: it also opens in single player)
      clean               written at a clean stop; its absence means the last stop was a crash
```

Keep `host-key.pfx` (mode 600, never share it): losing it changes the fingerprint, and every player then sees a "host key changed" stop.

## Invite and TLS fingerprint pinning

The server presents a self-signed certificate, so there is no certificate authority to trust. Instead the **fingerprint** (SHA-256 of the host key's public key, first 128 bits, grouped as
`SSFU2-PNMD6-...`) is part of the invite `unciv://host:port/fingerprint[/token]`. A client joining through an invite checks it before sending anything, so a man in the middle cannot slip in.
A client that types only `address:port` is shown the fingerprint once and asks "trust this host?" (like SSH's known hosts); later changes are a hard stop.

- The invite is printed only with `--print-invite`; start the server with it whenever you need the invite again (the fingerprint only changes if `host-key.pfx` does). Read the fingerprint to friends
  over another channel if you want to be strict.
- With a password set, `--print-invite` also mints a join token (`/token`, valid for 24 hours, redeemed when a friend uses it, so one friend per invite) so they need not type the password;
  friends without the token must give the password. The plain password is never logged or stored (only a hash is kept in `game.json`). Tokens live in memory only: after a restart, print a new invite.
- TLS 1.3 is preferred, TLS 1.2 is the fallback, both with the pinned key (`design/SERVER.md` 6.3a, 6.4).

## Resume with --game-id

Start with the same `--data-dir` and `--game-id` and the server loads `games/ID` before opening the port: the lobby is closed, the seats keep their keys, the saved turn timer rules apply
(`--max-players`, `--min-humans`, `--ai-fill` and the turn options of `--settings` are ignored, and the log says so), and a round that was interrupted is finished. Players reconnect with the
same client and the same identity key; they do not need the password again. The log prints the state hash at resume, which equals the hash printed at the last save. A crash (no `clean`
marker) is recovered from the latest snapshot plus the command log, with warnings listing anything lost. If the folder cannot be loaded (corrupt, or from rulesets that are missing) the server exits **3**.

**Backup and moving to another host** (`design/SERVER.md` 5.3, manual host change): stop the server (so `clean` is written), copy the whole `games/ID` folder, **and** `host-key.pfx` if you
want the same fingerprint, to the new machine's data dir (zip it and hand it to a friend), and start `Unciv.Server --data-dir ... --game-id ID --address new-host`. Players reconnect with the same keys
to the new address; if the host key is new the fingerprint differs, so send a fresh invite (a client that pinned the old fingerprint refuses the new one). Back up while the server runs only by copying a snapshot file; the log is being appended.

## Stopping and exit codes

- **SIGINT (Ctrl+C) and SIGTERM** (Linux and macOS): the server closes the port to new joins, finishes the running command and any round being resolved, saves, writes `clean`, tells the players the host
  is closing and exits **0**. A second signal while it saves ends the process at once without saving further (exit 130 or 143). **Windows:** only Ctrl+C is supported (UNVERIFIED).
- Exit codes: **0** stopped normally (also after a signal), **1** failure while running (port in use, game folder in use, lobby that cannot start, internal error),
  **2** bad command line or settings, **3** the saved game under `--game-id` cannot be loaded. A supervisor should restart on 1 but not on 2 or 3.

## systemd unit example

UNVERIFIED example (written for this guide, never run under systemd). Create a user, put the unpacked folder in `/opt/unciv-server`, a password in `/etc/unciv/password` (mode 600, owned by that user).

```ini
# /etc/systemd/system/unciv-server.service
[Unit]
Description=Unciv dedicated server
After=network-online.target
Wants=network-online.target

[Service]
User=unciv
WorkingDirectory=/opt/unciv-server
ExecStart=/opt/unciv-server/Unciv.Server --port 47821 --data-dir /var/lib/unciv-server --game-id friends \
    --max-players 4 --auto-start --min-humans 2 --password-file /etc/unciv/password --address play.example.org
# SIGTERM (the default) saves and exits 0; give it time to finish a round
KillSignal=SIGTERM
TimeoutStopSec=60
Restart=on-failure
# exit 2 (bad options) and 3 (unloadable save) will not fix themselves
RestartPreventExitStatus=2 3
StateDirectory=unciv-server
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/var/lib/unciv-server

[Install]
WantedBy=multi-user.target
```

`sudo systemctl daemon-reload && sudo systemctl enable --now unciv-server`, then `journalctl -u unciv-server -f` for the log and the `INVITE` line.

## Smoke test of a published build

```
python3 -I migration/tools/release_assets.py smoke-server <unpacked Unciv-server-linux-x64 folder>
```

(from `csharp/`) runs the binary with `PATH=/usr/bin:/bin` and no `DOTNET_ROOT` (it must bring its own runtime): `--help`, then a start on a free port that must print `LISTENING <port>`, then SIGTERM and exit 0. It prints `OK` and exits 0 when all pass.
CI (`csharp.yml`, job `server-publish`) and the release workflow run exactly this on the unpacked archive.
