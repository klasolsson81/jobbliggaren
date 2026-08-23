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
4. Both edge direction defaults (`ingressImplicitRule`, `egressImplicitRule`) read
   `DROP_ALL`, **verified by reading the firewall object back** — not by the presence of a
   trailing DROP rule, which does not and must not exist (§6.1). The accept rules are complete
   *before* the policy is assigned.
5. Egress is **verified from inside the box** after the rules are live: package updates,
   DNS **against an external resolver**, NTP and an outbound HTTPS fetch all still work.
6. **`ss -tlnp` lists only sshd on `0.0.0.0:22`.** Hardening adds no listener, and
   deliberately removes the IPv6 one it started with (§4.2).
7. Security updates install themselves, **no unattended reboot** can happen, and the
   pending-reboot flag is on the verification battery — auto-patching without a reboot signal
   stops covering the kernel and libc the moment the first kernel patch lands (§7).
8. Nothing pages secrets to disk: **zram swap only, no disk swap, no zram writeback device,
   core dumps discarded**.
9. ⚠ **The published privacy policy names THIS box's CITY, so moving the box is a legal-copy
   change.** Since 2026-08-09 (#1199) `messages/{sv,en}/content-legal.json` states that the
   service runs on a server at netcup GmbH in **Nuremberg** — a data-subject-facing Art. 13(1)(e)
   disclosure, not an internal note. A reprovision to any other netcup location (Vienna,
   Amsterdam, Manassas, Singapore) **falsifies published copy** and must be changed in the same
   change. Regenerate the affected set rather than working from a list — a hand-written
   enumeration rots, which is `release-checklist.md` §2.6 point 1's rule applied here:

   ```bash
   grep -rn "Nürnberg\|Nuremberg\|Nurnberg" \
     web/jobbliggaren-web/messages docs/runbooks docs/decisions BUILD.md README.md
   ```

   Sweep **both spellings and the umlaut-less form**; a single-language sweep closes nothing.
   Measured against exactly this command, 2026-08-09: **32 lines across 9 files**, of which
   **nine are in `docs/runbooks/gdpr-processing-register.md`** (eight Art. 30 sub-processor
   entries plus one Chapter V passage) — the legally heaviest, and the ones a two-file fix would
   leave false.

   ⚠ **The paths are enumerated deliberately and `docs/` as a whole is NOT swept.** A wholesale
   `docs/` sweep drags in `docs/sessions/`, `docs/reviews/` and `docs/current-work.md`, which must
   **never** be rewritten at a reprovision: a session log, a review report, or a dated
   `BOXEN ÄR MÄTT` block that acquires a new city is falsified history, not a corrected record.
   Read that list as the CLASS, not as an exhaustive enumeration — any dated record of what was
   measured then belongs to it, including present-tense sentences sitting under a dated heading,
   which assert what held THEN and not what holds now. It also makes the count unstable for reasons that have
   nothing to do with the box — a review report landing mid-sweep moves it. If you widen the
   paths, regenerate the number from the widened form or drop the number and let the command be
   the only answer (`release-checklist.md` §2.6 point 1's doctrine, which this invariant invokes,
   applies to the number as much as to the list).

   **No test can catch this.** A copy tripwire fails a silent *deletion*; here the string
   survives the move and CI stays green, so the obligation is hooked at the action that
   falsifies it. It is an invariant rather than a note in §12 because a reprovision can happen
   at any time — hardware failure, capacity, price — and is not a phase transition or a release.

### ADR 0050 gate M-6, clause by clause

M-6 is the hardening baseline this runbook exists to close. It names six things, and this is
where each one stands — so that "a runbook exists and its battery is green" is never mistaken
for "M-6 is done":

| M-6 clause | Status | Where |
|---|---|---|
| SSH key-only | met, measured | §4, §9.1 |
| Firewall | met, measured, both layers | §5, §6, §9.1 |
| fail2ban | **deviation, now recorded** — replaced by source restriction. Written up in **ADR 0050 `Amendment 2026-08-04` §6**, which is authoritative; **ratification still awaits Klas's GO** and the row does not read "accepted" until it lands | §11, ADR 0050 |
| Auto-patch | met, measured | §7, §9.1 |
| PG/Redis not public | **open** — no such service exists yet; today held by `forward policy drop` + the edge default-deny | #196, §12 |
| Swap / core-dump hygiene | met, measured (including no zram writeback device) | §8, §9.1 |

Gate B-1 (master key never plaintext on disk) is **MET — verified GREEN 2026-08-16** (#198;
`vps-deploy-stack.md` rows 21–25 carry the measurements). ~~*prepared*, not met~~ — true while no
key existed; the key was then injected, rotated to `local-v3`, and the journal measured clean for
all four secrets. §8 still closes the two mechanisms that would page it to disk. ⚠ **Closing B-1
did not release the first real data**, and neither did discharging Art. 28: the corpus load is owned
by **Klas's explicit written GO** — a decision, not a derivable state
(`release-checklist.md` §2.6 point 3.5); #1240 owns the load itself.
**No discharged gate, ticked box or closed issue is permission.**

### Not in scope

Deploy, containers, application data, DNS cutover, TLS certificates, the reverse proxy,
secret injection, backups, and the log sink.

---

## 2. Access inventory

| Item | Value / location |
|---|---|
| Host | `v2202608391467492778.supersrv.de` |
| Public IPv4 | `159.195.203.88` |
| Public IPv6 | `2a0a:4cc0:c2:afe5:b404:b6ff:fef6:8fa4/64` (measured on the box with `ip -6 addr`; the hostname has no AAAA record) |
| SSH host key (Ed25519) | `SHA256:TDVIOqy4zBkU/HYG3P0bgT9SogWXtTun86F7ahM7nGk` |
| Admin user | `jpadmin` (sudo, NOPASSWD — see §11) |
| Operator key | `~/.ssh/jobbpilot_vps_ed25519` on Klas's workstation, **no passphrase** (§11) |
| Operator SSH alias | `jp-vps` (see §4.1) |
| SSH source restriction | one `/32`, recorded in the local `docs/current-work.md` — **deliberately not written here**, because this file is public |
| Control panel | Netcup SCP, 2FA enabled |
| API credential | `~/.netcup/refresh_token` on the workstation, mode 600, never in the repo. **Treat as a credential, not as state:** it is pre-authenticated against the API that owns the firewall rules, so it bypasses the SCP's 2FA. Valid while used within 30 days. |
| Edge rollback file | The `GET` of the firewall object (§6.5) contains the real `<ADMIN_SRC_IP>`. Keep it beside `~/.netcup/refresh_token` — **never in a worktree**, since gitleaks does not flag IP addresses and would not catch it. |

**Two console identities exist on purpose.** The `root` password and the `jpadmin` password
both remain valid **at the VNC console**. Neither can be used over SSH after §4. Losing the
key must never mean losing the box, so **the root password is never locked** — it is the
rescue credential. Both live in Klas's password manager.

**The host key fingerprint is published deliberately** — it lets any future first connection be
verified against a value recorded before hardening began, which defeats a first-connection
machine-in-the-middle. That reasoning is specific to the **host** key, which the server sends
to every client anyway.

**It does not generalise to the operator's key**, and that fingerprint is deliberately absent
here. Nothing transmits it, so publishing it defends nothing — an operator can always read it
locally with `ssh-keygen -lf`. What it *would* provide is a matching oracle: given the threat
this runbook itself names in §11, an infostealer harvesting `~/.ssh` could turn "one of ten
thousand stolen keys" into "the key to this production host" offline, without ever probing the
box — and `LogLevel VERBOSE`, the only detective control here, never sees that lookup. In a
public repository the exposure is permanent from the moment it merges.

**Never** put a private key body, a password, a password hash, or a client-key fingerprint in
this file.

---

## 3. Rescue paths, in order

Try them in this order. **They are independent of each other against box-side failure** — a
wrong nftables ruleset, a broken `sshd_config`, a box in emergency mode. That is the failure
class this section is for.

They are **not** independent against credential compromise. All four ultimately trace back to
the operator's workstation: the SSH key lives there without a passphrase, and so does
`~/.netcup/refresh_token`, a pre-authenticated bearer credential against the same API that owns
the firewall rules — which is to say it walks past the SCP's 2FA. One compromised workstation
yields root on the host *and* the control plane *and* every rescue path. That is the argument
for §11's open question about the passphrase-less key, and it is why the token belongs in the
access inventory as a credential rather than as "API state".

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

### 4.0 Bootstrap — from root-with-password to a key-only admin

The starting state is a freshly provisioned box where root logs in with a password. Everything
below assumes `jpadmin` already exists with a key; this is how it gets there. Run it **before**
§4.2, because §4.2 closes the password path this step depends on.

At the **VNC console**, as root — never by pasting into noVNC, which drops characters (§9.1).

**Run the keyboard-layout probe from §10 row 1 first.** The console's layout is `en-us`; if it
does not match yours, `adduser` sets a password that is not the one you wrote down, and you find
out only when the rescue path is needed. Verify the new password with a fresh console login
**before** §4.2 closes the password path.

```bash
adduser jpadmin                                    # set a console password; store it in the password manager
printf 'jpadmin ALL=(ALL) NOPASSWD:ALL\n' > /etc/sudoers.d/90-jpadmin
chmod 0440 /etc/sudoers.d/90-jpadmin
visudo -c                                          # Förväntat: "parsed OK" — never skip this
install -d -m 0700 /var/backups/hardening          # §4.3 writes here; it must exist first
```

From the operator's **own terminal**:

```bash
ssh-keygen -t ed25519 -N "" -f ~/.ssh/jobbpilot_vps_ed25519   # -a rounds do nothing without a passphrase
ssh-copy-id -i ~/.ssh/jobbpilot_vps_ed25519.pub jpadmin@159.195.203.88
ssh -i ~/.ssh/jobbpilot_vps_ed25519 jpadmin@159.195.203.88 'sudo -n true && echo BOOTSTRAP-OK'
```

That last line is the gate: it proves the key **and** passwordless sudo before §4.2 removes the
password fallback. Do not proceed without `BOOTSTRAP-OK`.

Then restrict the key host-side, so the restriction does not depend on netcup's control plane
(a single edit in the SCP would otherwise remove the only source restriction):

This edit is the **only** change in this runbook that takes effect instantly — there is no
`reload` step between writing it and it being live, and a wrong address (or the placeholder
`<ADMIN_SRC_IP>` left in literally) locks you out immediately. It therefore gets the same
backup-and-dead-man discipline as §4.3, and §4.3's dead-man does **not** cover it — that one
only removes `00-hardening.conf`.

```bash
# ON THE BOX (via ssh or the console):
STAMP=$(date +%Y%m%d%H%M%S)
sudo cp -a /home/jpadmin/.ssh/authorized_keys /var/backups/hardening/ak-$STAMP
sudo systemd-run --on-active=10min --unit=ak-deadman --collect \
  /bin/cp -a /var/backups/hardening/ak-$STAMP /home/jpadmin/.ssh/authorized_keys

sudo sed -i 's|^ssh-ed25519|from="<ADMIN_SRC_IP>" ssh-ed25519|' /home/jpadmin/.ssh/authorized_keys
# ON THE WORKSTATION — a NEW session, before trusting the change:
ssh -i ~/.ssh/jobbpilot_vps_ed25519 jpadmin@159.195.203.88 'echo STILL-OK'

# BACK ON THE BOX, only after STILL-OK — and confirm the dead-man had not already fired:
sudo systemctl stop ak-deadman.timer 2>/dev/null
systemctl list-timers --all | grep -c deadman      # Förväntat: 0
grep -c '^from=' /home/jpadmin/.ssh/authorized_keys # Förväntat: 1 (the restriction survived)
```

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
ListenAddress 0.0.0.0
LogLevel VERBOSE
```

**`ListenAddress 0.0.0.0` removes the IPv6 listener on purpose.** The edge's `DROP_ALL` default
is proven over IPv4 only — the external probe service used in §6.4 cannot target IPv6 — so
whether it is address-family-agnostic is **unmeasured**. Rather than depend on an unmeasured
property for the single load-bearing ingress control, the dependency is removed: with no
`[::]:22` there is nothing for a v6 path to reach. The operator path is already pinned to IPv4
(§4.1), so this costs no functionality. Re-open it only after measuring the edge's v6 behaviour.

Note that the `00-` prefix does **not** protect this particular directive: `sshd_config(5)`
makes `ListenAddress` an explicit exception to first-obtained-wins — multiple occurrences are
permitted and all take effect. A later drop-in adding `ListenAddress ::` would re-open the v6
listener regardless of ordering. The `ss -tlnp` line in §9 is what catches that.

**`LogLevel VERBOSE` is the one line that records *which key* authenticated.** Everything else
in this runbook is preventive; this is the only detective control, and it matters precisely
because key theft is the dominant threat against a passphrase-less key (§11).

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

# ssh-copy-id appends the .pub file VERBATIM, and a .pub file carries no from= restriction.
# Re-apply it to any line that lacks one, or the rotation silently drops the mitigation
# that §11 records against an accepted risk:
ssh -o BatchMode=yes jp-vps \
  'sudo sed -i "s|^ssh-ed25519|from=\"<ADMIN_SRC_IP>\" ssh-ed25519|" /home/jpadmin/.ssh/authorized_keys'

# Pin the identity: without these an agent-loaded OLD key can also answer NEW-KEY-OK
ssh -i ~/.ssh/jobbpilot_vps_ed25519_new -o IdentitiesOnly=yes -o IdentityAgent=none \
  jpadmin@159.195.203.88 'echo NEW-KEY-OK'
# only after that succeeds: remove the old key's line from authorized_keys, then the local files

# Verify no line lost its restriction — Förväntat: no output
ssh -o BatchMode=yes jp-vps \
  'grep -v "^#" /home/jpadmin/.ssh/authorized_keys | grep -v "^$" | grep -v "^from="'
```

Add the new key **before** removing the old one, and prove the new one works from a fresh
session in between. If the console password is ever lost as well, this ordering is the only
thing standing between a typo and a reinstall.

That last check belongs in the §9 battery too, and is there: the battery otherwise reads
`sshd -T` and `ss`, never `authorized_keys`, so a dropped `from=` would go unnoticed
indefinitely.

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

Applying a ruleset uses the same dead-man discipline as §4.3 — but **not `nft flush ruleset` as
the revert**. A flush restores access by leaving the host completely unfiltered, which is itself
a failure mode (§10 row 6), and `systemctl restart nftables` would just re-read the broken file.
The revert must restore the *previous* ruleset:

```bash
STAMP=$(date +%Y%m%d%H%M%S)
{ echo "flush ruleset"; sudo nft list ruleset; } \
  | sudo tee /var/backups/hardening/nft-$STAMP.rules >/dev/null
sudo systemd-run --on-active=10min --unit=nft-deadman --collect \
  /usr/sbin/nft -f /var/backups/hardening/nft-$STAMP.rules
```

The leading `flush ruleset` line is what separates a working revert from one that duplicates
every rule — `nft list ruleset` does not emit it. Cancel it the same way as §4.3, including the
confirmation that it had not already fired:

```bash
sudo systemctl stop nft-deadman.timer 2>/dev/null
systemctl list-timers --all | grep -c deadman            # Förväntat: 0
# 0 timers alone does NOT distinguish "I stopped it in time" from "it fired and --collect
# swept it away". Ask whether the unit ever ran — Förväntat: no output.
sudo journalctl -u nft-deadman --no-pager -q | grep -i "started\|finished"
```

Apply the same question to the other two dead-men in this runbook (`sshd-deadman`,
`ak-deadman`): a timer count of zero is the same number either way, and the difference is
whether your hardening is still live.

Two checks matter afterwards:

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
- Transactional mail goes over the provider's HTTPS API — Scaleway Transactional Email in
  `fr-par` since #183 — never
  SMTP, so there is never a reason to ask Netcup to open 587.

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
| 1 | TCP | any | any | 443 | ACCEPT | HTTPS: apt, container registry, Scaleway TEM |
| 2 | TCP | any | any | 80 | ACCEPT | apt mirrors, OCSP |
| 3 | UDP | any | any | 53 | ACCEPT | DNS |
| 4 | TCP | any | any | 53 | ACCEPT | DNS over TCP (truncated answers) |
| 5 | UDP | any | any | 123 | ACCEPT | NTP |

SMTP needs no rule: `netcup Mail block` drops 25/465/587 outbound, and the `DROP_ALL` default
covers everything else. Verified: port 587 outbound is dead.

**There is no reply belt, and the reason is a measurement.** An earlier version carried three
`sourcePorts` 22/80/443 ACCEPT rules as insurance: TCP statefulness had only been observed for
*outbound-initiated* connections, and if the edge were asymmetric, replies to a **new inbound**
SSH connection would be dropped while every already-open session kept working and hid it.

That looked impossible to test without risking the lockout it prevented — but only if you test
on port 22. The hypothesis is a property of the edge, not of a port. So the **443** rule alone
was removed and port 443 re-probed from 8 external nodes: all 8 still answered `connection
refused`. That RST is itself an outbound packet with source port 443, sent with no egress rule
matching it. **The edge is stateful for inbound-initiated connections too**, the belt was
redundant, and all three rules were removed — verified afterwards with a new inbound SSH
session. Port 22 was never touched during the test.

Removing them also closes what the belt cost: a local process bound to source port 22/80/443
could otherwise reach any destination, and publishing that in a public runbook told a
post-compromise attacker exactly which egress channel was open.

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
refused`, not time out. Measured twice: first 6 of 8 nodes refused (including the Swedish one),
later 8 of 8. The result is therefore stable in the direction that matters, and whatever caused
the two initial timeouts was transient rather than a property of the rules — so do not read a
non-unanimous result as evidence of a broken rule. Re-check when a listener actually exists.

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

Rules can be edited in the SCP web UI or through the REST API. Everything below is a public
parameter — no secret appears in this section.

```bash
BASE=https://www.servercontrolpanel.de/scp-core/api/v1
REALM=https://www.servercontrolpanel.de/realms/scp/protocol/openid-connect

# One-time authorisation (OAuth2 device code). Open the printed URL and approve; the code
# expires in 10 minutes, so generate it when you are at the browser.
curl -s -X POST "$REALM/auth/device" -d client_id=scp
curl -s -X POST "$REALM/token" -d client_id=scp \
  -d grant_type=urn:ietf:params:oauth:grant-type:device_code -d device_code=<from above>
# Store .refresh_token beside ~/.netcup/ (mode 600). Valid while used within 30 days.

# Every call needs a fresh access token:
AT=$(curl -s -X POST "$REALM/token" -d client_id=scp \
       -d grant_type=refresh_token -d refresh_token="$(cat ~/.netcup/refresh_token)" \
     | jq -r .access_token)

# The user id is the token's `id` claim — NOT the customer number (§6.1).
# The firewall object lives per interface MAC:
curl -s -H "Authorization: Bearer $AT" \
  "$BASE/servers/<serverId>/interfaces/<mac>/firewall"

# Policies are created at user level first, then referenced by the server:
curl -s -X POST -H "Authorization: Bearer $AT" -H "Content-Type: application/json" \
  "$BASE/users/<userId>/firewall-policies" -d @policy.json
```

A rule object is `{action, description, direction, protocol, sources[], sourcePorts,
destinations[], destinationPorts}` — see §6.2/§6.3 for the applied values. Port ranges use a
hyphen (`32768-60999`); a colon is rejected 422.

**`<userId>` comes from the access token, not from the customer number.** Decode the JWT's
payload and read its `id` claim — `preferred_username` is the customer number and returns 403:

```bash
echo "$AT" | cut -d. -f2 | base64 -d 2>/dev/null | jq -r .id
```

**The write that attaches a policy to the interface** is the step whose obvious form is measured
to fail silently (§6.1). What works: create the policy at user level, then `PUT` the firewall
object with the policy **referenced**, letting the server resolve its rules by id. Do not send
an inline rules array — that returns `202` and lands nothing.

```bash
# 1. GET the current firewall object and keep it — that file is the rollback
curl -s -H "Authorization: Bearer $AT" \
  "$BASE/servers/<serverId>/interfaces/<mac>/firewall" > ~/.netcup/fw-backup.json
# NOT into a worktree: this file contains the real <ADMIN_SRC_IP>, and gitleaks does not
# flag IP addresses. An IP tied to a person is personal data (Art. 4(1), Breyer C-582/14).

# 2. attach our policies by NAME, then PUT the whole object back.
#    Assigning every policy the account owns would silently attach unrelated ones the day a
#    third exists — on the interface that carries the only ingress control.
jq --argjson p "$(curl -s -H "Authorization: Bearer $AT" \
      "$BASE/users/<userId>/firewall-policies")" \
   '.userPolicies = [$p[] | select(.name | startswith("jbl-"))]' \
   ~/.netcup/fw-backup.json > ~/.netcup/fw-new.json
curl -s -X PUT -H "Authorization: Bearer $AT" -H "Content-Type: application/json" \
  "$BASE/servers/<serverId>/interfaces/<mac>/firewall" \
  --data-binary @"$HOME/.netcup/fw-new.json"   # not @~/… — tilde does not expand after @

# 3. read back and compare — the 202 proves nothing
curl -s -H "Authorization: Bearer $AT" \
  "$BASE/servers/<serverId>/interfaces/<mac>/firewall"   | jq -c '{ingressImplicitRule, egressImplicitRule,
             policies: [.userPolicies[] | {name, n: (.rules|length)}]}'
