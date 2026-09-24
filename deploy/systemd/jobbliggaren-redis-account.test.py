"""Fixed-operation helper checks in the disposable Linux provisioning fixture."""
import json
import runpy
from pathlib import Path
import subprocess
import shutil

fixture = runpy.run_path('/repo/deploy/systemd/jobbliggaren-redis-secrets.test.py')
base, root, credentials = (fixture[name] for name in ('base', 'root', 'credentials'))
checked = fixture['checked']
script = base / 'deploy/systemd/jobbliggaren-redis-account.sh'
shutil.copyfile('/repo/deploy/systemd/jobbliggaren-redis-account.sh', script)
commands = Path('/tmp/account-commands')
mode = Path('/tmp/account-mode')
mode.write_text('normal')
Path('/usr/bin/docker').write_text('''#!/usr/bin/env python3
import json, pathlib, subprocess, sys
args = sys.argv[1:]
mode = pathlib.Path('/tmp/account-mode').read_text()
if args[0] == 'compose':
    print('a' * 64)
elif args[0] == 'inspect':
    print(('false' if mode == 'down' else 'true') + ' sha256:' + 'b' * 64)
elif args[0] == 'rm':
    pass
elif args[0] == 'run':
    assert '--read-only' in args and '--cap-drop' in args
    assert args[args.index('--network') + 1] == 'container:' + 'a' * 64
    assert args[args.index('--entrypoint') + 1] == 'redis-cli'
    assert args[args.index('--user') + 1] == '65534:65534'
    assert not any(a in args for a in ('--env', '-v', '--volume', '--mount')) and args.count('-e') == 1
    cli = args[args.index('sha256:' + 'b' * 64) + 1:]
    assert cli[:12] == ['-e', '--raw', '--user', 'operator-persistent', '--askpass', '-h', '127.0.0.1', '-p', '6379', '-n', '0', cli[11]]
    password = sys.stdin.read()
    expected = pathlib.Path('/run/jobbliggaren/redis/operator/persistent-password').read_text()
    assert password == expected and password not in ' '.join(args)
    with open('/tmp/account-commands', 'a') as log:
        log.write(json.dumps(cli[11:]) + '\\n')
    if mode == 'real':
        result = subprocess.run(['redis-cli', *cli], input=password, text=True, capture_output=True)
        sys.stdout.write(result.stdout); sys.stderr.write(result.stderr); sys.exit(result.returncode)
    if mode == 'auth':
        print('WRONGPASS ' + password); sys.exit(1)
    if mode == 'misleading':
        print('OK\\nERR ' + password); sys.exit(0)
    if mode == 'missing':
        print('OK' if cli[11] == 'SET' else '0'); sys.exit(0)
    if cli[11] == 'SET':
        pathlib.Path('/tmp/account-marker').touch(); print('OK')
    elif cli[11] == 'DEL':
        pathlib.Path('/tmp/account-marker').unlink(missing_ok=True); print('1')
    elif cli[11] == 'EXISTS':
        print('1' if pathlib.Path('/tmp/account-marker').exists() else '0')
    else:
        sys.exit(90)
else:
    sys.exit(90)
''')
Path('/usr/bin/docker').chmod(0o755)
account = 'A1B2C3D4-1111-2222-3333-444455556666'
key = 'jobbliggaren:user:' + account.lower() + ':deleted'
session = 'jobbliggaren:session:' + 'A' * 43

def run(*args):
    result = subprocess.run(['bash', str(script), *args], capture_output=True, timeout=30)
    assert not any(c.encode() in result.stdout + result.stderr for c in credentials), 'credential leaked'
    assert account.encode() not in result.stdout + result.stderr, 'account echoed'
    return result

def calls():
    return [json.loads(line) for line in commands.read_text().splitlines()] if commands.exists() else []

