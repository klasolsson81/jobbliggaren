"""Isolated Linux CLI specifications for the account-access Redis policy gate.

Run with RedisPolicyFixture.Dockerfile, /repo read-only, tmpfs /run and
tmpfs /opt/test-area. No Docker socket, host credential or application service
is used. Only the subject's five absolute path constants are redirected.
Redis parses the shipped policies and performs every ACL LIST/DRYRUN operation.
Docker metadata and mount namespaces are a narrow transport double, not a
measurement of an actual host bind or image-provenance verification.
"""
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import unittest

REPO = Path('/repo')
AREA = Path('/opt/test-area')
ROOT = AREA / 'redis'
POLICY = AREA / 'deploy/redis'
LOCK = AREA / 'reconcile.lock'
SUBJECT = AREA / 'policy.py'
STATE = AREA / 'state.json'
CALLS = AREA / 'docker-calls.jsonl'
USERS = ('api-persistent', 'api-volatile', 'worker-persistent',
         'health-persistent', 'health-volatile',
         'operator-persistent', 'operator-volatile')
PASSWORDS = {user: hashlib.sha256(('policy-fixture-' + user).encode()).hexdigest()
             for user in USERS}
FAILURE_SECRET = hashlib.sha256(b'policy-fixture-helper-failure').hexdigest()


def guard_disposable_container():
    if sys.platform != 'linux' or os.geteuid() != 0:
        raise RuntimeError('Run only in the documented disposable Linux container.')
    if not REPO.is_dir() or not os.statvfs(REPO).f_flag & os.ST_RDONLY:
        raise RuntimeError('/repo must be a read-only repository mount.')
    mounts = {}
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        fields = line.split()
        mounts[fields[4]] = fields[fields.index('-') + 1]
    if mounts.get('/run') != 'tmpfs' or mounts.get(str(AREA)) != 'tmpfs':
        raise RuntimeError('/run and /opt/test-area must be dedicated tmpfs mounts.')
    if os.statvfs(AREA).f_flag & os.ST_NOEXEC:
        raise RuntimeError('/opt/test-area must allow the fixture executable; mount it with exec.')
    version = subprocess.run(['redis-server', '--version'], capture_output=True,
                             check=True).stdout
    if b'v=8.6.' not in version:
        raise RuntimeError('Use the repository-pinned Redis 8.6 fixture image.')
    if Path('/var/run/docker.sock').exists():
        raise RuntimeError('A host Docker socket must not be mounted into this fixture.')


def prepare_subject():
    (AREA / 'bin').mkdir()
    POLICY.parent.mkdir(parents=True)
    shutil.copytree(REPO / 'deploy/redis', POLICY)
    helper = AREA / 'bin/docker'
    helper.write_text((REPO / 'tests/Deployment/RedisPolicyDockerFixture.py').read_text(),
                      newline='\n')
    helper.chmod(0o700)
    source = (REPO / 'deploy/systemd/jobbliggaren-redis-policy.py').read_text()
    paths = {
        '/run/jobbliggaren/redis': str(ROOT),
        '/opt/jobbliggaren/deploy/docker-compose.yml': str(AREA / 'deploy/docker-compose.yml'),
        '/run/jobbliggaren-reconcile.lock': str(LOCK),
        '/opt/jobbliggaren/deploy/redis': str(POLICY),
        '/usr/bin/docker': str(helper),
    }
    for old, new in paths.items():
        if source.count("'" + old + "'") != 1:
            raise RuntimeError('Subject path mapping changed; review the fixture mapping.')
        source = source.replace("'" + old + "'", "'" + new + "'")
    SUBJECT.write_text(source)
    (AREA / 'deploy/docker-compose.yml').write_text('services: {}\n')