# Förväntat: both DROP_ALL, jbl-ingress n=5, jbl-egress n=5 — compare against §6.2/§6.3.
```

Note that step 2's policy listing returns rules as **empty arrays**; that is fine here because
the server resolves them by id, but it is why a listing response must never be treated as a
complete object to re-`PUT` elsewhere. Either way:

1. `GET` the current configuration and **save it** — that file is the rollback.
2. Apply the change; re-apply/commit if the API requires it.
3. `GET` again and diff against the tables above.
4. Re-run the §6.4 probes on new connections.

**If your source IP changes** and SSH stops working, that is not a lockout. Confirm the new
address from any device (`curl ifconfig.me`), edit the SSH rule (§6.2 row 1) in the SCP, and reconnect. Use the VNC
console in the meantime.

### 6.6 DHCPv4 — measured: not in use

DHCP rules in either table exist only if the box actually uses a DHCP client. **Measured 2026-08-03: it
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

**Patches install, but they do not activate.** With `Automatic-Reboot "false"` — which is the
right call for a host that will hold a RAM-only master key — a kernel or libc update sits
installed and inert until someone reboots. Nothing here reads `/var/run/reboot-required`, so
invariant 7 would quietly stop covering the kernel from the first kernel patch onward. The flag
is on the §9 battery for exactly that reason, and the reboot cadence is a deploy-phase decision
(#196): every reboot also destroys the RAM-held key and requires re-injection.

**What this does not cover:** container image contents. `unattended-upgrades` patches the host
only. Base-image CVE cadence needs a `docker` ecosystem entry in `.github/dependabot.yml` —
**measured 2026-08-04: it does not exist**, the file carries only `npm` (×2), `nuget` and
`github-actions`. It must land in the same PR as the first image publish, or base images are
patched by nothing at all.

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
ss -tlnp                                   # Förväntat: only sshd on 0.0.0.0:22 — no [::]:22
sudo sshd -T | grep -E "^(passwordauthentication|permitrootlogin|allowusers|maxauthtries|loglevel) "
                                           # Förväntat: no / no / jpadmin / 3 / VERBOSE
sudo nft list ruleset | grep -E "policy (drop|accept)"
                                           # Förväntat: input drop, forward drop, output accept
systemctl is-enabled nftables ssh          # Förväntat: enabled enabled
/usr/sbin/swapon --show; /usr/sbin/zramctl # Förväntat: only /dev/zram0, zstd
cat /sys/block/zram0/backing_dev           # Förväntat: none — B-1 depends on this, so measure it
timedatectl | grep -E "synchronized|NTP service"    # Förväntat: yes / active
cat /var/run/reboot-required 2>/dev/null || echo "no reboot pending"
                                           # a pending reboot means kernel/libc patches are NOT live
sudo apt-get update -q >/dev/null && echo APT-EGRESS-OK
dig +short +time=3 +tries=1 @9.9.9.9 deb.debian.org >/dev/null && echo DNS-OK
                                           # MUST use an external resolver — see §6.4
                                           # (dig ships in bind9-dnsutils, not the base install)
curl -6 -m8 -sI https://deb.debian.org >/dev/null && echo V6-EGRESS-OK
cat /proc/sys/kernel/core_pattern          # Förväntat: |/bin/false

# Invariant 4 — the edge defaults, read back rather than assumed (see §6.5 for auth):
#   GET .../servers/<id>/interfaces/<mac>/firewall
# Förväntat: ingressImplicitRule and egressImplicitRule both DROP_ALL

# Invariant: every key line still carries its source restriction. Förväntat: no output.
grep -v '^#' ~/.ssh/authorized_keys | grep -v '^$' | grep -v '^from='
```

