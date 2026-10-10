namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>
/// Real kernel and runtime output, captured read-only on the production box (CLAUDE.md §9.2), so the parsers
/// are tested against what a producer emits and not against a shape imagined here (AGENTS.md §5, Tests).
/// Numbers and mount names only: no personal data, no secret. Container ids in the mountinfo are replaced
/// by zeros; nothing else is edited.
/// </summary>
internal static class HostFixtures
{
    /// <summary>
    /// Producer: Linux 6.12.101+deb13-amd64, 4 vCPU KVM guest (AMD EPYC), <c>cat /proc/stat</c> over SSH,
    /// 2026-10-10T13:52:20Z. The aggregate line is the 10-column form: user 20067891, nice 69831,
    /// system 9369748, idle 1889627981, iowait 820642, irq 0, softirq 1674696, steal 4422, guest 0, guest_nice 0.
    /// </summary>
    public const string ProcStat = """
cpu  20067891 69831 9369748 1889627981 820642 0 1674696 4422 0 0
cpu0 4947112 16341 2316412 472614453 198241 0 885379 1141 0 0
cpu1 5031354 17684 2354143 472335995 200146 0 477115 1192 0 0
cpu2 5062904 18471 2357588 472276089 209878 0 124050 910 0 0
cpu3 5026520 17334 2341603 472401442 212376 0 188150 1178 0 0
intr 5179868424 0 9 0 0 0 0 3 0 0 0 714633 32 15 0 4781549 0 0 0 0 0 0 0 0 0 0 148 0 2743001 1612995 3376952 1650290 3021749 1721275 4290248 1696457 0 16089802 17630895 17707992 17524069 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0
ctxt 19351420004
btime 1786820656
processes 61654546
procs_running 1
procs_blocked 0
softirq 1699611342 1 262165751 16698 180160036 2390750 0 45124 857645777 1830 397185375
""";

    /// <summary>The aggregate line above, grouped: busy = user+nice+system+irq+softirq+steal, idle = idle+iowait.</summary>
    public const long ProcStatBusy = 31_186_588;

    public const long ProcStatIdle = 1_890_448_623;

    /// <summary>Producer: the same kernel, <c>cat /proc/meminfo</c>, 2026-10-10T13:52:20Z.</summary>
    public const string ProcMeminfo = """
MemTotal:        8135992 kB
MemFree:         1256212 kB
MemAvailable:    6016652 kB
Buffers:          927160 kB
Cached:          4468400 kB
SwapCached:         1268 kB
Active:          3350212 kB
Inactive:        2775716 kB
Active(anon):    1559404 kB
Inactive(anon):     9256 kB
Active(file):    1790808 kB
Inactive(file):  2766460 kB
Unevictable:           0 kB
Mlocked:               0 kB
SwapTotal:       4067836 kB
SwapFree:        4026492 kB
Zswap:                 0 kB
Zswapped:              0 kB
Dirty:               848 kB
Writeback:             0 kB
AnonPages:        674548 kB
Mapped:          1297128 kB
Shmem:            838456 kB
KReclaimable:     508804 kB
Slab:             620404 kB
SReclaimable:     508804 kB
SUnreclaim:       111600 kB
KernelStack:        6928 kB
PageTables:        23520 kB
SecPageTables:         0 kB
NFS_Unstable:          0 kB
Bounce:                0 kB
WritebackTmp:          0 kB
CommitLimit:     8135832 kB
Committed_AS:    3661180 kB
VmallocTotal:   13743895347199 kB
VmallocUsed:       50200 kB
VmallocChunk:          0 kB
Percpu:             4720 kB
HardwareCorrupted:     0 kB
AnonHugePages:    360448 kB
ShmemHugePages:        0 kB
ShmemPmdMapped:        0 kB
FileHugePages:         0 kB
FilePmdMapped:         0 kB
Unaccepted:            0 kB
HugePages_Total:       0
HugePages_Free:        0
HugePages_Rsvd:        0
HugePages_Surp:        0
Hugepagesize:       2048 kB
Hugetlb:               0 kB
DirectMap4k:      180076 kB
DirectMap2M:     7159808 kB
DirectMap1G:     3145728 kB
""";