def rendered_policy(store, generation):
    # These are the exact predecessor/candidate template inputs supplied to the
    # deployment publisher. Real Redis below asserts that each resulting policy
    # is admissible. Corruptions are named operator/storage faults and assert only
    # the gate's safe refusal; no corruption is called a normal producer output.
    directory = POLICY if generation == 'candidate' else POLICY / 'predecessor-1976'
    policy = (directory / (store + '.acl.template')).read_text()
    for user, password in PASSWORDS.items():
        placeholder = '{{' + user.upper().replace('-', '_') + '_SHA256}}'
        policy = policy.replace(placeholder, hashlib.sha256(password.encode()).hexdigest())
    operator = (directory / ('operator-' + store + '.acl.template')).read_text()
    operator = operator.replace('{{OPERATOR_SHA256}}',
                                hashlib.sha256(PASSWORDS['operator-' + store].encode()).hexdigest())
    if '{{' in policy + operator or '}}' in policy + operator:
        raise RuntimeError('Fixture policy has an unresolved publisher placeholder.')
    return (policy + operator).encode()


class RedisPolicyVerificationTests(unittest.TestCase):
    def setUp(self):
        for directory in (ROOT, AREA / 'mounted', AREA / 'oracles'):
            if directory.exists():
                shutil.rmtree(directory)
            directory.mkdir()
        CALLS.unlink(missing_ok=True)
        self.processes = []
        self.state = {'services': {}, 'oracles': {}, 'failure_secret': FAILURE_SECRET}
        for user in USERS[:3]:
            directory = ROOT / user
            directory.mkdir()
            (directory / 'connection').write_text(
                'fixture:6379,user=' + user + ',password=' + PASSWORDS[user])
        (ROOT / 'operator').mkdir()
        for store in ('persistent', 'volatile'):
            (ROOT / store).mkdir()
            (ROOT / store / 'health-password').write_text(PASSWORDS['health-' + store])
            (ROOT / 'operator' / (store + '-password')).write_text(PASSWORDS['operator-' + store])
        self.install('candidate')

    def tearDown(self):
        for process in self.processes:
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=5)
        if STATE.exists():
            for oracle in json.loads(STATE.read_text())['oracles'].values():
                try:
                    os.kill(oracle['pid'], signal.SIGTERM)
                except ProcessLookupError:
                    pass
        # Redis oracle daemons reparent to this fixture's PID 1. Reap only this
        # disposable container's exited children, never a host process.
        deadline = time.monotonic() + .2
        while time.monotonic() < deadline:
            try:
                child, _ = os.waitpid(-1, os.WNOHANG)
                if child == 0:
                    time.sleep(.01)
                else:
                    continue
            except ChildProcessError:
                break

    def install(self, generation, alternate_acl_for=None):
        for process in self.processes:
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=5)
        self.processes = []
        self.state['services'] = {}
        self.state['oracles'] = {}
        for store, identifier in (('persistent', 'a'), ('volatile', 'b')):
            data = rendered_policy(store, generation)
            installed = ROOT / store / 'users.acl'
            installed.write_bytes(data)
            mounted = AREA / 'mounted' / store
            mounted.mkdir(exist_ok=True)
            mounted_acl = mounted / 'users.acl'
            mounted_acl.write_bytes(data)
            configured_acl = mounted_acl
            if store == alternate_acl_for:
                configured_acl = mounted / 'operator-selected.acl'
                configured_acl.write_bytes(data)
            socket = str(mounted / 'redis.sock')
            service = {'container': identifier * 64, 'socket': socket,
                       'mounted_acl': str(mounted_acl)}
            self.state['services'][store] = service
            log = mounted / 'redis.log'
            process = subprocess.Popen(
                ['redis-server', '--port', '0', '--unixsocket', socket,
                 '--unixsocketperm', '600', '--aclfile', str(configured_acl),
                 '--save', '', '--appendonly', 'no', '--logfile', str(log)],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            self.processes.append(process)
            deadline = time.monotonic() + 3
            while True:
                if process.poll() is not None:
                    self.fail('Shipped ACL policy did not start isolated Redis.')
                if Path(socket).exists():
                    response = self.direct(store, 'PING')
                    if response.returncode == 0 and response.stdout.strip() == b'PONG':
                        break
                if time.monotonic() >= deadline:
                    self.fail('Isolated Redis did not accept the shipped operator credential.')
                time.sleep(.01)
        self.save()

    def save(self):
        STATE.write_text(json.dumps(self.state))

    def direct(self, store, *command):
        return subprocess.run(
            ['redis-cli', '-e', '--raw', '-s', self.state['services'][store]['socket'],
             '--user', 'operator-' + store, '--askpass', *command],
            input=PASSWORDS['operator-' + store].encode(), capture_output=True,
            timeout=5, check=False)

    def invoke(self, policy='candidate', descriptor='held', arguments=None):
        command = [sys.executable, str(SUBJECT),
                   *(arguments if arguments is not None else ['--lock-fd', '9', '--policy', policy])]
        if descriptor == 'missing':
            result = subprocess.run(command, capture_output=True, timeout=40, check=False)
        else:
            path = AREA / 'wrong.lock' if descriptor == 'wrong' else LOCK
            with path.open('a+') as inherited:
                fcntl.flock(inherited, fcntl.LOCK_EX | fcntl.LOCK_NB)
                if descriptor == 'contended':
                    with LOCK.open('a+') as separate:
                        os.dup2(separate.fileno(), 9, inheritable=True)
                        try:
                            result = subprocess.run(command, pass_fds=(9,), capture_output=True,
                                                    timeout=40, check=False)
                        finally:
                            os.close(9)
                else:
                    os.dup2(inherited.fileno(), 9, inheritable=True)
                    try:
                        result = subprocess.run(command, pass_fds=(9,), capture_output=True,
                                                timeout=40, check=False)
                    finally:
                        os.close(9)
        transcript = result.stdout + result.stderr
        if CALLS.exists():
            transcript += CALLS.read_bytes()
        for secret in (*PASSWORDS.values(), FAILURE_SECRET):
            self.assertTrue(secret.encode() not in transcript, 'Fixture credential leaked.')
            self.assertTrue(hashlib.sha256(secret.encode()).hexdigest().encode() not in transcript,
                            'Fixture credential hash leaked.')
        return result

    def assert_refused(self, result):
        self.assertNotEqual(0, result.returncode, 'Policy uncertainty was accepted.')
        self.assertIn(b'REFUSING:', result.stderr, 'Refusal did not use the redacted outcome.')
        self.assertNotIn(b'Traceback', result.stderr, 'Dependency data escaped as a traceback.')
        self.assertNotIn(b'policies and v1/v2 authorization probes verified', result.stdout)

    def test_candidate_policy_passes_real_redis_oracle_and_all_probes(self):
        self.assertEqual(0, self.invoke().returncode, 'Complete candidate policy was refused.')
        calls = [json.loads(line) for line in CALLS.read_text().splitlines()]
        commands = [call[call.index('-p') + 2:] for call in calls
                    if call[0] == 'run' and '-p' in call]
        for version in ('v1', 'v2'):
            for family in ('challenge', 'challenge-bound', 'account-email-change', 'grant', 'oauth-state'):
                self.assertTrue(any(command[:2] == ['ACL', 'DRYRUN'] and
                                    any('/' + family + '/' + version + '/' in part for part in command)
                                    for command in commands), 'A required generation probe was skipped.')
        for forbidden in ('KEYS', 'FLUSHDB', 'ACL'):
            self.assertTrue(any(command[:2] == ['ACL', 'DRYRUN'] and command[3] == forbidden
                                for command in commands), 'A destructive/admin denial probe was skipped.')

    def test_predecessor_policy_passes_without_v2_authority(self):
        self.install('predecessor')
        self.assertEqual(0, self.invoke('predecessor').returncode,
                         'Complete predecessor policy was refused.')

    def test_transition_accepts_complete_predecessor(self):
        self.install('predecessor')
        self.assertEqual(0, self.invoke('transition').returncode,
                         'Valid interrupted preparation predecessor was refused.')

    def test_transition_accepts_complete_candidate(self):
        self.assertEqual(0, self.invoke('transition').returncode,
                         'Valid interrupted preparation candidate was refused.')

    def test_candidate_refuses_predecessor_and_predecessor_refuses_candidate(self):
        self.assert_refused(self.invoke('predecessor'))
        self.install('predecessor')
        self.assert_refused(self.invoke('candidate'))

    def test_candidate_indices_keep_their_explicit_legacy_and_v2_boundaries(self):
        for family in ('challenge-by-address', 'challenge-by-user', 'account-email-change-by-user'):
            for version in ('v1', 'v2'):
                key = 'jobbliggaren:auth/' + family + '/' + version + '/policy-probe'
                self.assertEqual(b'OK', self.direct('volatile', 'ACL', 'DRYRUN',
                                 'api-volatile', 'SET', key, 'fixture').stdout.strip())
                permitted = version == 'v1' or family == 'account-email-change-by-user'
                response = self.direct('volatile', 'ACL', 'DRYRUN', 'api-volatile', 'GET', key)
                self.assertEqual(permitted, response.stdout.strip() == b'OK')

    def test_predecessor_indices_never_gain_v2_authority(self):
        self.install('predecessor')
        for family in ('challenge-by-address', 'challenge-by-user', 'account-email-change-by-user'):
            key = 'jobbliggaren:auth/' + family + '/v2/policy-probe'
            for command in (('GET', key), ('SET', key, 'fixture')):
                response = self.direct('volatile', 'ACL', 'DRYRUN', 'api-volatile', *command)
                self.assertNotEqual(b'OK', response.stdout.strip())

    def test_mixed_generation_address_lua_is_only_authorized_by_candidate(self):
        command = ('ACL', 'DRYRUN', 'api-volatile', 'EVAL', 'return 1', '2',
                   'jobbliggaren:auth/account-email-change/v2/policy-probe',
                   'jobbliggaren:auth/account-email-change/v1/policy-probe')
        self.assertEqual(b'OK', self.direct('volatile', *command).stdout.strip())
        self.install('predecessor')
        self.assertNotEqual(b'OK', self.direct('volatile', *command).stdout.strip())

    def test_missing_inherited_descriptor_refuses_before_docker(self):
        self.assert_refused(self.invoke(descriptor='missing'))
        self.assertFalse(CALLS.exists())

    def test_descriptor_for_another_inode_refuses_before_docker(self):
        LOCK.touch()
        self.assert_refused(self.invoke(descriptor='wrong'))
        self.assertFalse(CALLS.exists())

    def test_another_lock_owner_excludes_the_checker(self):
        self.assert_refused(self.invoke(descriptor='contended'))
        self.assertFalse(CALLS.exists())

    def test_invalid_cli_shapes_refuse_before_docker(self):
        for args in ([], ['--policy', 'candidate'], ['--lock-fd', '8', '--policy', 'candidate'],
                     ['--lock-fd', '9', '--policy', 'unknown'],
                     ['--lock-fd', '9', '--policy', 'candidate', 'extra']):
            with self.subTest(arguments=args):
                self.assert_refused(self.invoke(arguments=args))
                self.assertFalse(CALLS.exists())

    def test_missing_or_ambiguous_compose_container_refuses(self):
        for identifier in ('', 'short-id', 'A' * 64, 'a' * 64 + '\n' + 'b' * 64):
            with self.subTest(identifier_shape=len(identifier)):
                self.state['container_output'] = {'persistent': identifier}
                self.save()
                self.assert_refused(self.invoke())

    def test_stopped_container_refuses(self):
        self.state['running'] = False
        self.save()
        self.assert_refused(self.invoke())

    def test_unreadable_container_metadata_refuses(self):
        self.state['malformed_metadata'] = True
        self.save()
        self.assert_refused(self.invoke())

    def test_wrong_mount_namespace_source_or_write_permission_refuses(self):
        for override in ({'Source': '/opt/test-area/stale-directory'},
                         {'Destination': '/another-mount'}, {'RW': True}):
            with self.subTest(mount=override):
                self.state['mount_override'] = override
                self.save()
                self.assert_refused(self.invoke())

    def test_missing_or_duplicate_policy_mount_refuses(self):
        for count in (0, 2):
            with self.subTest(mount_count=count):
                self.state['mount_count'] = count
                self.save()
                self.assert_refused(self.invoke())

    def test_installed_policy_differing_from_mounted_directory_refuses(self):
        # Operator directory replacement can leave a live bind on the old inode.
        (ROOT / 'volatile/users.acl').write_bytes(rendered_policy('volatile', 'predecessor'))
        self.assert_refused(self.invoke('transition'))

    def test_partial_installed_policy_refuses(self):
        (ROOT / 'volatile/users.acl').unlink()
        self.assert_refused(self.invoke('transition'))

    def test_operator_active_acl_change_refuses_even_when_files_match(self):
        changed = self.direct('volatile', 'ACL', 'SETUSER', 'unexpected-fixture-user',
                              'on', 'nopass', '+ping')
        self.assertEqual(0, changed.returncode, 'Fault actor did not change the active ACL.')
        self.assert_refused(self.invoke('transition'))

    def test_operator_active_password_drift_refuses_even_when_files_match(self):
        changed = self.direct('volatile', 'ACL', 'SETUSER', 'api-volatile', 'resetpass',
                              '#' + hashlib.sha256(b'fixture-drift').hexdigest())
        self.assertEqual(0, changed.returncode, 'Fault actor did not change the active password.')
        self.assert_refused(self.invoke())

    def test_malformed_retained_credential_refuses_without_a_traceback(self):
        path = ROOT / 'api-volatile/connection'
        original = path.read_text()
        for malformed in ('missing-password-delimiter', 'fixture,password=not-hex',
                          original + '\n'):
            with self.subTest(credential_shape=len(malformed)):
                path.write_text(malformed)
                self.assert_refused(self.invoke())
        path.write_text(original)

    def test_unknown_publisher_placeholder_refuses(self):
        template = POLICY / 'volatile.acl.template'
        original = template.read_bytes()
        try:
            template.write_bytes(original + b'\n{{UNRECOGNIZED_SECRET}}\n')
            self.assert_refused(self.invoke())
        finally:
            template.write_bytes(original)

    def test_active_config_points_at_the_actual_policy_mount(self):
        # The fixture operator selects another real ACL file at server boot.
        # Its bytes and effective ACL stay identical to the expected mount, so
        # only the subject's actual CONFIG GET aclfile gate can catch this fault.
        self.install('candidate', alternate_acl_for='volatile')
        alternate = AREA / 'mounted/volatile/operator-selected.acl'
        configured = self.direct('volatile', 'CONFIG', 'GET', 'aclfile')
        self.assertEqual([b'aclfile', str(alternate).encode()], configured.stdout.splitlines(),
                         'The fixture operator did not select the alternate boot configuration.')
        self.assertTrue(alternate.read_bytes() ==
                        Path(self.state['services']['volatile']['mounted_acl']).read_bytes(),
                        'The boot-configuration fault must preserve the policy bytes.')
        self.assert_refused(self.invoke())
        calls = [json.loads(line) for line in CALLS.read_text().splitlines()]
        self.assertTrue(any(call[-3:] == ['CONFIG', 'GET', 'aclfile'] and
                            self.state['services']['volatile']['container'] in
                            ' '.join(call) for call in calls),
                        'Refusal happened before the intended active-configuration gate.')

    def test_dependency_failures_never_leak_plaintext_or_password_hashes(self):
        for failure in ('compose', 'inspect', 'mounted-digest', 'acl-list', 'oracle-start',
                        'oracle-ping', 'oracle-list', 'config', 'dryrun'):
            with self.subTest(helper=failure):
                self.state['failure'] = failure
                self.save()
                self.assert_refused(self.invoke())
                state = json.loads(STATE.read_text())
                self.assertIn(failure, state.get('observed_failures', []),
                              'Refusal happened before the intended dependency fault.')
                self.assertFalse(state['oracles'],
                                 'Failed helper left an oracle registered.')
                self.assertFalse(list(ROOT.glob('policy-oracle-*')),
                                 'Failed helper left a temporary password-hash policy.')

    def test_positive_probe_refuses_if_active_permissions_change_after_acl_snapshot(self):
        self.state['late_acl'] = {'command': 'HMGET', 'rules': ['clearselectors']}
        self.save()
        self.assert_refused(self.invoke())
        self.assertTrue('late_acl' not in json.loads(STATE.read_text()),
                        'The real permission change was not reached.')

    def test_negative_probe_refuses_if_active_permissions_expand_after_acl_snapshot(self):
        self.state['late_acl'] = {'command': 'KEYS', 'rules': ['+keys']}
        self.save()
        self.assert_refused(self.invoke())
        self.assertTrue('late_acl' not in json.loads(STATE.read_text()),
                        'The real permission expansion was not reached.')


if __name__ == '__main__':
    guard_disposable_container()
    prepare_subject()
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(RedisPolicyVerificationTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    failed = len(result.failures) + len(result.errors)
    print(f'total: {result.testsRun}; failed: {failed}', flush=True)
    sys.exit(0 if result.wasSuccessful() else 1)
