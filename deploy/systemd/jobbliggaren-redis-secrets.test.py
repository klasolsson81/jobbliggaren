"""Run only inside a disposable Linux container with tmpfs /run and /repo read-only."""
import fcntl
import hashlib
import os
from pathlib import Path
import pty
import select
import shutil
import subprocess
import time

repo = Path('/repo')
assert repo.is_dir() and os.geteuid() == 0
base = Path('/opt/jobbliggaren')
(base / 'deploy/systemd').mkdir(parents=True)
shutil.copytree(repo / 'deploy/redis', base / 'deploy/redis')
for name in ('jobbliggaren-redis-secrets.sh',):
    shutil.copyfile(repo / 'deploy/systemd' / name, base / 'deploy/systemd' / name)
(base / 'deploy/docker-compose.yml').write_text('services: {}\n')
ids = base / 'deploy/systemd/jobbliggaren-runtime-ids.sh'
ids.write_text('#!/bin/sh\nprintf "1654\\n1654\\n"\n')
ids.chmod(0o755)
docker = Path('/usr/bin/docker')
docker.write_text('''#!/bin/sh
printf '%s\\n' "$1" >> /tmp/docker-calls
case "$1" in
compose) printf '%s\\n' '{"services":{"api":{"image":"fixture-api"},"worker":{"image":"fixture-worker"},"redis":{"image":"fixture-redis"},"redis-volatile":{"image":"fixture-volatile"}}}';;
image) printf 'sha256:%064d\\n' 1;;
run) printf '999\\n999\\n';;
*) exit 90;;
esac
''')
docker.chmod(0o755)
root = Path('/run/jobbliggaren/redis')
root.parent.mkdir()
script = base / 'deploy/systemd/jobbliggaren-redis-secrets.sh'
credentials = [hashlib.sha256(f'isolated-fixture-{n}'.encode()).hexdigest() for n in range(7)]
count = 0

def checked(name, ok):
    global count
    assert ok, name
    count += 1
    print('PASS ' + name, flush=True)

def run(*args):
    return subprocess.run(['bash', str(script), *args], capture_output=True).returncode

def inject(values=credentials, interrupt_after=None):
    master, slave = pty.openpty()
    process = subprocess.Popen(['bash', str(script), '--inject'], stdin=slave, stdout=slave, stderr=slave)
    os.close(slave)
    buffer = b''
    sent = 0
    deadline = time.monotonic() + 15
    while process.poll() is None and time.monotonic() < deadline:
        if select.select([master], [], [], .1)[0]:
            try:
                chunk = os.read(master, 8192)
            except OSError:
                break
            buffer += chunk
            if buffer.endswith(b': ') and sent < len(values):
                os.write(master, (values[sent] + '\n').encode())
                sent += 1
                if sent == interrupt_after:
                    process.terminate()
    if process.poll() is None:
        process.wait(timeout=2)
    os.close(master)
    assert not any(value.encode() in buffer for value in values), 'credential echoed'
    return process.returncode

checked('absent set refuses', run('--check') != 0)
checked('noninteractive injection refuses', run('--inject') != 0)
checked('duplicate credentials refuse without publication', inject([credentials[0]] * 7) != 0 and not root.exists())
checked('interrupted terminal input does not publish', inject(interrupt_after=3) != 0 and not root.exists())
with open('/run/jobbliggaren-reconcile.lock', 'w') as lock:
    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    checked('reconcile lock excludes injection', inject() != 0 and not root.exists())
template = base / 'deploy/redis/volatile.acl.template'
original_template = template.read_bytes()
template.write_bytes(original_template + b'\n{{UNKNOWN}}\n')
checked('unresolved policy refuses atomic publication', inject() != 0 and not root.exists())
checked('failed staging is removed', not list(root.parent.glob('redis.staging.*')))
template.write_bytes(original_template)
checked('complete set publishes', inject() == 0)
before = {p.relative_to(root): p.read_bytes() for p in root.rglob('*') if p.is_file()}
Path('/tmp/docker-calls').unlink()
checked('static check succeeds without Docker', run('--check') == 0 and not Path('/tmp/docker-calls').exists())
checked('repeat injection preserves credentials', inject() == 0 and before == {p.relative_to(root): p.read_bytes() for p in root.rglob('*') if p.is_file()})
checked('unverified incoming image refuses', run('--check-images', 'api:latest', 'worker:latest') != 0)
checked('verified incoming reader matches', run('--check-images', 'api@sha256:' + 'a'*64, 'worker@sha256:' + 'b'*64) == 0)
ids.write_text('#!/bin/sh\nprintf "1655\\n1654\\n"\n')
checked('incoming reader drift refuses', run('--check-images', 'api@sha256:' + 'a'*64, 'worker@sha256:' + 'b'*64) != 0)
ids.write_text('#!/bin/sh\nprintf "1654\\n1654\\n"\n')

path = root / 'api-persistent/connection'
original = path.read_bytes()
for name, data in [('empty', b''), ('wrong identity', original.replace(b'api-persistent', b'worker-persistent')), ('injected option', original.replace(b'password=', b'user=worker-persistent,password='))]:
    path.write_bytes(data)
    checked(name + ' refuses', run('--check') != 0)
    path.write_bytes(original)
for name, mutation, restore in [
    ('wrong mode', lambda: path.chmod(0o444), lambda: path.chmod(0o400)),
    ('wrong owner', lambda: os.chown(path, 999, 999), lambda: os.chown(path, 1654, 1654)),
]:
    mutation()
    checked(name + ' refuses', run('--check') != 0)
    restore()
path.unlink()
path.symlink_to(root / 'api-volatile/connection')
checked('symlink refuses', run('--check') != 0)
path.unlink()
path.write_bytes(original)
path.chmod(0o400)
os.chown(path, 1654, 1654)
acl = root / 'volatile/users.acl'
content = acl.read_bytes()
acl.write_bytes(content + b'\nuser intruder on nopass +@all ~*\n')
checked('changed ACL refuses', run('--check') != 0)
acl.write_bytes(content)
checked('restored set passes', run('--check') == 0)
(root / 'complete').unlink()
checked('partial existing set refuses reinjection', inject() != 0)
# Model tmpfs loss, then a legacy Compose-created empty skeleton. All cleanup is confined
# to this container's synthetic root; no host path is mounted writable.
shutil.rmtree(root)
for name in ('api-persistent', 'api-volatile', 'worker-persistent', 'persistent', 'volatile'):
    (root / name).mkdir(parents=True)
checked('empty legacy skeleton is not silently replaced', inject() != 0)
for child in root.iterdir():
    child.rmdir()
root.rmdir()
checked('documented empty-directory recovery allows injection', inject() == 0)
print(f'total: {count}; failed: 0', flush=True)
