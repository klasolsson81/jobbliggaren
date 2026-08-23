# #1208 — dev-Seq content measurement, 2026-08-23

Read-only. Taken before any remedy was chosen, per Klas's scope point 1
("noll idag är inte samma sak som noll i morgon"). Every number below is a
measurement of the LOCAL developer Seq (`jobbliggaren-seq`, compose service
`seq`, `127.0.0.1:5341` / `5342`), not of the box.

Seq version 2025.2.16202, container up 28 h at the time of measurement.

## 1. Scope correction — read this before the numbers

My first sweep grepped `/data/Stream/*.tick` only. That is **16.2 MB of a
196.6 MB store**: the bulk lives in 14 `.span` files (179.2 MB), which the tick
glob never reached. Every number in §3 is from the re-scoped sweep over every
file under `/data`. The tick-only figures are not reported here at all, because
a sweep that saw 8 % of the sink is not a smaller measurement — it is a
different one.

```
docker exec jobbliggaren-seq sh -c 'ls -la /data/Stream | awk "NR>3 {n=\$9; sub(/.*\./,\"\",n); s[n]+=\$5; c[n]++} END {for (k in s) printf \"%-10s %4d files %12d bytes\n\", k, c[k], s[k]}"'
```

## 2. Store layout and event window

| Thing | Value |
|---|---|
| `/data` total | 211 MB |
| `/data/Stream` total | 196 631 067 B |
| — `.span` (stable, coalesced) | 14 files, 179 209 535 B |
| — `.tick` (recent extents) | 98 files, 16 221 514 B |
| — `.index` | 14 files, 1 171 958 B |
| Tick-extent window | 2026-08-23T10:51 → 2026-08-23T16:54 (UTC) |

The extent name encodes .NET ticks. Validated against an independent anchor
rather than assumed: `stream.08df012982399b00…tick` decodes to
`2026-08-23T15:16:30`, and the file's mtime is `2026-08-23T15:16:34` — a 4 s
write latency, not a decode error.

```
docker exec jobbliggaren-seq sh -c 'ls /data/Stream/*.tick' | sed -E 's#.*/stream\.([0-9a-f]+)\..*#\1#'
# decode: datetime(1,1,1) + timedelta(microseconds=int(hex,16)/10)
```

## 3. What the sink holds

**548 address-shaped occurrences, 56 distinct**, across the whole of `/data`:

| Domain | Distinct | Reserved by |
|---|---|---|
| `e2e.jobbliggaren.test` | 40 | RFC 6761 (`.test`) |
| `example.se` / `exempel.se` | 7 | **nothing — registrable under `.se`** |
| `example.com` | 1 | RFC 2606 |
| `jobbliggaren.se` | 1 | the project's own domain |
| binary-boundary artefacts | 7 | not addresses (`or.readmessagelon`, `microsoft.entityf`, …) |

- Explicit probe for consumer-mail domains
  (`gmail|googlemail|hotmail|outlook|live|yahoo|icloud|protonmail|telia|comhem|bahnhof|tele2`
  and friends): **0 hits.**