invalid = [(), ('mark-deleted', account), ('mark-deleted', 'invalid', '--ttl-seconds', '10'),
           ('check-deleted', account, 'extra'), ('delete-known-session', account, '--session-key', 'jobbliggaren:session:*'),
           ('delete-known-session', account, '--session-key', session[:-1] + 'B'),
           ('arbitrary', account)]
invalid += [('mark-deleted', account, '--ttl-seconds', ttl) for ttl in ('', '0', '-1', '1.5', '1e3', ' 10', '01', '9223372036854775808')]
for args in invalid:
    before = calls()
    checked('invalid input refuses before Redis: ' + repr(args[:1]), run(*args).returncode != 0 and calls() == before)
checked('explicit TTL and canonical account reach SET unchanged', run('mark-deleted', account, '--ttl-seconds', '2592000').returncode == 0 and calls()[-2:] == [['SET', key, '1', 'EX', '2592000'], ['EXISTS', key]])
checked('check reports presence', b'present' in run('check-deleted', account).stdout)
checked('clear verifies absence', run('clear-deleted', account).returncode == 0 and calls()[-2:] == [['DEL', key], ['EXISTS', key]])
checked('check reports absence', b'absent' in run('check-deleted', account).stdout)
checked('delete index constructs account key', run('delete-session-index', account).returncode == 0 and calls()[-1] == ['DEL', key.replace(':deleted', ':sessions')])
checked('delete exact known session', run('delete-known-session', account, '--session-key', session).returncode == 0 and calls()[-1] == ['DEL', session])
for value in ('auth', 'down', 'misleading', 'missing'):
    mode.write_text(value)
    checked(value + ' refuses without success receipt', run('mark-deleted', account, '--ttl-seconds', '10').returncode != 0)
if shutil.which('redis-server'):
    import time
    data = Path('/tmp/account-redis'); data.mkdir()
    server = subprocess.Popen(['redis-server', '--aclfile', str(root / 'persistent/users.acl'),
                               '--save', '', '--appendonly', 'yes', '--dir', str(data)],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            probe = subprocess.run(['redis-cli', '-e', '--user', 'operator-persistent', '--askpass', 'PING'],
                                   input=credentials[5], text=True, capture_output=True)
            if probe.returncode == 0 and probe.stdout.strip() == 'PONG': break
            time.sleep(.05)
        else: raise AssertionError('Redis did not become ready')
        mode.write_text('real')
        checked('real redis-cli marks and verifies the tombstone', run('mark-deleted', account, '--ttl-seconds', '2592000').returncode == 0)
        checked('real redis-cli reports presence', b'present' in run('check-deleted', account).stdout)
        checked('real redis-cli clears and verifies absence', run('clear-deleted', account).returncode == 0)
        checked('real redis-cli deletes absent index safely', run('delete-session-index', account).returncode == 0)
        checked('real redis-cli deletes absent known session safely', run('delete-known-session', account, '--session-key', session).returncode == 0)
        password_file = root / 'operator/persistent-password'
        # A syntactically valid complete replacement set with an unchanged live Redis policy
        # models a credential/policy mismatch, through the actual CLI rather than a stub reply.
        live_password = password_file.read_bytes()
        acl = root / 'persistent/users.acl'; live_acl = acl.read_bytes()
        import hashlib
        wrong = 'F' * 64
        acl.write_bytes(live_acl.replace(hashlib.sha256(live_password).hexdigest().encode(), hashlib.sha256(wrong.encode()).hexdigest().encode()))
        password_file.write_text(wrong)
        checked('real redis-cli rejects wrong authentication', run('check-deleted', account).returncode != 0)
        password_file.write_bytes(live_password); acl.write_bytes(live_acl)
        server.terminate(); server.wait(timeout=10)
        checked('real redis-cli refuses Redis outage', run('check-deleted', account).returncode != 0)
    finally:
        if server.poll() is None: server.terminate(); server.wait(timeout=10)
print(f'total: {checked.__globals__["count"]}; failed: 0', flush=True)