    /// <summary>
    /// Producer: Docker 29.7.1 with runc on Debian 13, cgroup v2, no lxcfs; <c>/proc/self/mountinfo</c> read
    /// inside the production API container, 2026-10-10. No mount stands behind <c>/proc/stat</c> or
    /// <c>/proc/meminfo</c>; <c>/proc</c> is <c>proc</c>. The container's <c>memory.max</c> on that day was
    /// 1073741824 (1 GiB).
    /// </summary>
    public const string ContainerMountInfo = """
575 520 0:55 / / rw,relatime - overlay overlay rw,lowerdir=/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20249/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20131/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20130/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20129/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20128/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10827/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10824/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10823/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10818/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10817/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10812/fs:/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/10707/fs,upperdir=/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20250/fs,workdir=/var/lib/containerd/io.containerd.snapshotter.v1.overlayfs/snapshots/20250/work
415 575 0:71 / /proc rw,nosuid,nodev,noexec,relatime - proc proc rw
576 575 0:125 / /dev rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755,inode64
577 576 0:126 / /dev/pts rw,nosuid,noexec,relatime - devpts devpts rw,gid=5,mode=620,ptmxmode=666
416 575 0:72 / /sys ro,nosuid,nodev,noexec,relatime - sysfs sysfs ro
417 416 0:28 / /sys/fs/cgroup ro,nosuid,nodev,noexec,relatime - cgroup2 cgroup rw,nsdelegate,memory_recursiveprot
418 576 0:118 / /dev/mqueue rw,nosuid,nodev,noexec,relatime - mqueue mqueue rw
578 576 0:74 / /dev/shm rw,nosuid,nodev,noexec,relatime - tmpfs shm rw,size=65536k,inode64
579 575 254:4 /var/lib/docker/volumes/jobbliggaren-prod_dataprotection_keys/_data /keys rw,relatime master:1 - ext4 /dev/vda4 rw,errors=remount-ro
580 575 0:25 /jobbliggaren/secrets /run/app-secrets ro,nosuid,nodev,noexec,relatime - tmpfs tmpfs rw,size=813600k,mode=755,inode64
581 575 0:25 /jobbliggaren/redis/api-persistent /run/redis-persistent ro,nosuid,nodev,noexec,relatime - tmpfs tmpfs rw,size=813600k,mode=755,inode64
461 575 0:25 /jobbliggaren/redis/api-volatile /run/redis-volatile ro,nosuid,nodev,noexec,relatime - tmpfs tmpfs rw,size=813600k,mode=755,inode64
462 575 254:4 /var/lib/docker/containers/0000000000000000000000000000000000000000000000000000000000000000/resolv.conf /etc/resolv.conf rw,relatime - ext4 /dev/vda4 rw,errors=remount-ro
464 575 254:4 /var/lib/docker/containers/0000000000000000000000000000000000000000000000000000000000000000/hostname /etc/hostname rw,relatime - ext4 /dev/vda4 rw,errors=remount-ro
465 575 254:4 /var/lib/docker/containers/0000000000000000000000000000000000000000000000000000000000000000/hosts /etc/hosts rw,relatime - ext4 /dev/vda4 rw,errors=remount-ro
406 415 0:71 /bus /proc/bus ro,nosuid,nodev,noexec,relatime - proc proc rw
407 415 0:71 /fs /proc/fs ro,nosuid,nodev,noexec,relatime - proc proc rw
408 415 0:71 /irq /proc/irq ro,nosuid,nodev,noexec,relatime - proc proc rw
414 415 0:71 /sys /proc/sys ro,nosuid,nodev,noexec,relatime - proc proc rw
486 415 0:71 /sysrq-trigger /proc/sysrq-trigger ro,nosuid,nodev,noexec,relatime - proc proc rw
487 415 0:57 / /proc/acpi ro,relatime - tmpfs tmpfs ro,size=4k,nr_inodes=1,inode64
488 415 0:125 /null /proc/interrupts rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755,inode64
489 415 0:125 /null /proc/kcore rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755,inode64
490 415 0:125 /null /proc/keys rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755,inode64
491 415 0:125 /null /proc/timer_list rw,nosuid - tmpfs tmpfs rw,size=65536k,mode=755,inode64
492 416 0:57 / /sys/firmware ro,relatime - tmpfs tmpfs ro,size=4k,nr_inodes=1,inode64
""";

    public const string ContainerMemoryMax = "1073741824\n";

    /// <summary>
    /// The same file as an lxcfs-style runtime would present it: the real lines plus the fuse mounts lxcfs
    /// puts over <c>/proc/meminfo</c> and <c>/proc/stat</c> (a constructed variant: this producer is not
    /// installed on the box, which is why the check exists; its shape is mountinfo's documented one).
    /// </summary>
    public static readonly string LxcfsMountInfo = ContainerMountInfo
        + "\n612 415 0:140 /proc/meminfo /proc/meminfo rw,nosuid,nodev,relatime - fuse.lxcfs lxcfs rw,user_id=0,group_id=0,allow_other"
        + "\n613 415 0:140 /proc/stat /proc/stat rw,nosuid,nodev,relatime - fuse.lxcfs lxcfs rw,user_id=0,group_id=0,allow_other";
}