**The DNS line uses `@9.9.9.9` deliberately.** `getent hosts` goes through
`/etc/resolv.conf` to netcup's own resolvers, on a path that never crosses the edge filter —
so it prints `DNS-OK` even with the UDP reply rule deleted. It would report success on a
broken firewall. §6.4 documents that trap; this battery is where it would otherwise be
re-introduced, in the one instrument an operator actually runs.

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
| Key login | `jpadmin`, key fingerprint verified against the operator's own `.pub` | `ssh -o BatchMode=yes` from a new session |
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

**2026-08-04 — changes made in response to review:**

| Property | Measured value | Instrument |
|---|---|---|
| IPv6 SSH listener | **removed** — `ss` shows only `0.0.0.0:22` | `ListenAddress 0.0.0.0`; the edge's v6 behaviour is unmeasured, so the dependency was removed rather than trusted |
| Edge is stateful for **inbound**-initiated TCP | 8/8 nodes still `refused` on 443 with the reply rule deleted | the RST is itself an outbound packet from sport 443 |
| Reply belt | **deleted** — proven redundant by the line above; new inbound SSH still works | `ssh` from a new session afterwards |
| zram writeback device | `none` — B-1 now measured, not asserted | `cat /sys/block/zram0/backing_dev` |
| Host-side source restriction | `from="<ADMIN_SRC_IP>"` in `authorized_keys`; new session still works | independent of netcup's control plane |
| SSH logging | `loglevel VERBOSE` — records *which key* authenticated | `sshd -T` |
| Journal cleaned | the exposed string went 1 hit → **0**, and journald is the only sink — `rsyslog` is not installed, so no `/var/log/auth.log` survives it | `journalctl --rotate && --vacuum-time=1s`; `dpkg -l rsyslog` |
| Pending reboot | none | `/var/run/reboot-required` |
| External-resolver DNS | works — the battery no longer measures through netcup's resolver | `dig @9.9.9.9` |

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
| 1 | noVNC keyboard layout mangles the password — the rescue path is fiction | Type a **fixed probe string** covering the same character classes — `!"#¤%&/()=?` — into the *username* field. **Never the password's own characters:** whatever is typed there is logged as a failed login name, which is precisely how this session leaked one (§9.1) | Stop. Resolve through Netcup's password-reset facilities before touching SSH |
| 2 | sshd change locks you out (wrong `AllowUsers`, wrong key) | A new session is refused within seconds | The open session still works; the dead-man reverts within 10 min; VNC console |
| 3 | Dead-man was never cancelled — password auth silently returns | `systemctl list-timers` + `sshd -T` after every cutover | Re-apply the drop-in and reload |
| 4 | cloud-init regenerates a drop-in enabling passwords | `sshd -T` in the §9 battery | `00-` prefix wins; `ssh_pwauth: false` is set; delete the file if it reappears |
| 5 | Malformed `/etc/fstab` → emergency mode at boot | `findmnt --verify` at edit time; no SSH after reboot | VNC console; restore fstab from `/var/backups/hardening/` |
| 6 | `nftables.service` not enabled → unfiltered host after reboot | `systemctl is-enabled nftables` | `systemctl enable --now nftables` |
| 7 | Wrong `Origins-Pattern` → zero security updates, silently, forever | The `--dry-run` check shows more or fewer than one origin | Fix the drop-in; re-run the dry run |
| 8 | Your source IP changed; SSH looks like a lockout | `curl ifconfig.me` from another device | Edit the SSH rule (§6.2 row 1) in the SCP (browser path is unaffected); VNC in the meantime |
| 9 | Egress default-deny cuts DNS, NTP or apt | The §9 battery fails from inside immediately | Set `egressImplicitRule` back to `ACCEPT_ALL` — it is a **field, not a rule** (§6.1) — fix the gap, set `DROP_ALL` again |
| 10 | An edge rule change locks out SSH | New session refused while the browser still works | SCP/API is unaffected by box-side rules: restore from the saved `GET` (§6.5); VNC console meanwhile |
| 11 | Box does not come back from a reboot | No SSH after ~6 minutes | VNC console → `systemctl --failed`, `journalctl -xb` |
| 12 | Temptation to burn the last snapshot during an incident | — | Don't. Items 2–4 in §3 cover every row in this table |

