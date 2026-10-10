# Runbook — Admin host observations: CPU, memory and disk (#1982, Server sub-track)

The Server card of the `/admin` overview shows three readings of the **host** the stack runs on. This
runbook is the one home for what each reading means, where it comes from, how it can be wrong and how to
check it against the real box. The decision and its alternatives are ADR 0158. The Backup card's collectors
and their bridge are a different mechanism with their own runbook (`host-observations.md`, ADR 0157): this
one has no host script, no mount, no unit and no compose change.

## 1. Mechanism in one paragraph

A `BackgroundService` inside the API process (`HostObservationService`) calls the sampler once at start and
then every `HostObservation:SampleIntervalSeconds` (default 30 s). Each sample reads, through the
Application port `IHostObservationProbe`, `/proc/stat` (the aggregate CPU line), `/proc/meminfo`
(`MemTotal`, `MemAvailable`), `/proc/self/mountinfo` and `memory.max` (the host-view check, §3) and the
filesystem at `/` through `DriveInfo` (a plain `statfs`). The newest result is held in memory as an immutable
snapshot. `GET /api/v1/admin/overview/host` never touches the host: it reads that snapshot, stamps the read
time and decides what is stale. The browser asks for nothing; it never supplies a path, a command or an
interval, and the paths are constants in the adapter, not configuration.

## 2. What each reading means

| Reading | Definition | Never |
|---|---|---|
| **CPU %** | Share of the window the host's CPUs were not idle, over all vCPUs (100 % = every vCPU busy). `100 · busy / (busy + idle)` over the **difference** between two consecutive samples, where `busy = user + nice + system + irq + softirq + steal` and `idle = idle + iowait` (proc(5)). `iowait` counts as idle; `steal` (time the hypervisor ran another guest) counts as busy; the two `guest` columns are left out because the kernel already counts guest time inside `user` and `nice`. The window length is returned (`windowSeconds`) and shown. | Load average. A single-instant reading. A per-core number. |
| **Memory** | `used = MemTotal − MemAvailable`, `total = MemTotal`, `percent = used / total`. `MemAvailable` is the kernel's estimate of memory available to start applications without swapping; it counts reclaimable page cache and reclaimable slab as available. Swap is not included. | `MemFree`. On the box (2026-10-10) `MemFree` was ≈ 1.2 GB while `MemAvailable` was ≈ 5.7–6.0 GB of 7.8 GiB: `MemFree` would have shown the host nearly full. |
| **Disk** | The filesystem that backs the API container's root. From `statfs`: `used = Size − TotalFree`, `free = Available` (blocks available to non-root processes), `total = used + free`, `percent = used / total`. This is `df`'s arithmetic (`Use% = used / (used + avail)`), the same definition as the heartbeat's disk floor. | A sum over filesystems. A path, device name or mount in the response. |

**The disk total is not `df`'s Size.** The filesystem reserves a share of blocks for root (about 4 % on the
box). Those are neither used nor available to the application, so `df`'s Size is larger than the card's total
by exactly that reserve. On the box on 2026-10-10: Size 269 205 880 832 B, Used 15 866 851 328 B, Avail
242 287 181 824 B, so the card shows 242 287 181 824 free of 258 154 033 152 (≈ 225.6 of 240.4 GiB), 6.1 %;
`df` rounds its percentage up and prints 7 %. The card's meter and its two numbers always use the same
denominator.

**Which filesystem, and why one.** The container's root is an overlay whose `statfs` reports the filesystem
beneath it. On the box that is the host root (`/dev/vda4`, ext4), which also holds `/var/lib/docker` (named
volumes: Postgres, Seq, Caddy), `/var/lib/containerd` (image layers) and `/var/lib/jobbliggaren`. The only
other real filesystems are `/boot` and `/boot/efi`. Nothing is added up, so nothing can be counted twice.

## 3. The host/container boundary — the assumption this design stands on

No namespace virtualises `/proc/stat` or `/proc/meminfo` (pid, mount, cgroup and user alike). Under Docker
with runc a process in the container reads the **host kernel's** counters, and `statfs` on the overlay root
reports the filesystem under it. Measured read-only on the box on 2026-10-10 (numbers only): `MemTotal`
identical inside the API container and on the host; the CPU counters inside the container within seconds of
the host's; `statfs('/')` inside the container equal to the host's `/`; runtime runc, cgroup v2, no `lxcfs`;
the container's `memory.max` 1 073 741 824 (1 GiB), far below the host's 8 GiB.

It is a **deployment fact**, so the adapter looks for the two ways it stops holding. If either finds
something, CPU and memory are `NotObservable` and disk is unaffected (`statfs` is not virtualised the same
way):

