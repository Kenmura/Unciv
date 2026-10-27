# Multiplayer Hosting Guide

Unciv multiplayer uses direct player-to-player networking (TCP + TLS 1.3) with a self-signed host key and cryptographic identity validation. There is no central server, matchmaking VPS, or account registration required.

---

## 1. Quick Overview

When you host a multiplayer game:
- Your Unciv server listens on port **47821** (TCP) by default.
- It generates a self-signed **ECDSA P-256** certificate valid for 10 years and computes a normalized SHA-256 fingerprint.
- You can copy and share an **Invite Code** format:
  ```text
  unciv://<host>:<port>/<fingerprint>[/<token>]
  ```
- Connecting players who use your invite code have **zero first-use window**—the invite carries the cryptographic fingerprint of your host certificate, preventing Man-in-the-Middle (MITM) attacks.
- If joining directly via IP:Port without a fingerprint, Unciv uses **Trust-on-First-Use (TOFU)** and records the fingerprint in `known-hosts.json`. If the key ever changes unexpectedly, Unciv terminates the connection with a hard stop.

---

## 2. Network Configurations

Depending on your network topology, choose one of the following methods to allow other players to connect to your host:

### Option A: Local Area Network (LAN)
If all players are on the same local Wi-Fi or Ethernet network:
1. Find your local IP address:
   - **Linux / macOS**: `ip addr` or `ifconfig` (look for `192.168.x.x` or `10.x.x.x`).
   - **Windows**: `ipconfig` (IPv4 Address).
2. Start the game lobby in Unciv.
3. Share your invite code or local IP with players on the same network. No router configuration or port forwarding is required.

### Option B: Manual Port Forwarding (Public IPv4)
If you have a public IPv4 address and want players across the internet to join:
1. Log into your home router's admin portal (usually `192.168.1.1` or `192.168.0.1`).
2. Locate the **Port Forwarding** / **Virtual Server** section.
3. Add a forwarding rule:
   - **Service / Name**: `Unciv`
   - **Protocol**: `TCP`
   - **External / WAN Port**: `47821`
   - **Internal / LAN IP**: Your local machine's IP address (e.g. `192.168.1.50`).
   - **Internal / LAN Port**: `47821`
4. Find your external public IP by visiting an IP checker (or `curl -4 ifconfig.me`).
5. Share your invite code containing your public IP.

### Option C: IPv6 (No NAT / No Port Forwarding)
If your ISP provides native IPv6 connectivity:
1. Ensure your host machine has a global unicast IPv6 address (starts with `2xxx:...`).
2. Verify that your router's IPv6 firewall permits incoming TCP connections to port 47821 for your machine's IPv6 address.
3. Invite codes format IPv6 addresses in square brackets:
   ```text
   unciv://[2001:db8::1]:47821/A1B2C-D3E4F-...
   ```
4. IPv6 clients can connect directly without NAT traversal.

### Option D: Overlay VPNs (Tailscale / ZeroTier / WireGuard)
If you cannot forward ports due to Carrier-Grade NAT (CGNAT) or prefer not to expose public ports:
- **Tailscale**:
  1. Both the host and joining players install [Tailscale](https://tailscale.com) and sign in.
  2. The host copies their Tailscale IP (e.g., `100.x.y.z` or MagicDNS name).
  3. Other players on your tailnet connect using the Tailscale IP and port 47821.
- **ZeroTier**:
  1. Create a virtual network on [ZeroTier](https://www.zerotier.com).
  2. All players join the network and the network administrator authorizes them.
  3. Connect using the managed 10.x.x.x ZeroTier IP.
- **WireGuard**:
  1. Set up a point-to-point WireGuard tunnel between players.
  2. Connect directly over the tunnel interface.

---

## 3. Cryptographic Security Model

- **Transport Encryption**: All traffic is encrypted using **TLS 1.3** (`TLS_AES_128_GCM_SHA256` or `TLS_AES_256_GCM_SHA384`).
- **Framing**: Bounded length-prefixed frames (up to 64 KB per frame) guard against unbounded memory buffering and denial of service.
- **Host Certificate**: Stored in PKCS#12 (`host-key.pfx`) with an empty password, generated with ECDSA P-256.
- **Known-Hosts Pinning**: `known-hosts.json` maintains host-to-fingerprint mappings with timestamps. Changes trigger hard verification failures.