---

## 11. Deviation log

Decisions that depart from an earlier written expectation, recorded so they are reviewable
rather than discovered:

- **Host and sizing depart from ADR 0050 Beslut 2** — a Netcup RS 1000 G12 (8 GB) instead of a
  Hetzner CAX31 (16 GB), a sizing that ADR explicitly rejected. **Ratified by Klas 2026-08-04:
  Hetzner is off the table and Netcup is the host going forward**, and the earlier "Swedish VPS"
  direction is withdrawn on price and performance grounds — to be revisited only if a Swedish
  option reaches comparable terms. So this is a decision, not drift. **Recorded 2026-08-04 in ADR 0050's
  `Amendment 2026-08-04`** (authoritative; ADR 0122 carries the rationale but is local —
  if it is absent from your worktree the amendment is sufficient and you are missing no
  gate). It supersedes ADR 0050 Beslut 2 in full (plus Beslut 3's host reference,
  Beslut 4's Cloudflare half and backup target, and gate M-5). Capacity conditions: §12.

  The residency question is separate, and **its two halves have different sources** (`country:`
  and `netname:` are registration attributes the LIR sets itself, so they do not prove
  physical datacentre placement; ADR 0050's amendment carries the full caveat). RIPE gives
  `netname DE-NETCUP-KVM`, `country DE` and `route`/`origin AS197540` — **the country**. RIPE
  carries **no `geoloc:`**, and the only city in the object is the contact address **Karlsruhe**
  (netcup GmbH's registered seat, not this server). **The city — Nuremberg — comes from netcup's
  own control panel for this box**, measured 2026-08-03 over SCP + SSH. The host leg is
  EU-resident, so there is no Chapter V transfer here; the "Swedish VPS" point was a preference,
  never a compliance one. *(Attribution corrected 2026-08-09, #1199.)*

  ⚠ **The city is a published, data-subject-facing claim** — the obligation that follows from that
  lives in **§1 invariant 9**, not here. This section is a register of decisions taken; an
  obligation recorded only in a deviation log is one nobody executes.
- **No Cloudflare** (Klas decision K3) — Caddy will go straight to Let's Encrypt. That is why
  §6.2 opens 80/443 to `any` rather than to Cloudflare ranges as ADR 0050 gate M-5 prescribes.
  **The superseder has landed: ADR 0050's `Amendment 2026-08-04` retires M-5** into
  **M-5a** (origin TLS is the whole TLS story) and **M-5b** (the edge exposure is
  unmitigated) — see that amendment for their normative text; ADR 0122 carries only the
  rationale. **Do not "correct" these rules toward M-5's original text** — ACME
  HTTP-01 dies, and M-5's origin-IP lockdown has no mechanism left without a CDN. The residual
  exposure (no DDoS absorption, no origin hiding) is written out in that amendment's §5 —
  tracked, and readable without the local ADRs — and is re-read by the
  mandatory second security audit before first real data.
- **`fail2ban` is not installed**, although ADR 0050 gate M-6 lists it. With
  `AuthenticationMethods publickey`, `AllowUsers jpadmin`, `PermitRootLogin no` and port 22
  restricted to one source address, it would defend an authentication path that does not exist
  against a population that cannot reach the port — while adding a root-running log parser that
  consumes attacker-controlled input. **Two limits on that verdict:** it covers SSH only (when
  80/443 gain a real listener, HTTP abuse is a separate question), and M-6 requires a hardening
  *baseline*, not fail2ban as a product — the baseline is met without it. **Now recorded where
  the reviewer will look: ADR 0050 `Amendment 2026-08-04` §6**, at the M-6 row itself — a runbook
  cannot amend an Accepted ADR. **Klas's GO is still outstanding**, so that row reads "deviation
  recorded, ratification pending", never "accepted".
- **`NOPASSWD` sudo for `jpadmin` combined with a passphrase-less operator key.** Non-interactive
  invocations cannot answer a sudo prompt, and SSH-agent plumbing under Git Bash is unreliable for
  background work — but together these mean **key theft equals root**, and root means the master
  key out of process memory once it exists.

  **What the source restriction buys, stated precisely — it is neither useless nor sufficient.**
  The dominant theft vector is a commodity infostealer reading `~/.ssh` on the workstation. Such
  malware exfiltrates the key and uses it from the attacker's own infrastructure, and `from=`
  **defeats exactly that** — which is the common case, so the control is worth having twice
  (edge and `authorized_keys`, the latter independent of netcup's control plane, §4.0). What it
  does not stop: `<ADMIN_SRC_IP>` is a **public** address, so behind NAT every device on the
  operator's network satisfies it. A compromised phone or router on the same LAN passes, as does
  a resident implant that proxies through the workstation. And on a consumer line the address is
  normally **dynamic** — this runbook assumes that elsewhere (§6.5, §10 row 8) — so the
  restriction names whatever address the ISP currently leases, and a released lease can be handed
  to another subscriber who then satisfies `from=` until someone notices. The real granularity is
  *whoever currently holds that address*, not *the operator's machine*.

  **A second correction:** after NOPASSWD, the `jpadmin` console password is not a
  lower-privilege second identity — it is root. §2's "two console identities" is redundancy for
  availability, not a privilege boundary.

  **Both named mitigations are VOID AS WRITTEN, measured 2026-08-17** — the ADR that named them is
  gitignored, so the derivation is written here to stand alone. They were: a separate key for
  automation with `restrict,command=,from=`, and narrowing NOPASSWD to a `Cmnd_Alias` once the
  deploy automation's real command set was known. Both assume an **inbound automation actor**. The
  delivered architecture is reconcile-pull — the timer runs on this box, as root, under systemd,
  and no workflow SSHes in. Regenerate with
  `grep -rniE 'jpadmin|ssh -i|ssh-action|appleboy|SSH_PRIVATE' .github/workflows/` (expect no
  output), read against `jobbliggaren-reconcile.service`'s header, which states the model in its
  own words. **The single SSH principal is the operator — Klas interactively, CC over
  `BatchMode`** (§4.1), whose command set is unbounded, so `command=` cannot bind it. So mitigation
  1 has no actor to hand a restricted key to, and mitigation 2's command set is the **operator's**.

  ⚠ **Mitigation 2 as written now instructs the next reader to build a boundary that is not one.**
  Its blocking clause was *"not known until #196 exists"*; #196 closed 2026-08-08, so the clause
  reads satisfied today. Regenerate what a reader would write the alias over:
  `grep -rhoE 'sudo +(-[a-zA-Z]+ +)*[/a-zA-Z0-9_.-]+' docs/runbooks/*.md deploy/ | sed -E 's/.*sudo +//; s/^-[a-zA-Z]+ +//' | sort | uniq -c | sort -rn`.
  Several entries are individually root-equivalent — `docker` mounts `/` in a container,
  `tee`/`sed -i`/`install`/`cp` write any file, `chmod` sets setuid, `apt-get` runs a maintainer
  script, and `find -exec`/`sh`/`systemd-run` execute anything. An alias over that set grants root
  under another name, and a control that **reads** as a privilege boundary while not being one is
  worse than the honest `NOPASSWD:ALL` it would replace.

  ⚠ **And the exclusion is on the CLASS, not on this measured set — a curated subset is not a
  way round it.** The census above is dated and mutable, so a reader who narrows the set and
  re-measures will find fewer root-equivalent members and read that as progress. It is not, for
  two reasons that do not depend on the census:
  **1. Every operable subset keeps at least one root-equivalent member.** Operating this box
  means starting and stopping units and applying reconciles, so any subset that still lets the
  operator do that retains `systemctl` and `docker` — and `docker` alone is root by
  construction (`-v /:/host`, `--pid=host`, a read of `/proc/<pid>/mem`). Remove them and the
  operator can no longer run the box; keep them and the alias grants root under another name.
  **2. The axis is wrong, and on the axis that matters no operable subset is a boundary.** A
  `sudo` command restriction bounds what may be **changed** — an integrity boundary. The risk
  this ADR names is **disclosure**: root reads the master key out of process memory, and out of
  a `0400` file on tmpfs. ⚠ **So a read-only subset is the WORST case here, not the boundary
  case** — it removes every write path and not one read path, and a read is exactly how the key
  leaves the box. That is not hypothetical: `host-detection.md` §5's D1 drill reads that file
  with `sudo dd`.
  A boundary subset would have to contain **neither** primitive — nothing that can read an
  arbitrary root-readable file, which discloses the key directly, and nothing that can write a
  file or execute code, which discloses it through root one unit or one container later. Read
  the census above against that test: `tee`/`sed -i`/`install`/`cp`/`chmod` write,
  `apt-get`/`find -exec`/`sh`/`systemd-run` execute, and `docker` does both. Nothing that starts
  a unit, applies a reconcile or installs a file survives the test — so for an operable box the
  boundary does not exist, and what would survive is not a NARROWING of `NOPASSWD` but the
  removal of operator sudo.
  **What would actually reduce this risk is a model where root holds no readable master key** —
  and the two mechanisms this repo has weighed for that were measured **exhausted on this host
  2026-08-09** (`master-key-ops.md`, *"Why not a sealed blob on disk"*: no TPM, `sops` absent
  from trixie), neither of which would touch the process-memory path named above. It is an
  ADR-level decision with a measurement and `security-auditor`'s signature.
  **`security-auditor` 2026-08-17: the exclusion in
  `release-checklist.md` §2.6 point 3.5 clause (1) stands on this class argument, not on the
  census above.**

  **What replaces them: nothing yet**, and the roadmap must not be dressed up. The exit at the
  pre-real-data boundary is therefore **re-grant**, not **close**; the only real candidates are
  the ones the ADR's own Alternatives already carry. `restrict,pty,from=` on the operator key is
  **hygiene, not mitigation 1** — it closes agent forwarding and `~/.ssh/rc`, which the server-wide
  drop-in does not, but an attacker holding the key and satisfying `from=` still reaches root in one
  `sudo -n`. ⚠ **And it is not applied** — §4.0 provisions the key with `from=` alone, and `restrict`
  appears nowhere as an applied control.

  Non-interactive operation requires *no prompt*, not *unlimited root* — conflating those two is
  what this trade-off actually is. **Written up as ADR 0123** (local), which
  carries this reasoning, a scope limit (accepted only while the box holds no real user data)
  and both unclosed mitigations. ⚠ **Klas GRANTED it 2026-08-16** — read the status in the ADR,
  not here. **That closes the acceptance, not the mitigations:** the two named above are still
  open, which is why #1201's M-7 escalation can still fire (its condition is *ungranted **or**
  unmitigated*). ⚠ **And the grant covers only the state WITHOUT real user data** — M-7 is
  evaluated **at** real user data, so the acceptance lapses exactly where the condition is read.
  `security-auditor` ruled 2026-08-17 that **M-7 does convert**; see `release-checklist.md` §2.6
  point 3.5 for what would actually discharge it. ⚠ **Do not enumerate it here** — she restated
  requirement (1) the same day, and the earlier enumeration named as necessary work the two
  mechanisms she now expressly excludes. ⚠ **Condition on the
  CAPABILITY, never on issue numbers** — this line said "#196/#198" until 2026-08-17 and #196 has
  been closed since 2026-08-08; both legs are homed at **#1201** per ADR 0050's dated note.
- **Root is rotated but deliberately not locked.** Beyond being the console rescue identity,
  there is a stronger reason: after the NOPASSWD decision, `jpadmin` at the console already
  grants unrestricted root. Locking root would remove a tested rescue identity while reducing
  blast radius by exactly nothing.
- **ICMP and ICMPv6 are permitted in both directions** — but via the enabled copied policy
  `netcup Ping allow`, not via rules of our own. NDP is not optional: it is how IPv6 works.
- **Host `output` policy is `accept`** rather than a second filtering layer. The load-bearing
  argument is not debugging surface: an attacker with root can rewrite `nftables` but cannot
  reach the edge rules, so egress control lives in the one layer that survives a host
  compromise. Rationale in §5.
- **Detective controls are thin.** `LogLevel VERBOSE` (§4.2) is the only one; there is no
  `auditd`, no file-integrity monitoring, no alerting. GDPR Art. 33's 72-hour clock runs from
  becoming *aware*, and on a host where nothing would make anyone aware, that deadline is not
  hard to meet — it is unmeasurable. ADR 0050's gate table is entirely preventive and needs a
  detection gate before real user data.

---

## 12. Notes for the phases that follow

- **`forward policy drop` (§5) stops all container traffic, including traffic to a published
  reverse proxy.** A container-published port is DNAT'd in `nat/PREROUTING` and then traverses
  **`forward`**, not `input` — netfilter runs every base chain on the hook, so a DROP in `inet
  filter` is final and Docker's own ACCEPT rules in `ip filter` do not rescue it. This means
  `tcp dport {80,443} accept` in `input` does **not** admit a containerised Caddy. The handover
  needs an active decision; "no firewall change needed" would be wrong.
- **`forward policy drop` plus the edge default-deny is what keeps M-6's "PG/Redis not public" true**
  even against an accidental `0.0.0.0` publish. Whatever resolves the point above **must
  preserve that**: targeted `iif`/`oif` accepts for the Docker bridge, never a blanket `policy
  accept`. Open `forward` wholesale and the container's bind address becomes the *only*
  remaining control on 80/443 — which are already open to `any` at both layers.
- Verify empirically after deploy: `curl` the host IP on every container port *from outside*,
  not by reading `expose:` entries. **The reason this gate reads the response and not the file:**
  the dev compose file bound five of six ports to `0.0.0.0`, including an unauthenticated Seq,
  **while its own comment claimed the opposite** — for months, and nobody caught it by reading.
  Both were repaired in #1198 (all six now `127.0.0.1`, comment rewritten), so the *instance* is
  gone; the *lesson* is why the proof is a `curl` from outside and never a config read.
- **The edge's IPv6 behaviour is still unmeasured, and it becomes load-bearing here.** §4.2
  removed the dependency for SSH by not listening on v6 at all, but the host chain accepts
  `tcp dport {80,443}` address-family-agnostically and the edge opens them to `any`. Exposure is
  zero today (no listener → RST), and while `forward policy drop` stands it covers v6 as well.
  **The moment that policy is replaced with targeted Docker accepts, the edge's v6 half becomes
  the control — and nobody has measured it.** Measure before that swap: from a phone on mobile
  data (Swedish mobile networks are native IPv6), `nc -6 -vz <box-v6-address> 22` — timeout means
  the edge does not pass v6, RST means the packet reached the host. `ListenAddress 0.0.0.0` does
  not spoil that probe; it makes it safe, because a successful packet no longer reaches a
  listener.
- **This is an 8 GB box, and ADR 0050 Beslut 2 rejected that sizing** — a rejection **ADR 0122
  supersedes**, on the measurement that its ground (`MaxResponseContentBufferSize = 500 MB`) no
  longer exists in `src/`. ADR 0122 also supersedes ADR 0050's `mem_limit` doctrine: its
  "generous/unset cap on Postgres" rested explicitly on 16 GB dissolving the zero-sum game, and
  at 8 GB that game is back, so every service is capped. The capacity verdict
  (2026-08-02) let it through as "marginal but workable" conditioned on four things the deploy
  phase must carry: `next build` in CI and never on the box, `DOTNET_gcServer=0` for Api and
  Worker, an explicitly tuned Postgres (not defaults, against 8 GB), and **zram instead of disk
  swap**. The last one is delivered in §8 — but it satisfies *two* requirements at once, B-1 and
  the capacity verdict. **Adding a disk swap file under memory pressure breaks B-1.** Add RAM
  instead.
- **Outbound mail stays impossible** and must remain so: transactional mail goes over the
  provider's HTTPS API — Scaleway Transactional Email in `fr-par` (#183) — never SMTP.
- The `_FILE` secret seam, the key-in-tmpfs work, and the mandatory second security audit of
  the actual production configuration are gates for the *first real user data*, not for this
  runbook. See ADR 0050's pre-beta-data gates.

---

## 13. References

- [`docs/decisions/0050-deployment-migration-aws-exit-hetzner.md`](../decisions/0050-deployment-migration-aws-exit-hetzner.md) — **start here.** Gates B-1 (master key never plaintext on disk), M-5a/M-5b, M-6 (hardening baseline) and M-7. Its `Amendment 2026-08-04` is **authoritative for the gates and written to be read alone**; its Hetzner/Cloudflare text is **superseded, not deleted**, and the banner at the top carries the precise boundary
- **ADR 0122** — the host, the sizing and the capacity *rationale*. **Local (gitignored)** per ADR 0072 docs-privacy; `.worktreeinclude` syncs it, but a worktree that skipped the docs-sync will not have it. **If it is absent, ADR 0050's amendment is sufficient and you are missing no gate.** ADR **0123** (local; **granted by Klas 2026-08-16** — read its status there, never here) carries the `NOPASSWD` risk acceptance
- [#196](https://github.com/klasolsson81/jobbliggaren/issues/196) — deploy stack; owns everything in §12
- [`CLAUDE.md`](../../CLAUDE.md) §11 — tooling and the dev-boot config contract