1. **Structural.** `/proc/self/mountinfo` shows a mount at `/proc/stat` or `/proc/meminfo` (how lxcfs
   presents a container's limit as the machine), or `/proc` is not of type `proc`.
2. **Heuristic.** `memory.max` is a finite number and `MemTotal` is not larger than it: `/proc` presents the
   limit as RAM.

A missing file, an unreadable file or `max` cannot disprove host scope, so sampling proceeds.

**Residuals, written down rather than discovered** (ADR 0158 holds them with the lapse trigger):

- r1. A sandboxing runtime (gVisor, Kata, Sysbox) is not detected.
- r2. The heuristic fails safe in the other direction: a memory cap at or above the host's RAM withholds the
  CPU and memory meters.
- r3. Disk means the filesystem under the container's writable layer. Today that equals the host root.
- r4. About 30 s of `Collecting` for the CPU after every API recreate, which is every release.
- r5. One API replica.

Process memory (`IProcessMemoryProbe`, Worker only) is a different instrument and is not used here; the API's
own 1 GiB cgroup limit is not what the memory card shows.

## 4. Time, states and what the browser gets

Every reading carries its own state, so one failing reading never blanks the other two.

| State | Meaning | `value` and `sampledAt` |
|---|---|---|
| `Available` | Sampled; at most `StaleAfterSeconds` old when read | both |
| `Stale` | The newest value is older than `StaleAfterSeconds` (default 120 s): the sampler stopped | both, with the **original** `sampledAt` |
| `Collecting` | No baseline yet: the CPU's first window after the API started | neither |
| `NotObservable` | The reading is built but this deployment cannot supply it: no procfs (Windows development) or an unverifiable host view. Never worded "Kommer snart", which says a capability is not built (ADR 0150 D2) | neither |
| `Failed` | This tick could not produce a trustworthy value: a read, parse or plausibility failure, a counter reset, an invalid window, or no tick within `StaleAfterSeconds` | neither |

- The response is `{ readAt, staleAfterSeconds, cpu, memory, disk }` with each reading
  `{ state, sampledAt, value }`; `value` is `{ percent, windowSeconds }`, `{ percent, usedBytes, totalBytes }`
  and `{ percent, freeBytes, totalBytes }`. **There is no reason, path, device name or free text.** Why a
  reading is missing stays on the server, in tests and in edge-triggered log lines (event ids 6220–6224).
- `sampledAt` is the injected wall clock read **once per tick, after the reads**. `readAt` is the wall clock
  at the request, and the header `X-Admin-Sampled-At` carries it, like the other overview sources. They are
  different instants: a reading can be old although it was just read.
- A failed tick writes `Failed`; the previous value is **not** carried forward as new. A `sampledAt` more
  than 5 s ahead of `readAt` (a wall clock that stepped backwards) is `Failed`, never fresh. A number that is
  not known is `null`, never 0. A real 0.0 % CPU is a value.
- The page ages a reading it keeps (after a refresh that failed) by the same `staleAfterSeconds`, and marks
  it. Every response, errors and rate-limit refusals included, is `private, no-store`.

**CPU specifics.** The window is the **monotonic** time between the two samples (`Stopwatch`, taken when
`/proc/stat` is read), so a step of the wall clock cannot bend it, and the wall clock is used for `sampledAt`
and `readAt` only. Accepted window: from half to three times the interval (15–90 s by default).

- Under half an interval: the sample is skipped; the baseline and the previous reading, with its own
  `sampledAt`, are kept. A short window is noise, not a failure.
- Over three intervals: `Failed`, and the sample becomes the new baseline (the sampler did not run; an
  average over that gap would misstate "now").
- A decrease of `busy`, of `idle` or a window with no ticks: `Failed`, and the sample becomes the new
  baseline. Monotonicity is checked on these grouped sums, not per column, because the kernel does not
  promise that `iowait` alone never decreases.
- `Collecting` means exactly "no baseline yet". A permanent fault shows as `Failed`, never as endless
  `Collecting`.
- A failed or unsupported CPU read drops the baseline, so the next successful read is `Collecting`.

## 5. Cadence and cost

| Setting | Default | Notes |
|---|---|---|
| `HostObservation:SampleIntervalSeconds` | 30 | 5–300. The browser polls the overview every 60 s while the tab is visible (ADR 0150 amendment 2026-10-08). |
| `HostObservation:StaleAfterSeconds` | 120 | Must be more than three times the interval, validated at start. |

Cost per sample: four small file reads (≈ 12 KB in all, each capped at 64 KiB) and one `statfs`. The read
endpoint does no I/O: its cost is the Admin authorization lookup every admin endpoint pays. Measured numbers
are recorded in §9 when taken; do not copy a figure forward without re-running the command there. The
endpoint shares the per-user `admin-read` bucket (30 per 10 s); the overview adds one request per 60 s poll.

## 6. Local development

The API runs natively on Windows (`dotnet run`), where `/proc` does not exist: all three readings are
`NotObservable` (no warning is logged) and the card says so. Nothing is invented. Running the API in a Linux
container shows that container's host (Docker Desktop's VM), which is correct for that environment and not
the developer's laptop.

## 7. Security boundaries

Admin policy and `IAdminRequest` apply: anonymous 401, ordinary user 403, a revoked admin is refused on the
next call, direct Mediator use is refused. No SSH key, Docker socket, host mount or extra Linux capability is
involved: the sampler reads files the container can already read. Host metrics are not personal data and are
not retained beyond the newest snapshot in memory.

