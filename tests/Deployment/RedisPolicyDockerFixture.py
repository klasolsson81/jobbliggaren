#!/usr/bin/env python3
"""Docker transport double; ACL LIST and DRYRUN always use real isolated Redis.

The fixture projects local sockets/files into container namespaces. It does not
prove Docker's bind-mount implementation, daemon identity or image verification.
"""
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

AREA = Path('/opt/test-area')
STATE = AREA / 'state.json'
CALLS = AREA / 'docker-calls.jsonl'


def save(state):
    STATE.write_text(json.dumps(state))


def redis(service, user, password, command):
    return subprocess.run(
        ['redis-cli', '-e', '--raw', '-s', service['socket'], '--user', user,
         '--askpass', *command], input=password, capture_output=True, timeout=5,
        check=False)


def leak_and_fail(state, gate):
    # Failed dependencies can print sensitive diagnostics. The subject must
    # discard them; the test harness never forwards these captured bytes.
    secret = state['failure_secret'].encode()
    state.setdefault('observed_failures', []).append(gate)
    save(state)
    os.write(1, secret + b'\n')
    os.write(2, hashlib.sha256(secret).hexdigest().encode() + b'\n')
    return 71


def start_oracle(name, policy, state):
    directory = AREA / 'oracles' / name
    directory.mkdir(parents=True)
    socket = str(directory / 'redis.sock')
    pid = directory / 'redis.pid'
    result = subprocess.run(
        ['redis-server', '--port', '0', '--unixsocket', socket,
         '--unixsocketperm', '600', '--aclfile', str(policy), '--save', '',
         '--appendonly', 'no', '--daemonize', 'yes', '--pidfile', str(pid),
         '--logfile', str(directory / 'redis.log')],
        capture_output=True, timeout=5, check=False)
    if result.returncode:
        return result.returncode
    deadline = time.monotonic() + 3
    while not pid.is_file() or not Path(socket).exists():
        if time.monotonic() >= deadline:
            return 72
        time.sleep(.01)
    state['oracles'][name] = {'socket': socket, 'pid': int(pid.read_text())}
    save(state)
    print('e' * 64)
    return 0


def main():
    state = json.loads(STATE.read_text())
    args = sys.argv[1:]
    # Recording argv catches a password-argument regression without recording stdin.
    with CALLS.open('a') as log:
        log.write(json.dumps(args) + '\n')
    if args[0] == 'compose':
        if state.get('failure') == 'compose':
            return leak_and_fail(state, 'compose')
        store = 'persistent' if args[-1] == 'redis' else 'volatile'
        print(state.get('container_output', {}).get(store,
                                                   state['services'][store]['container']))
        return 0
    if args[0] == 'inspect':
        if state.get('failure') == 'inspect':
            return leak_and_fail(state, 'inspect')
        if state.get('malformed_metadata'):
            print('not-json')
            return 0
        store, service = next((store, value) for store, value in state['services'].items()
                              if value['container'] == args[1])
        mount = {'Destination': '/run/redis-policy',
                 'Source': str(AREA / 'redis' / store), 'RW': False}
        mount.update(state.get('mount_override', {}))
        mounts = [mount] * state.get('mount_count', 1)
        print(json.dumps([{'State': {'Running': state.get('running', True)},
                           'Image': 'sha256:' + 'd' * 64, 'Mounts': mounts}]))
        return 0
    if args[0] == 'rm':
        oracle = state['oracles'].pop(args[-1], None)
        if oracle:
            try:
                os.kill(oracle['pid'], signal.SIGTERM)
            except ProcessLookupError:
                pass
            save(state)
        return 0
    if args[0] == 'run' and '--entrypoint' in args:
        entrypoint = args[args.index('--entrypoint') + 1]
        if entrypoint == 'redis-server':
            if state.get('failure') == 'oracle-start':
                return leak_and_fail(state, 'oracle-start')
            name = args[args.index('--name') + 1]
            options = dict(item.split('=', 1) for item in
                           args[args.index('--mount') + 1].split(',') if '=' in item)
            # This process shares the subject's namespace, unlike a real daemon.
            return start_oracle(name, Path(options['src']), state)
        container = args[args.index('--network') + 1].removeprefix('container:')
        store, service = next((store, value) for store, value in state['services'].items()
                              if value['container'] == container)
        command = args[args.index('-p') + 2:]
        failure = ('acl-list' if command == ['ACL', 'LIST'] else
                   'config' if command[:2] == ['CONFIG', 'GET'] else 'dryrun')
        if state.get('failure') == failure:
            return leak_and_fail(state, failure)
        password = sys.stdin.buffer.read()
        user = args[args.index('--user', args.index('--entrypoint')) + 1]
        late = state.get('late_acl')
        if late and command[:2] == ['ACL', 'DRYRUN'] and command[3] == late['command']:
            changed = redis(service, user, password,
                            ['ACL', 'SETUSER', command[2], *late['rules']])
            if changed.returncode:
                return changed.returncode
            state.pop('late_acl')
            save(state)
        result = redis(service, user, password, command)
        output = result.stdout
        if command == ['CONFIG', 'GET', 'aclfile']:
            # Project only the actual configured mount file into the namespace.
            configured = output.decode().splitlines()
            if configured == ['aclfile', service['mounted_acl']]:
                output = b'aclfile\n/run/redis-policy/users.acl\n'
        os.write(1, output)
        os.write(2, result.stderr)
        return result.returncode
    if args[0] == 'exec':
        if args[-1] == '/run/redis-policy/users.acl':
            if state.get('failure') == 'mounted-digest':
                return leak_and_fail(state, 'mounted-digest')
            service = next(value for value in state['services'].values()
                           if value['container'] == args[1])
            digest = hashlib.sha256(Path(service['mounted_acl']).read_bytes()).hexdigest()
            print(digest + '  /run/redis-policy/users.acl')
            return 0
        name = args[args.index('-i') + 1]
        oracle = state['oracles'][name]
        command = args[args.index('--askpass') + 1:]
        failure = 'oracle-ping' if command == ['PING'] else 'oracle-list'
        if state.get('failure') == failure:
            return leak_and_fail(state, failure)
        result = redis(oracle, args[args.index('--user') + 1],
                       sys.stdin.buffer.read(), command)
        os.write(1, result.stdout)
        os.write(2, result.stderr)
        return result.returncode
    return 73


if __name__ == '__main__':
    sys.exit(main())
