#!/usr/bin/env python3
"""Verify installed, mounted and effective Redis policy without reading application keys."""
import fcntl
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import uuid

ROOT = Path('/run/jobbliggaren/redis')
COMPOSE = '/opt/jobbliggaren/deploy/docker-compose.yml'
LOCK = Path('/run/jobbliggaren-reconcile.lock')
POLICY = Path('/opt/jobbliggaren/deploy/redis')
DOCKER = '/usr/bin/docker'


def refuse():
    raise RuntimeError('Redis mounted/effective policy or authorization probe differs')


def run(*arguments, password=None):
    result = subprocess.run([DOCKER, *arguments], input=password, capture_output=True,
                            timeout=45, check=False)
    if result.returncode != 0:
        refuse()
    return result.stdout.decode('utf-8').strip()


def cli(container, image, user, password, *command):
    return run('run', '--rm', '-i', '--network', 'container:' + container,
               '--pull', 'never', '--read-only', '--user', '65534:65534',
               '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
               '--entrypoint', 'redis-cli', image, '-e', '--raw', '--user', user,
               '--askpass', '-h', '127.0.0.1', '-p', '6379', *command, password=password)


def render_policy(store, predecessor):
    passwords = {}
    for user in ('api-persistent', 'api-volatile', 'worker-persistent'):
        value = (ROOT / user / 'connection').read_text()
        parts = value.split(',password=', 1)
        if len(parts) != 2:
            refuse()
        passwords[user] = parts[1]
    for name in ('persistent', 'volatile'):
        passwords['health-' + name] = (ROOT / name / 'health-password').read_text()
        passwords['operator-' + name] = (ROOT / 'operator' / (name + '-password')).read_text()
    source = POLICY / 'predecessor-1976' if predecessor else POLICY
    policy = (source / (store + '.acl.template')).read_text()
    for user, password in passwords.items():
        if len(password) != 64 or any(c not in '0123456789abcdefABCDEF' for c in password):
            refuse()
        placeholder = '{{' + user.upper().replace('-', '_') + '_SHA256}}'
        policy = policy.replace(placeholder, hashlib.sha256(password.encode()).hexdigest())
    operator = (source / ('operator-' + store + '.acl.template')).read_text()
    operator = operator.replace('{{OPERATOR_SHA256}}',
                                hashlib.sha256(passwords['operator-' + store].encode()).hexdigest())
    policy += operator
    if '{{' in policy or '}}' in policy:
        refuse()
    return policy.encode()


