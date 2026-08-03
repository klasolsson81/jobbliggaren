# VPS base hardening — Jobbliggaren production host

**Created:** 2026-08-03 (spår B, VPS lane)
**Host:** Netcup RS 1000 G12, Debian 13 (trixie), Nuremberg (EU)
**Source:** ADR 0050 pre-beta-data-gates B-1 / M-6; security-auditor 2026-08-03
**Scope:** base hardening only — no deploy, no application data, no DNS, no TLS

This runbook covers turning a freshly provisioned, password-exposed root server into an
access-controlled host with a proven rescue path. It is deliberately written to be executed
against a box that carries **no application data**, and it stops where the deploy work
starts. The deploy stack, reverse proxy, certificates and secret injection are owned by
[#196](https://github.com/klasolsson81/jobbliggaren/issues/196) and are **not** in scope here.

---

## 1. Scope and end-state invariants

Hardening is finished when all of the following hold, each proven by the command in §9 —
never asserted:

1. The **rescue path is proven before anything else changes**. A console session that has
   never been tested is not a rescue path.
2. SSH accepts **public keys only**, for **one named non-root user**. Password and
   keyboard-interactive authentication are refused; root is refused even with a valid key.
3. The **host firewall** filters inbound traffic with a default-drop policy and survives a
   reboot.
4. The **edge firewall** carries an explicit `DROP all` as the last own-rule **in both
   directions**. Allow-rules alone do nothing here (§6.1).
5. Egress is **verified from inside the box** after the rules are live: package updates,
   DNS, NTP and an outbound HTTPS fetch all still work.
6. **`ss -tlnp` lists only sshd on port 22**, before and after. Hardening adds no listener.
7. Security updates install themselves, and **no unattended reboot** can happen.
8. Nothing pages secrets to disk: **zram swap only, no disk swap, core dumps discarded**.

### Not in scope

Deploy, containers, application data, DNS cutover, TLS certificates, the reverse proxy,
secret injection, backups, and the log sink. `fail2ban` is deliberately **not** installed —
see the deviation log (§11).

---

## 2. Access inventory

| Item | Value / location |
|---|---|
| Host | `v2202608391467492778.supersrv.de` |
| Public IPv4 | `159.195.203.88` |
| Public IPv6 | `2a0a:4cc0:c2:afe5::/64` |
| SSH host key (Ed25519) | `SHA256:TDVIOqy4zBkU/HYG3P0bgT9SogWXtTun86F7ahM7nGk` |
| Admin user | `jpadmin` (sudo, NOPASSWD — see §11) |
| Operator key | `~/.ssh/jobbpilot_vps_ed25519` on Klas's workstation, **no passphrase** (§11) |
| Operator SSH alias | `jp-vps` (see §4.1) |
| SSH source restriction | one `/32`, recorded in the local `docs/current-work.md` — **deliberately not written here**, because this file is public |
| Control panel | Netcup SCP, 2FA enabled |
| API state | `~/.netcup/refresh_token` on the workstation, never in the repo |

**Two console identities exist on purpose.** The `root` password and the `jpadmin` password
both remain valid **at the VNC console**. Neither can be used over SSH after §4. Losing the
key must never mean losing the box, so **the root password is never locked** — it is the
rescue credential. Both live in Klas's password manager.

The host key fingerprint is published deliberately: it lets any future first connection be
verified against a value that was recorded before hardening began. Publishing a *public*
fingerprint is a security feature. **Never** put a private key body, a password, or a
password hash in this file.

---

## 3. Rescue paths, in order

Try them in this order. Each one is independent of the layer below it.

1. **SSH as `jpadmin`** — normal operation.
2. **Edge firewall edit in SCP** (browser). The control plane rides *your* internet
   connection, not the box's, so no firewall rule on the box can lock you out of it. This is
   the fix when the edge rules are the problem — including when your own source IP has
   changed (§6.5).
3. **VNC console ("Screen" in SCP)** — out-of-band, below the network stack entirely. This
   is what survives a wrong `nftables` ruleset, a broken `sshd_config`, and a box that boots
   into emergency mode. Log in as `root` or `jpadmin` with the console password.
4. **File-level restores on the box** — `/var/backups/hardening/` holds a timestamped copy
   of `/etc/ssh`, `/etc/fstab` and the pre-change nftables ruleset.

**Snapshots are not a rescue path here.** One exportable snapshot slot remains and it is
rationed for the migration work later in the lane. Restoring one also rolls back everything
else. Do not spend it on a hardening mistake — items 2–4 cover every failure mode in §10.

---

## 4. SSH configuration

### 4.1 Operator side

`~/.ssh/config` on the workstation:

```
Host jp-vps
    HostName 159.195.203.88
    User jpadmin
    IdentityFile ~/.ssh/jobbpilot_vps_ed25519
    IdentitiesOnly yes
    AddressFamily inet
    ServerAliveInterval 15
    ServerAliveCountMax 4
```

`AddressFamily inet` is load-bearing, not cosmetic. The edge rule that permits SSH is
**IPv4-only**; on a network that prefers IPv6 the connection would otherwise be dropped by
the edge and read exactly like a lockout. Pin the family and the failure cannot happen.

Every automated connection uses `-o BatchMode=yes` so that an unexpected prompt **fails
instead of hanging**.

### 4.2 Server side

Debian 13's `sshd_config` puts `Include /etc/ssh/sshd_config.d/*.conf` at the **top**, and
OpenSSH takes the **first** value it obtains for a keyword. Two consequences:

- Our file is named `00-hardening.conf` so it wins even if a vendor or cloud-init drop-in
  reappears later.
- Any pre-existing drop-in is renamed to `*.conf.disabled` (which removes it from the glob)
  rather than edited.

`/etc/ssh/sshd_config.d/00-hardening.conf`:

```
PermitRootLogin no
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
AuthenticationMethods publickey
AllowUsers jpadmin
MaxAuthTries 3
AllowTcpForwarding no
X11Forwarding no
```

If cloud-init is installed, also write `/etc/cloud/cloud.cfg.d/99-no-pwauth.cfg` containing
`ssh_pwauth: false`, so a regenerated drop-in cannot re-enable passwords.

**Port 22 is kept.** Moving it is noise reduction, not security (security-auditor,
2026-08-03). What is worth more and costs nothing is restricting port 22 to one source
address at the edge (§6.3).

### 4.3 Applying a change to sshd — the safe cycle

Never reload sshd without an escape hatch armed and the current session held open.

```bash
# 1. Back up
sudo tar -C /etc -czf /var/backups/hardening/etc-ssh-$(date +%Y%m%d%H%M%S).tgz ssh

# 2. Arm a dead-man revert (fires in 10 minutes unless cancelled)
sudo systemd-run --on-active=10min --unit=sshd-deadman --collect \
  /bin/sh -c "rm -f /etc/ssh/sshd_config.d/00-hardening.conf; systemctl reload ssh"

# 3. Validate syntax BEFORE reloading, then reload — keep the current session open
sudo sshd -t && sudo systemctl reload ssh
# Förväntat: no output from sshd -t; reload returns 0
```

Verify from **new** sessions (an already-open session proves nothing — it was authenticated
under the old configuration), then cancel the dead-man **and confirm it did not already
fire**:

```bash
sudo systemctl stop sshd-deadman.timer 2>/dev/null
systemctl list-timers --all | grep -c deadman     # Förväntat: 0
sudo sshd -T | grep ^passwordauthentication       # Förväntat: passwordauthentication no
```

That second check is not redundant. A dead-man that was left to fire silently restores
password authentication ten minutes later, and the box looks fine while it happens.

### 4.4 Key rotation

```bash
ssh-keygen -t ed25519 -a 100 -N "" -C "jpadmin@jobbliggaren-vps $(date +%Y-%m)" \
  -f ~/.ssh/jobbpilot_vps_ed25519_new
ssh-copy-id -i ~/.ssh/jobbpilot_vps_ed25519_new.pub jp-vps   # appends, does not replace
ssh -i ~/.ssh/jobbpilot_vps_ed25519_new jpadmin@159.195.203.88 'echo NEW-KEY-OK'
# only after that succeeds: remove the old key's line from authorized_keys, then the local files
```

Add the new key **before** removing the old one, and prove the new one works from a fresh
session in between. If the console password is ever lost as well, this ordering is the only
thing standing between a typo and a reinstall.

---

## 5. Host firewall — nftables

`/etc/nftables.conf`, loaded at boot by `nftables.service`:

```
#!/usr/sbin/nft -f
flush ruleset

table inet filter {
    chain input {
        type filter hook input priority filter; policy drop;

        iif "lo" accept
        ct state established,related accept
        ct state invalid drop

        icmp type { echo-request, echo-reply, destination-unreachable,
                    time-exceeded, parameter-problem } accept

        # NDP is mandatory. Measured 2026-08-03: the IPv6 default route is
        # "default via fe80::1 dev eth0" — a link-local next hop. Filter NDP and the
        # box loses its IPv6 path entirely the moment the router refreshes.
        icmpv6 type { echo-request, echo-reply, destination-unreachable, packet-too-big,
                      time-exceeded, parameter-problem, nd-router-advert,
                      nd-neighbor-solicit, nd-neighbor-advert, mld-listener-query } accept

        tcp dport 22 accept
        # Pre-opened for the deploy phase. Nothing listens yet, so the kernel answers RST —
        # zero exposure — and the RST is load-bearing for the edge probes in §6.4.
        tcp dport { 80, 443 } accept
    }
    chain forward {
        type filter hook forward priority filter; policy drop;
        # Docker (deploy phase) installs its own chains and punches its own holes.
        # This interaction MUST be revisited when containers land — see #196.
    }
    chain output {
        type filter hook output priority filter; policy accept;
        # Egress filtering is owned by the edge firewall (§6). One layer, one place to debug.
    }
}
```

**Why output is `accept`.** Filtering egress in both layers doubles the debugging surface —
every outage has to be chased through two rule sets — while adding little containment: an
attacker with root can rewrite `nftables`, but cannot reach the edge rules. Revisit this when
containers land and per-service egress control becomes meaningful.

**Why the host needs no UDP reply holes.** The host's conntrack is stateful for UDP too, so
replies match `ct state established`. The edge is stateless for UDP and therefore needs
explicit reply rules (§6.2) — and the host's default-drop is what backstops them: a packet
forged with source port 53 passes the edge and dies here unless it answers a real query.

Applying a ruleset uses the same dead-man discipline as §4.3, with `nft flush ruleset` as the
revert. Two checks matter afterwards:

```bash
sudo nft -c -f /etc/nftables.conf                      # Förväntat: no output (syntax OK)
sudo nft list chain inet filter input | grep "policy drop"
sudo systemctl enable --now nftables && systemctl is-enabled nftables   # Förväntat: enabled
```

`is-enabled` is not a formality. An unenabled service means the next reboot silently brings
up a completely unfiltered host, and nothing else in the system reports it.

---

## 6. Edge firewall — Netcup SCP

### 6.1 Semantics you must know before touching a rule

Measured against the live API on 2026-08-04. Several of these contradict what the SCP web UI
suggests, and each one has a failure mode attached:

- **The direction defaults are a configurable field, not an unchangeable trailing rule.**
  `ingressImplicitRule` and `egressImplicitRule` sit in the firewall object and each read
  `ACCEPT_ALL` on a fresh box — which is what "default-allow in both directions" actually
  means here.
- **Assigning a user policy flips that direction's default to `DROP_ALL` by itself.** Measured:
  a `PUT` that sent `ingressImplicitRule: "ACCEPT_ALL"` together with one INGRESS user rule
  came back as `DROP_ALL`. **The direction becomes default-deny the moment its first user rule
  lands**, so the full rule set must be complete *before* assignment, not built up afterwards.
  A **copied** policy does not do this: `netcup Mail block` has carried three EGRESS DROP rules
  all along while egress stayed `ACCEPT_ALL`. Copied and user policies are not equivalent, and
  conflating them is how the older "allow-rules alone are decoration" reading arose.
- **Stateful for TCP, stateless for UDP** — confirmed the hard way, see §6.4.
- **Established connections survive rule changes.** Your open SSH session is not evidence
  that the rules permit SSH. Every test must open a **new** connection.
- **Ports ranges use a hyphen** (`32768-60999`). A colon is rejected with HTTP 422.
- Two default **copied** policies are on and are **left alone**: `netcup Mail block` (outbound
  TCP 25/465/587 DROP) and `netcup Ping allow` (ICMP **and ICMPv6**, both directions). The
  second one is why this runbook writes no ICMP rules of its own — including NDP, which arrives
  as ICMPv6 and would otherwise need its own rule.
- Transactional mail goes over the SES HTTPS API — never SMTP — so there is never a reason to
  ask Netcup to open 587.

**A 2xx response does not mean the change landed.** A `PUT` assigning an inline `userPolicies`
object returned `HTTP 202` with a `PENDING` task, and the policy simply never appeared. User
policies must be created first at `POST /users/{userId}/firewall-policies` and then referenced;
the server resolves their rules by id. **Always read the configuration back and compare** — the
status code is not the measurement. Note also that *listing* policies returns them with empty
`rules` arrays, so a list response is not a safe thing to re-`PUT`.

**The user id is not the customer number.** `/users/403517/...` returns HTTP 403 while
`/users/235072/...` works. The correct value is the `id` claim inside the access token, not
`preferred_username`.

### 6.2 Ingress — user policy `jbl-ingress`

Both direction defaults are `DROP_ALL`. There is **no** explicit trailing DROP rule, because
the default is a field rather than a rule (§6.1).

| # | Proto | Source | Sport | Dport | Action | Why |
|---|---|---|---|---|---|---|
| 1 | TCP | `<ADMIN_SRC_IP>/32` | any | 22 | ACCEPT | SSH from the operator only |
| 2 | TCP | any | any | 80 | ACCEPT | future ACME/HTTP; nothing listens yet |
| 3 | TCP | any | any | 443 | ACCEPT | future HTTPS |
| 4 | UDP | any | 53 | 32768-60999 | ACCEPT | **DNS replies** — UDP is stateless here (§6.4) |
| 5 | UDP | any | 123 | 32768-60999 | ACCEPT | **NTP replies** — same reason |

`<ADMIN_SRC_IP>` is intentionally a placeholder: this file is public. The live value is in the
local `docs/current-work.md` and in the rule itself.

No ICMP rules: `netcup Ping allow` already covers ICMP **and ICMPv6** in both directions, which
includes NDP. No IPv6 SSH rule: the operator path is pinned to IPv4 (§4.1). No DHCP rules: the
box is statically configured (§6.6). The destination range is the box's own
`net.ipv4.ip_local_port_range` — check it rather than assuming, and widen these two rules if it
ever changes.

### 6.3 Egress — user policy `jbl-egress`

| # | Proto | Sport | Destination | Dport | Action | Why |
|---|---|---|---|---|---|---|
| 1 | TCP | any | any | 443 | ACCEPT | HTTPS: apt, container registry, SES later |
| 2 | TCP | any | any | 80 | ACCEPT | apt mirrors, OCSP |
| 3 | UDP | any | any | 53 | ACCEPT | DNS |
| 4 | TCP | any | any | 53 | ACCEPT | DNS over TCP (truncated answers) |
| 5 | UDP | any | any | 123 | ACCEPT | NTP |
| 6–8 | TCP | 22 / 80 / 443 | any | any | ACCEPT | **reply belt** — see below |

SMTP needs no rule: `netcup Mail block` drops 25/465/587 outbound, and the `DROP_ALL` default
covers everything else. Verified: port 587 outbound is dead.

**The reply belt (6–8) is deliberate redundancy.** TCP statefulness is measured only for
*outbound-initiated* connections. If the edge were asymmetric, replies to a **new inbound** SSH
connection would be dropped — while every already-open session kept working and hid it. Three
rules remove the failure mode. The residual exposure (a local process bound to source port
22/80/443 reaching an arbitrary destination) requires a root-level compromise to reach, and
SMTP stays blocked regardless. Whether the belt is load-bearing has **not** been isolated:
proving it would mean removing it and risking exactly the lockout it prevents.

### 6.4 Proving the rules actually filter

The textbook probe — an outside connection to a closed port flipping from *refused* to *timed
out* — **does not work here**, because the host firewall (§5) is applied first and already
times out everything else. Three signals survive, and together they are sufficient. Run each
one **at baseline first**: a probe whose "before" value was never measured proves nothing about
the "after".

**1. Port 22 from other source addresses.** This needs a vantage point outside the operator's
network. A phone on mobile data works; so does any external TCP-probe service, which is what
was used here and is easier to repeat:

```bash
RID=$(curl -s -H "Accept: application/json" \
  "https://check-host.net/check-tcp?host=159.195.203.88:22&max_nodes=4" | jq -r .request_id)
sleep 15
curl -s -H "Accept: application/json" "https://check-host.net/check-result/$RID" | jq
```

Measured 2026-08-04 — baseline: NL, UA and US all connected. After the ingress rules: **all
four nodes time out**, while the operator's own SSH keeps working. That difference *is* the
source restriction.

**2. RST pass-through on 443.** An accepted port with no listener must answer `connection
refused`, not time out. Measured across 8 nodes: 6 refused (including the Swedish one), 2 timed
out — the two are almost certainly filtering in their own networks, since a rule that dropped
443 would drop it for everyone. Re-check this when a listener actually exists.

**3. The egress flip.** `curl -4 -m5 http://portquiz.net:8080` succeeded at baseline and
**times out** once egress is default-deny.

**Do not test DNS through the provider's own resolver.** This nearly produced a wrong
conclusion. With ingress at `DROP_ALL` and no UDP reply rule, `getent hosts` still resolved
fine — suggesting the edge tracked UDP state. It does not. Netcup's resolvers (`46.38.x`) are
reachable on a path that never crosses the edge filter, so they answer regardless. Probing
external resolvers separated the cases:

| Target | Result | Meaning |
|---|---|---|
| `46.38.252.230` (netcup) | replied in 0.00 s | does not cross the edge — proves nothing |
| `9.9.9.9`, `8.8.8.8` | timed out | **UDP replies are dropped** |
| NTP `158.180.28.150` | timed out, then replied in 0.01 s once the rule was added | the reply rule is what fixes it |

Without those probes, NTP would have failed silently and the clock would have drifted for
months. **Always probe a target outside the provider's own network.**

### 6.5 Changing rules later

Rules can be edited in the SCP web UI or through the REST API (`https://api.netcup.com/api/v1/`,
OAuth2 device-code flow against the `scp` Keycloak realm; the refresh token stays valid as
long as it is used within 30 days). Either way:

1. `GET` the current configuration and **save it** — that file is the rollback.
2. Apply the change; re-apply/commit if the API requires it.
3. `GET` again and diff against the tables above.
4. Re-run the §6.4 probes on new connections.

**If your source IP changes** and SSH stops working, that is not a lockout. Confirm the new
address from any device (`curl ifconfig.me`), edit I1 in the SCP, and reconnect. Use the VNC
console in the meantime.

### 6.6 DHCPv4 — measured: not in use

Rows I8 and E11 exist only if the box actually uses a DHCP client. **Measured 2026-08-03: it
does not.** No `dhclient`/`dhcpcd` process runs, and the address is configured statically by
cloud-init through `/etc/network/interfaces.d/50-cloud-init.cfg`. Both rows are therefore
omitted.

Re-check this before any egress change if the network configuration is ever touched: a missed
lease renewal fails silently, hours later, and reads as a network outage rather than a
firewall rule.

---

## 7. Updates — unattended-upgrades

Security updates only, and **never** an automatic reboot: a reboot at an arbitrary hour is
its own outage, and this box will soon carry a key that only exists in RAM.

`/etc/apt/apt.conf.d/52unattended-upgrades-local`:

```
// Jobbliggaren: security-only, never auto-reboot
#clear Unattended-Upgrade::Origins-Pattern;
Unattended-Upgrade::Origins-Pattern {
        "origin=Debian,codename=${distro_codename}-security,label=Debian-Security";
};
Unattended-Upgrade::Automatic-Reboot "false";
```

**The `#clear` is mandatory.** APT configuration lists *append* across files, so a drop-in
that simply states an `Origins-Pattern` widens the set instead of narrowing it. Without
`#clear` the result is the opposite of what the file appears to say.

`/etc/apt/apt.conf.d/20auto-upgrades`:

```
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
```

Verify:

```bash
sudo unattended-upgrade --dry-run --debug 2>&1 | grep -A3 "Allowed origins"
# Förväntat: exactly one origin, the -security one
apt-config dump | grep Automatic-Reboot        # Förväntat: "false"
systemctl is-active apt-daily.timer apt-daily-upgrade.timer   # Förväntat: active active
```

**What this does not cover:** container image contents. `unattended-upgrades` patches the
host only. Base-image CVE cadence is a separate mechanism (a `docker` ecosystem entry in
Dependabot) and belongs to the phase that starts publishing images.

**During risky cutovers, stop the timers** (`systemctl stop apt-daily.timer
apt-daily-upgrade.timer`). An unattended run holding the dpkg lock in the middle of a
firewall verification reads exactly like an egress failure. Use `stop`, not `disable`, so a
reboot restores them.

---

## 8. Memory and crash hygiene

ADR 0050 gate B-1 requires that the future master key **never reaches disk**. Two mechanisms
would break that promise behind your back, so both are closed here, before any key exists.

**zram instead of disk swap.** `/etc/systemd/zram-generator.conf`:

```ini
[zram0]
zram-size = min(ram / 2, 4096)
compression-algorithm = zstd
swap-priority = 100
```

`systemd-zram-generator` is chosen over `zram-tools`: one declarative file, device lifecycle
owned by a native systemd generator, no shell-script service. **No `writeback-device` is
configured** — swapped pages must stay in RAM; that is the entire point.

**Writing the config is not enough on a running system.** Measured 2026-08-03: the device had
already been created, so `systemctl start` applied the *size* from the new config but left the
compression algorithm at the default `lz4`. An active swap device cannot be reconfigured in
place. Recreate it:

```bash
sudo swapoff /dev/zram0
sudo systemctl stop systemd-zram-setup@zram0.service
echo 1 | sudo tee /sys/block/zram0/reset >/dev/null
sudo systemctl start systemd-zram-setup@zram0.service
zramctl        # Förväntat: ALGORITHM zstd
```

This applies only to the first setup on a running box. At boot the generator creates the
device from the config directly — verified across a reboot: `zstd` from the start, with no
intervention. Note that `zramctl` and `swapon` live in `/usr/sbin`, which is **not** on the
PATH of a non-interactive SSH session; call them by full path from scripts.

Disk swap is removed. Back up `/etc/fstab` first, `swapoff` everything that is not zram,
comment out the swap lines, and then:

```bash
findmnt --verify        # Förväntat: 0 parse errors
swapon --show           # Förväntat: only /dev/zram0
```

`findmnt --verify` is the gate that matters: a malformed fstab boots into emergency mode, and
you would discover it at the reboot rather than at the edit.

Companion sysctls in `/etc/sysctl.d/90-zram.conf` — `vm.swappiness = 100` and
`vm.page-cluster = 0`. Swapping to zram costs CPU rather than disk I/O, so being eager is
correct, and read-ahead batching buys nothing for a RAM-backed device.

**Core dumps discarded.** `/etc/systemd/coredump.conf.d/90-disable.conf`:

```ini
[Coredump]
Storage=none
ProcessSizeMax=0
```

A crash dump of the process holding the master key is precisely the mechanism that would copy
that key to persistent storage. If `/proc/sys/kernel/core_pattern` does not route to
`systemd-coredump`, also set `kernel.core_pattern = |/bin/false` via `sysctl.d`, otherwise
the kernel writes `core` files into the crashing process's working directory.

Verify with an actual crash, not by reading the config:

```bash
cd /tmp && bash -c 'kill -SEGV $$'
ls -A /var/lib/systemd/coredump/ 2>/dev/null; ls /tmp/core* 2>/dev/null
# Förväntat: no files in either location
```

---

## 9. Verification battery

Run after any change to this box, and in full after every reboot.

```bash
ss -tlnp                                   # Förväntat: only sshd, :22 and [::]:22
sudo sshd -T | grep -E "^(passwordauthentication|permitrootlogin|allowusers|maxauthtries) "
                                           # Förväntat: no / no / jpadmin / 3
sudo nft list ruleset | grep -E "policy (drop|accept)"
                                           # Förväntat: input drop, forward drop, output accept
systemctl is-enabled nftables ssh          # Förväntat: enabled enabled
swapon --show; zramctl                     # Förväntat: only /dev/zram0, zstd
timedatectl | grep -E "synchronized|NTP service"    # Förväntat: yes / active
sudo apt-get update -q >/dev/null && echo APT-EGRESS-OK
getent hosts deb.debian.org >/dev/null && echo DNS-OK
curl -6 -m8 -sI https://deb.debian.org >/dev/null && echo V6-EGRESS-OK
cat /proc/sys/kernel/core_pattern
```

Negative checks, from the workstation — a hardening claim is only proven by what is
**refused**:

```bash
# password path dead (expect "Permission denied (publickey)." and NO password prompt)
ssh -o PubkeyAuthentication=no -o PreferredAuthentications=password,keyboard-interactive \
    jpadmin@159.195.203.88
# root refused even with a valid key
ssh -i ~/.ssh/jobbpilot_vps_ed25519 -o BatchMode=yes root@159.195.203.88 true
# edge filtering (from a different source address, e.g. a phone): port 22 must time out
# accept rules still pass: fast "connection refused", not a timeout
curl -m5 telnet://159.195.203.88:443
```

---

## 9.1 Verification log

Every claim below was produced by the command next to it, on the date given. A line without a
measurement is not a result.

**2026-08-03 — host layer, verified across a reboot:**

| Property | Measured value | Instrument |
|---|---|---|
| Rescue path | root shell obtained at the VNC console **before** any SSH change | Netcup SCP → Screen |
| Key login | `jpadmin`, fingerprint `SHA256:c1iqLV7QPTDrW/pUI8xC3mwV2Jdtxpn6EPdEhPGl73I` | `ssh -o BatchMode=yes` from a new session |
| Password login | refused — server advertises only `(publickey)`, previously `(publickey,password)` | `ssh -o PreferredAuthentications=password` |
| Keyboard-interactive | refused | `ssh -o PreferredAuthentications=keyboard-interactive` |
| Root over SSH | refused **even with a valid key** | `ssh -i <key> root@…` |
| Host firewall bites | port 9876 flipped REFUSED → **TIMEOUT**; port 443 stayed REFUSED | `/dev/tcp` probe, before and after |
| Listeners | only `sshd` on `0.0.0.0:22` and `[::]:22` | `ss -tlnp` |
| nftables persistence | `enabled` + `active`; input drop / forward drop / output accept | after reboot |
| zram | `/dev/zram0`, **zstd**, 3.9 G, priority 100; no disk swap anywhere | after reboot |
| Core dumps | `core_pattern = |/bin/false`; no files produced by a real `SIGSEGV` | live crash test |
| Auto-patching | exactly one origin: `origin=Debian,codename=trixie-security,label=Debian-Security` | `unattended-upgrade --dry-run --debug` |
| Auto-reboot | `"false"` | `apt-config dump` |
| Egress | DNS, apt, and IPv6 HTTPS all reachable | from inside the box |
| Dead-man timers | 0 remaining, and the hardening survived — neither timer fired | `systemctl list-timers --all` |

**2026-08-04 — edge layer, verified across a second reboot:**

| Property | Measured value | Instrument |
|---|---|---|
| Both direction defaults | `ingressImplicitRule` and `egressImplicitRule` = `DROP_ALL` | `GET …/firewall` |
| Port 22 from outside | **times out from all probe nodes**; baseline was NL/UA/US all connecting | check-host.net, 4 nodes |
| Port 443 from outside | `connection refused` from 6 of 8 nodes incl. Sweden — accepted, no listener | check-host.net, 8 nodes |
| Operator SSH | unaffected throughout | new session each time |
| DNS / NTP / apt / IPv6 HTTPS | all working from inside | direct probes, external targets |
| UDP is stateless at the edge | NTP timed out, then replied in 0.01 s once the reply rule landed | raw UDP socket |
| Egress default-deny bites | portquiz:8080 reachable at baseline, **times out** after | `curl` from the box |
| Outbound SMTP | dead (587) | `curl telnet://…:587` |

**Incident, same session.** During bootstrap the root password was typed into the console's
*username* field, so it was written to the journal as a failed login name (measured: 1 hit).
noVNC also proved unusable for pasting: a 542-character command arrived with characters
dropped and reordered (`/etc/sudoers.d/90-jpadmin` became `/etc/sudoetc/`), which bash rejected
outright — so nothing executed. The key was installed with `ssh-copy-id` from the operator's
own terminal instead, the root password was rotated once key access was proven, and the
interim root `authorized_keys` was removed. **Do not paste long commands into noVNC.** Use it
to read output and to run short, hand-typed commands.

---

## 10. Failure modes and recovery

| # | Situation | How you detect it | Recovery |
|---|---|---|---|
| 1 | noVNC keyboard layout mangles the password — the rescue path is fiction | Type the password's special characters into the *username* field before relying on it | Stop. Resolve through Netcup's password-reset facilities before touching SSH |
| 2 | sshd change locks you out (wrong `AllowUsers`, wrong key) | A new session is refused within seconds | The open session still works; the dead-man reverts within 10 min; VNC console |
| 3 | Dead-man was never cancelled — password auth silently returns | `systemctl list-timers` + `sshd -T` after every cutover | Re-apply the drop-in and reload |
| 4 | cloud-init regenerates a drop-in enabling passwords | `sshd -T` in the §9 battery | `00-` prefix wins; `ssh_pwauth: false` is set; delete the file if it reappears |
| 5 | Malformed `/etc/fstab` → emergency mode at boot | `findmnt --verify` at edit time; no SSH after reboot | VNC console; restore fstab from `/var/backups/hardening/` |
| 6 | `nftables.service` not enabled → unfiltered host after reboot | `systemctl is-enabled nftables` | `systemctl enable --now nftables` |
| 7 | Wrong `Origins-Pattern` → zero security updates, silently, forever | The `--dry-run` check shows more or fewer than one origin | Fix the drop-in; re-run the dry run |
| 8 | Your source IP changed; SSH looks like a lockout | `curl ifconfig.me` from another device | Edit I1 in the SCP (browser path is unaffected); VNC in the meantime |
| 9 | Egress DROP-all cuts DNS, NTP or apt | The §9 battery fails from inside immediately | Disable E12 in the SCP, fix the gap, re-apply |
| 10 | Edge is asymmetrically stateful → replies to new inbound SSH die | Open a **new** inbound SSH session right after E12 | The reply belt (E8–E10) pre-empts this; otherwise disable E12 |
| 11 | Box does not come back from a reboot | No SSH after ~6 minutes | VNC console → `systemctl --failed`, `journalctl -xb` |
| 12 | Temptation to burn the last snapshot during an incident | — | Don't. Items 2–4 in §3 cover every row in this table |

---

## 11. Deviation log

Decisions that depart from an earlier written expectation, recorded so they are reviewable
rather than discovered:

- **`fail2ban` is not installed**, although ADR 0050 gate M-6 lists it. security-auditor,
  2026-08-03: with `PasswordAuthentication no` and `PermitRootLogin no` it "defends against a
  brute force that cannot succeed", and the application already has per-account lockout and
  per-IP limiting. What replaces it is worth more and costs nothing: port 22 restricted to one
  source address at the edge. Install it later if quiet logs are wanted — as a log-hygiene
  measure, not a security control.
- **`NOPASSWD` sudo for `jpadmin`.** Non-interactive automation cannot answer a sudo prompt.
  Combined with a passphrase-less key this means key theft equals root. The account password
  still exists and is required at the console, so the rescue identity is unaffected. Narrowing
  this is a live option once the deploy automation's real command set is known.
- **The operator key has no passphrase.** SSH-agent plumbing under Git Bash is unreliable for
  background automation. Mitigations: the edge restricts port 22 to one source address, the
  VNC/SCP rescue paths do not depend on the key, and rotation is a two-command procedure
  (§4.4).
- **ICMP is permitted outbound (E6/E7)** beyond the originally scoped egress set. ICMPv6 is
  not optional — NDP is how IPv6 works — and ICMPv4 carries PMTUD, whose absence produces
  connections that establish and then hang on large payloads.
- **Host `output` policy is `accept`** rather than a second filtering layer; rationale in §5.
- **`jpadmin` has passwordless sudo and the operator key has no passphrase**, so possession of
  that key is equivalent to root on this host. Both are recorded above; the combination is what
  a reviewer should weigh, not either half alone.

---

## 12. Notes for the phases that follow

- **Docker will conflict with §5.** It writes its own chains and bypasses a naive host
  firewall; every container port must be bound explicitly to `127.0.0.1`, and the `forward`
  policy interaction must be re-examined. This is not optional: the dev compose file binds
  five of six ports to `0.0.0.0`, including an unauthenticated Seq.
- Ports 80 and 443 are already open at both layers, so bringing up the reverse proxy needs no
  firewall change.
- **Outbound mail stays impossible** and must remain so: SES is called over its HTTPS API.
- The `_FILE` secret seam, the key-in-tmpfs work, and the mandatory second security audit of
  the actual production configuration are gates for the *first real user data*, not for this
  runbook. See ADR 0050's pre-beta-data gates.

---

## 13. References

- [`docs/decisions/0050-deployment-migration-aws-exit-hetzner.md`](../decisions/0050-deployment-migration-aws-exit-hetzner.md) — gates B-1 (master key never plaintext on disk) and M-6 (hardening baseline)
- [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) — deploy stack; owns everything in §12
- [`CLAUDE.md`](../../CLAUDE.md) §11 — tooling and the dev-boot config contract
