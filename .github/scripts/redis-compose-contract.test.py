"""Assert the resolved manifests, including anchors and long-form bind properties."""
import json
import os
from pathlib import Path
import re
import subprocess

root = Path(__file__).resolve().parents[2]
contract = json.loads((root / 'deploy/redis/service-boundaries.json').read_text())
env = os.environ.copy()
for source in ('deploy/docker-compose.yml', 'docker-compose.yml'):
    for key in re.findall(r'\$\{([A-Z_]+):\?', (root / source).read_text(encoding='utf-8')):
        env[key] = 'synthetic-contract-value'
env['JBL_WEB_API_SUBNET'] = '172.30.0.0/24'

def render(source):
    return json.loads(subprocess.check_output(['docker', 'compose', '--profile', '*', '--env-file', os.devnull, '-f', str(root / source), 'config', '--format', 'json'], env=env))

model = render('deploy/docker-compose.yml')
services = model['services']
alias = lambda name: 'redis' if name == 'redis-persistent' else name
expected = {frozenset(map(alias, pair)) for pair in contract['internalPairs']}
actual = []
for network, properties in model['networks'].items():
    members = frozenset(name for name, service in services.items() if network in service.get('networks', {}))
    if properties.get('internal', False):
        actual.append(members)
    else:
        assert len(members) == 1 and next(iter(members)) in contract['egressOwners'], 'unexpected egress membership'
assert len(actual) == len(expected) and set(actual) == expected, 'internal bridge pairs differ'
assert sum(not n.get('internal', False) for n in model['networks'].values()) == len(contract['egressOwners'])
assert services['api']['environment']['ForwardedHeaders__KnownNetworks__0'] == env['JBL_WEB_API_SUBNET']
assert model['networks']['web-api']['ipam']['config'][0]['subnet'] == env['JBL_WEB_API_SUBNET']
expected_secrets = {'api': {'api-persistent', 'api-volatile'}, 'worker': {'worker-persistent'}, 'redis': {'persistent'}, 'redis-volatile': {'volatile'}}
for name, service in services.items():
    mounts = [m for m in service.get('volumes', []) if m.get('source', '').startswith('/run/jobbliggaren/redis/')]
    assert {m['source'].rsplit('/', 1)[1] for m in mounts} == expected_secrets.get(name, set()), 'wrong credential recipient'
    assert all(m['type'] == 'bind' and m['read_only'] and not m.get('bind', {}).get('create_host_path', False) for m in mounts), 'unsafe credential mount'
    assert not any('password=' in str(v) for k, v in service.get('environment', {}).items() if k.startswith('ConnectionStrings__Redis')), 'credential in environment'
assert services['worker']['read_only'] and '/tmp:size=16m,mode=1777' in services['worker']['tmpfs']
assert services['worker']['healthcheck']['test'] == ['CMD', 'dotnet', 'Jobbliggaren.Worker.dll', '--readiness-probe']
for model, name in ((model, 'redis-volatile'), (render('docker-compose.yml'), 'redis-volatile-dev')):
    volatile = model['services'][name]
    assert volatile['read_only']
    assert all(m['type'] == 'bind' and m['read_only'] and m['target'] != '/data' for m in volatile['volumes'])
    policy = next(m for m in volatile['volumes'] if m['target'] == '/run/redis-policy')
    assert not policy.get('bind', {}).get('create_host_path', False)
    command = volatile['command']
    assert command[command.index('--save') + 1] == '' and command[command.index('--appendonly') + 1] == 'no'
print('PASS resolved deploy/dev network, identity, mount, forwarding and readiness contracts')