- Rendered `[ConsoleEmailSender] To=` events: **106** — `.tick` 22, `.span` 84, `.index` 0.
  *(A first version of this line reported 22. That was the tick-only figure, i.e. the very scope
  error §1 above retracts, committed inside the section §1 certifies as re-scoped. Caught by
  `security-auditor` on PR #1474, not by me.)*

  ```
  docker exec jobbliggaren-seq sh -c "grep -rhao '\[ConsoleEmailSender\] To=' /data | wc -l"
  ```

- **All 7 of the `example.se`/`exempel.se` addresses reached this sender**, i.e. they are recipients
  and not incidental strings: 19 of the 106 occurrences (18 + 1). Three of them are
  `render-1303-{1280,1920,3440}-…@example.se` — the repo's own mandated rendered-verification
  viewports, so this is a delivered developer flow rather than hypothetical traffic. #1475 owns the
  migration to reserved domains.

  ```
  docker exec jobbliggaren-seq sh -c "grep -rhaoE '\[ConsoleEmailSender\] To=[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' /data | sed 's/.*To=//'" | tr 'A-Z' 'a-z' | sort -u
  ```
- Personnummer-shaped strings (`(19|20)?NNNNNN[-+]NNNN`): **0**.

```
docker exec jobbliggaren-seq sh -c 'grep -rhaoE "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}" /data 2>/dev/null' | tr 'A-Z' 'a-z' | sort -u
```

**Reading:** CLAUDE.md §11's condition 2 holds today. It holds by luck, not by
construction — nothing in the write path prevents the next dev registration from
putting a real address and a whole activation body into this store. The real
`@gmail.com` the issue records from 2026-08-04 is gone, which is consistent with
the issue's own note that the volume was discarded when auth was enabled.

## 4. Retention: none, measured two ways

Seq's own retention processor, in its own voice, on every pass since 2026-08-04:

```
{"@mt":"Applying {Count} retention policies","Count":0,"SourceContext":"Seq.Server.Features.Retention.RetentionProcessor"}
```

**2384 passes, `Count` 0 in all 2384.** No `delet*` / `remov*` / `expir*`
message shape appears in the internal log at all.

Corroborated independently by the metastore, using the same instrument shape the
#1170 session used on the box — a candidate prefix read against non-zero
controls:

| Prefix | Occurrences |
|---|---|
| `retentionpolicy` | **0** |
| `signal` | 36 |
| `user` | 8 |
| `index` | 8 |
| `dashboard` | 5 |
| `apikey` | 4 |

```
docker exec jobbliggaren-seq sh -c 'grep -rhao "\"@mt\":\"[^\"]*retention policies\",\"Count\":[0-9]*" /data/Logs | sort | uniq -c'
docker exec jobbliggaren-seq sh -c 'grep -raoi "retentionpolicy" /data/Documents | wc -l'
```

`Seq.json` carries no retention key. Retention in Seq is metastore state set
through the UI or the API; there is no declarative surface in `docker-compose.yml`
to put it in — the same conclusion #1170 reached about the box's Seq yesterday.

## 5. Condition 1 is still measurable

- `GET /api/events` unauthenticated → **401**. `GET /api` → 200 (product name and
  link list, no data).
- `POST /api/users/login` with the `.env` admin password → **401**,
  `{"Error":"A password change is required."}`. `docs/runbooks/local-dev-setup.md`
  §6.4 records that a scripted login can 401 on Seq's CSRF handshake as well, so
  this response is not read here as evidence about the password.
- Published ports are `127.0.0.1:5341` and `127.0.0.1:5342`; the form of that
  binding is guarded by `.github/scripts/compose-loopback-guard.sh` in CI job
  `scripts (bash fixtures)`.

## 6. Where `ConsoleEmailSender` can run — repo-side, not box-side

- `AddEmailSender` registers `ConsoleEmailSender` only when
  `environment.IsDevelopment() || environment.IsEnvironment("Test")`; otherwise
  the Console arm resolves to `NullEmailSender`.
- `deploy/docker-compose.yml:340` sets `ASPNETCORE_ENVIRONMENT: Production` as a
  **literal** — there is no `${…}` interpolation of that key anywhere under
  `deploy/` — and the Worker gets `DOTNET_ENVIRONMENT: Production` at :433.
- `deploy/.env.example` records that since 2026-08-16 the box's own `.env`
  carries `EMAIL_PROVIDER=Scaleway`.

⚠ **These are repo-side facts. The box's live environment was NOT measured this
session** — the `ssh jp-vps` read was refused by the tool-permission classifier.
Do not read §6 as a measurement of what runs on the box.

Consequence, stated as a consequence and not as a measurement: the issue's
escalation trigger — *"escalates to Major at the first real test user"* — has no
path through the deployed box on these repo-side facts. What remains is a
developer typing a real address into a local stack.
