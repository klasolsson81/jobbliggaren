# Redis policy verification fixture

This fixture invokes the actual `jobbliggaren-redis-policy.py` CLI with only its
five absolute filesystem constants redirected into `/opt/test-area`. Redis 8.6
parses the shipped candidate and predecessor ACL templates, returns canonical
`ACL LIST`, and executes positive and negative `ACL DRYRUN` probes. Expected
negative Redis errors retain their real exit code; the transport does not turn
them into successful replies.

Build and run from the owned worktree, substituting its absolute path below:

```powershell
docker build --file tests/Deployment/RedisPolicyFixture.Dockerfile --tag jbl-1976-policy-tests tests/Deployment
docker run --rm --network none --read-only --tmpfs /run:rw,noexec,nosuid,size=32m --tmpfs /opt/test-area:rw,exec,nosuid,size=64m --tmpfs /tmp:rw,noexec,nosuid,size=16m --mount type=bind,src=C:/tmp/jbl-admin-suspend-1976,dst=/repo,readonly --pids-limit 128 --memory 192m jbl-1976-policy-tests
```

The image supplies Python's standard library and the repository-pinned Redis
binary. Its build installs Python with Alpine's package manager. The run has no
network, published port, writable repository, Docker socket, host credential or
shared service. `/run` and `/opt/test-area` must be dedicated tmpfs mounts; the
fixture refuses other environments. The test area needs `exec` for the isolated
Docker transport executable; the helper is normalized to LF when copied.
Redis processes use isolated Unix sockets.
All credentials are deterministic synthetic fixture values.

The 27 test methods cover complete candidate/predecessor/transition policies;
generation and selector boundaries, including mixed-generation address Lua;
inherited-lock identity and contention; CLI refusal; stopped/ambiguous containers;
installed/mounted/active disagreement; missing, duplicate or writable mounts;
credential drift and malformed credentials; active configuration; dependency
failures and cleanup; and permission changes after the ACL snapshot. Captured
stdout/stderr and Docker argv are checked for fixture plaintext credentials and
their hashes. Faulting dependencies intentionally emit both to prove redaction.
The configuration-fault actor starts its isolated Redis with the same ACL bytes
under a different real `--aclfile` path. The expected mount and effective policy
remain intact, while the real `CONFIG GET aclfile` reveals the boot configuration
error. No CONFIG SET permission or synthetic configuration reply is introduced.

The Docker transport is a narrow file-backed double. Its container metadata,
mount paths and filesystem namespace projection are modeled; it does **not**
prove the real daemon's bind-mount behavior, image provenance or production
container identification. In particular, the production oracle's host-tempfile
bind cannot be tested faithfully from a disposable container without introducing
the host daemon/socket and cross-namespace path ownership. A separate real-Docker
rehearsal remains necessary for that boundary. The fixture does not substitute
for reconciler interruption/resume tests, migration tests or live approval.

The test writer does not execute builds or tests. The driving session must record
the actual `total:`/`failed:` output; file creation is not a passing result.