## 8. Checking the real values against the box

Under the read rule of CLAUDE.md §9.2: numbers only, no signed-in request to the application, no log lines.

1. Take a host reading and note the UTC time:
   `ssh jp-vps 'date -u +%FT%TZ; grep -E "^(MemTotal|MemAvailable):" /proc/meminfo; head -1 /proc/stat; df -B1 --output=size,used,avail,pcent /'`.
2. Take a second `head -1 /proc/stat` about 30 s later and compute the CPU percent by §2.
3. Prove the boundary of §3 from inside the API container:
   `ssh jp-vps 'sudo docker exec jobbliggaren-api sh -c "grep -E ^MemTotal: /proc/meminfo; head -1 /proc/stat; df -B1 /; cat /sys/fs/cgroup/memory.max"'`.
   `MemTotal` must equal the host's and `memory.max` must be below it.
4. Klas opens `/admin` signed in and reads the Server card: memory used and total within a few percent of
   step 1 (`MemTotal − MemAvailable`), disk free equal to `df`'s Avail and total equal to `df`'s Used + Avail,
   CPU of the same order as step 2 (the card averages a 30 s window; the box idles in low single digits).
5. After a deploy the API restarts: memory and disk are `Available` at once, the CPU reads `Collecting`
   for about one interval, then `Available`.

## 9. Measured cost

Both figures below are dated one-off measurements, not budgets; re-run the commands before quoting them.

**The sampler (one tick = the four reads), 2026-10-10, 2 000 iterations after warm-up, Release build, on the
developer workstation's Docker Desktop VM (Linux, `mcr.microsoft.com/dotnet/sdk:10.0-noble`) — not on the
production box.** It runs the real `ProcFsHostObservationProbe` against that VM's own `/proc`:

| Call | Wall per call | Allocated per call |
|---|---|---|
| `ReadCpu` | 37 µs | 3.8 KB |
| `ReadMemory` | 44 µs | 3.2 KB |
| `ReadDisk` | 6 µs | 24 B |
| `ReadHostView` | 186 µs | 25 KB |
| **whole tick** | **243 µs** | **32 KB** |

One tick every 30 s is 0.0008 % of one core and about 93 MB of short-lived garbage a day (2 880 ticks). The
files read there were 1.8 KB (`/proc/stat`), 1.5 KB (`/proc/meminfo`) and 3.9 KB (`mountinfo`).
`ReadHostView` is the dearest read because it parses the mount table and re-reads `/proc/meminfo`; it is not
worth optimising at this cadence.

**The read endpoint, 2026-10-10, 300 sequential `GET`s as an admin over the in-process test server with a
Testcontainers Postgres, after 30 warm-up calls:** `/overview/host` p50 9.7 ms, p95 11.0 ms; for comparison
`/overview/accounts` (a SQL read) p50 11.6 ms, p95 13.8 ms. The handler is an in-memory read, so what is left
of those 9.7 ms is the session and Admin authorization lookup every admin endpoint pays. ADR 0045's budget for
a read handler is p95 300 ms.

To regenerate the sampler figures, from a checkout, in a temporary directory outside the repo:

```bash
mkdir /tmp/hostcost && cd /tmp/hostcost   # then create the two files below
docker run --rm -v "<checkout>:/repo:ro" -v "$PWD:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0-noble dotnet run -c Release --nologo
```

`HostCost.csproj` — an exe that compiles the probe and its parsers straight from the checkout, so it needs no
package restore:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Application/Admin/HostObservations/HostMetricState.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Application/Admin/HostObservations/IHostObservationProbe.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Infrastructure/Diagnostics/ProcFsHostObservationProbe.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Infrastructure/Diagnostics/ProcStatParser.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Infrastructure/Diagnostics/ProcMeminfoParser.cs" />
    <Compile Include="/repo/src/Jobbliggaren.Infrastructure/Diagnostics/ProcMountInfo.cs" />
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using System.Diagnostics;
using Jobbliggaren.Infrastructure.Diagnostics;

var probe = new ProcFsHostObservationProbe();
const int N = 2000;
for (var i = 0; i < 300; i++) { probe.ReadCpu(); probe.ReadMemory(); probe.ReadDisk(); probe.ReadHostView(); }

void Measure(string name, Action action)
{
    GC.Collect();
    var alloc = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < N; i++) action();
    Console.WriteLine($"{name,-13} {sw.Elapsed.TotalMilliseconds * 1000 / N,8:F1} us  {(GC.GetAllocatedBytesForCurrentThread() - alloc) / (double)N,8:F0} B");
}

Measure("ReadCpu", () => probe.ReadCpu());
Measure("ReadMemory", () => probe.ReadMemory());
Measure("ReadDisk", () => probe.ReadDisk());
Measure("ReadHostView", () => probe.ReadHostView());
Measure("whole tick", () => { probe.ReadCpu(); probe.ReadMemory(); probe.ReadDisk(); probe.ReadHostView(); });
```