def check_store(store, allowed):
    service = 'redis' if store == 'persistent' else 'redis-volatile'
    container = run('compose', '-f', COMPOSE, 'ps', '-q', service)
    if len(container) != 64 or any(c not in '0123456789abcdef' for c in container):
        refuse()
    metadata = json.loads(run('inspect', container))
    if len(metadata) != 1 or metadata[0]['State']['Running'] is not True:
        refuse()
    image = metadata[0]['Image']
    mounted = [m for m in metadata[0]['Mounts'] if m['Destination'] == '/run/redis-policy']
    if len(mounted) != 1 or mounted[0]['Source'] != str(ROOT / store) or mounted[0]['RW']:
        refuse()
    policy = ROOT / store / 'users.acl'
    digest = hashlib.sha256(policy.read_bytes()).hexdigest()
    actual = run('exec', container, 'sha256sum', '/run/redis-policy/users.acl').split()
    if len(actual) != 2 or actual[0] != digest:
        refuse()
    password = (ROOT / 'operator' / (store + '-password')).read_bytes()
    operator = 'operator-' + store
    active = cli(container, image, operator, password, 'ACL', 'LIST')
    matches = set()
    for format_name in allowed:
        oracle = 'jbl-acl-oracle-' + uuid.uuid4().hex
        with tempfile.NamedTemporaryFile(dir=ROOT, prefix='policy-oracle-', delete=False) as expected_file:
            expected_file.write(render_policy(store, format_name == 'predecessor'))
            expected_path = Path(expected_file.name)
        try:
            run('run', '--rm', '-d', '--name', oracle, '--network', 'none', '--pull', 'never',
                '--read-only', '--memory', '64m', '--pids-limit', '64', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--mount',
                'type=bind,src=' + str(expected_path) + ',dst=/policy.acl,readonly',
                '--entrypoint', 'redis-server', image, '--aclfile', '/policy.acl',
                '--save', '', '--appendonly', 'no')
            for attempt in range(20):
                probe = subprocess.run([DOCKER, 'exec', '-i', oracle, 'redis-cli', '-e', '--raw',
                                        '--user', operator, '--askpass', 'PING'], input=password,
                                       capture_output=True, timeout=5, check=False)
                if probe.returncode == 0 and probe.stdout.strip() == b'PONG':
                    break
            else:
                refuse()
            expected = run('exec', '-i', oracle, 'redis-cli', '-e', '--raw', '--user', operator,
                           '--askpass', 'ACL', 'LIST', password=password)
            if sorted(expected.splitlines()) == sorted(active.splitlines()):
                matches.add(format_name)
        finally:
            subprocess.run([DOCKER, 'rm', '-f', oracle], capture_output=True, timeout=15, check=False)
            expected_path.unlink(missing_ok=True)
    if not matches:
        refuse()
    config = cli(container, image, operator, password, 'CONFIG', 'GET', 'aclfile').splitlines()
    if config != ['aclfile', '/run/redis-policy/users.acl']:
        refuse()
    probes = []
    if store == 'persistent':
        for version in ('', 'v2:'):
            key = 'jobbliggaren:session:' + version + 'policy-probe'
            probes += [('api-persistent', ('HMGET', key, 'data'), True),
                       ('worker-persistent', ('HMGET', key, 'data'), False),
                       ('health-persistent', ('HMGET', key, 'data'), False)]
    else:
        for version in ('v1', 'v2'):
            for family, command in (('challenge', 'HGET'), ('challenge-bound', 'HGET'),
                                    ('account-email-change', 'HGET'), ('grant', 'GETDEL'),
                                    ('oauth-state', 'GETDEL')):
                key = 'jobbliggaren:auth/' + family + '/' + version + '/policy-probe'
                args = (command, key, 'p') if command == 'HGET' else (command, key)
                permitted = version == 'v1' or 'candidate' in matches
                probes += [('api-volatile', args, permitted), ('health-volatile', args, False)]
            probes.append(('api-volatile', ('GET', 'jobbliggaren:auth/challenge/'
                                            + version + '/policy-probe'), False))
    api = 'api-' + store
    probes += [(api, ('KEYS', '*'), False), (api, ('FLUSHDB',), False),
               (api, ('ACL', 'LIST'), False), (api, ('GET', 'foreign:policy-probe'), False)]
    for user, command, allowed in probes:
        response = cli(container, image, operator, password, 'ACL', 'DRYRUN', user, *command)
        if (response == 'OK') != allowed:
            refuse()
    return matches


def main():
    if os.geteuid() != 0 or len(sys.argv) != 5 or sys.argv[1:4] != ['--lock-fd', '9', '--policy']:
        refuse()
    formats = {'candidate': {'candidate'}, 'predecessor': {'predecessor'},
               'transition': {'candidate', 'predecessor'}}
    allowed = formats.get(sys.argv[4])
    if allowed is None:
        refuse()
    descriptor = os.fstat(9)
    expected = LOCK.stat()
    if (descriptor.st_dev, descriptor.st_ino) != (expected.st_dev, expected.st_ino):
        refuse()
    fcntl.flock(9, fcntl.LOCK_EX | fcntl.LOCK_NB)
    with LOCK.open('r') as other:
        try:
            fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            pass
        else:
            refuse()
    for store in ('persistent', 'volatile'):
        allowed = allowed & check_store(store, allowed)
        if not allowed:
            refuse()
    print('Installed, mounted and effective Redis policies and v1/v2 authorization probes verified.')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, ValueError, KeyError, subprocess.TimeoutExpired):
        print('REFUSING: Redis effective policy could not be verified; no application key was read.', file=sys.stderr)
        sys.exit(1)
