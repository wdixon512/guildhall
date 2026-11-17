# Phase 5: Private Networking & Remote Streamable HTTP MCP Gateway

This guide covers connecting your Windows home host (running SQL Server + Ollama + `Guildhall.AgentHost`) to a remote DigitalOcean Droplet or secondary machine over a **private encrypted tunnel** (WireGuard or Tailscale) without exposing SQL Server (`1433`) or Ollama (`11434`) to the public internet.

---

## 1. Security Invariant

- **Never** open TCP `1433` (SQL Server) or TCP `11434` (Ollama) on public firewall interfaces (`0.0.0.0` inbound from `Any`).
- `Guildhall.AgentHost` binds to loopback (`127.0.0.1:43120`) by default and requires a 32–128 char bearer token (`GUILDHALL_SESSION_TOKEN`).
- Remote Streamable HTTP MCP servers registered in Guildhall must use `https://`, loopback `http://`, or an explicitly opted-in private tunnel IP (`10.x.x.x`, `172.16-31.x.x`, `192.168.x.x`, `100.64.0.0/10`, or `*.ts.net`). Plain `http://` to public internet hosts is rejected by `PrivateNetworkGuard`.

---

## 2. Option A: WireGuard Tunnel (Home Windows Host <-> DigitalOcean Droplet)

1. **On the DigitalOcean Droplet (Ubuntu):**
   - Install WireGuard (`apt install wireguard`) and create `/etc/wireguard/wg0.conf` with `Address = 10.66.0.1/24` and `ListenPort = 51820`.
   - Allow UDP `51820` in `ufw` (`ufw allow 51820/udp`). Keep all MCP/relay ports bound to `10.66.0.1` or `127.0.0.1`.
2. **On the Windows Home Host:**
   - Install WireGuard for Windows and import your peer config (`Address = 10.66.0.2/24`, `AllowedIPs = 10.66.0.0/24`).
   - Verify in Guildhall **Connections → Private Infrastructure & Remote MCP** that the WireGuard interface is detected and `1433`/`11434` are not exposed publicly.

---

## 3. Option B: Tailscale (Optional)

1. Install Tailscale on both the Windows host and the DigitalOcean Droplet and sign into the same tailnet.
2. Guildhall automatically detects Tailscale interfaces (`100.64.0.0/10`) and `.ts.net` MagicDNS hostnames in `PrivateNetworkProbeService` and `PrivateNetworkGuard`.
3. Optionally enable Tailscale HTTPS certificates (`tailscale cert <host>.ts.net`) for end-to-end TLS over the tailnet.

---

## 4. Authenticated Streamable HTTP MCP Gateway (`/mcp/gateway`)

- `Guildhall.AgentHost` exposes `POST /mcp/gateway` (JSON-RPC 2.0: `initialize`, `tools/list`, `tools/call`) protected by the host bearer token (`Authorization: Bearer <token>`).
- Only allowlisted **Tier 1 read-only** tools (`sql-server-mcp`: `sql_list_profiles`, `sql_list_objects`, `sql_describe_object`, `sql_query`) are exposed on `/mcp/gateway`.
- All queries pass through `SqlReadOnlyGuard` (`ApplicationIntent=ReadOnly`, single-statement `SELECT`/`WITH` only, row & timeout caps, `GuildhallDev` blocked) and `ExfiltrationGuard`.
