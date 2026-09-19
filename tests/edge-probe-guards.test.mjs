import assert from 'node:assert/strict';
import test from 'node:test';
import { assertLocalDockerEndpoint, assertForwardedAddress } from '../scripts/edge-probe-guards.mjs';

test('assertLocalDockerEndpoint_ShouldAccept_WhenNamedPipeIsLocal', () => {
  for (const endpoint of ['npipe:////./pipe/docker_engine', 'npipe:////./pipe/dockerDesktopLinuxEngine']) {
    assert.doesNotThrow(() => assertLocalDockerEndpoint(endpoint), endpoint);
  }
});

test('assertLocalDockerEndpoint_ShouldAccept_WhenUnixSocketHasAbsolutePath', () => {
  for (const endpoint of ['unix:///var/run/docker.sock', 'unix:///run/user/1000/docker.sock']) {
    assert.doesNotThrow(() => assertLocalDockerEndpoint(endpoint), endpoint);
  }
});

test('assertLocalDockerEndpoint_ShouldReject_WhenNamedPipeTargetsAnotherHost', () => {
  for (const endpoint of ['npipe://remote-server/pipe/docker_engine', 'npipe:////remote-server/pipe/docker_engine']) {
    assert.throws(() => assertLocalDockerEndpoint(endpoint), endpoint);
  }
});

test('assertLocalDockerEndpoint_ShouldReject_WhenTransportCanReachAnotherHost', () => {
  for (const endpoint of ['tcp://127.0.0.1:2375', 'ssh://remote-server', 'http://remote-server:2375', 'https://remote-server:2376', 'unix://remote-server/var/run/docker.sock']) {
    assert.throws(() => assertLocalDockerEndpoint(endpoint), endpoint);
  }
});

test('assertLocalDockerEndpoint_ShouldReject_WhenEndpointIsMissingOrMalformed', () => {
  for (const endpoint of [undefined, null, '', 'npipe:', 'npipe:////./pipe/', 'unix:', 'unix:///', 'unix:relative.sock']) {
    assert.throws(() => assertLocalDockerEndpoint(endpoint), String(endpoint));
  }
});

test('assertForwardedAddress_ShouldAccept_WhenHeaderContainsOneIpAddress', () => {
  for (const address of ['192.0.2.10', '203.0.113.20', '2001:db8::10', '::ffff:192.0.2.10']) {
    assert.doesNotThrow(() => assertForwardedAddress(address), address);
  }
});

test('assertForwardedAddress_ShouldReject_WhenBaselineHeaderIsAbsent', () => {
  const upstreamHeadersWithoutForwarding = {};
  assert.throws(() => assertForwardedAddress(upstreamHeadersWithoutForwarding['x-forwarded-for']));
  assert.throws(() => assertForwardedAddress(''));
});

test('assertForwardedAddress_ShouldReject_WhenHeaderIsNotOneValidIpAddress', () => {
  for (const address of [null, 123, ['192.0.2.10'], ' ', '192.0.2.10, 203.0.113.20', '999.0.2.10', 'client.example', '192.0.2.10:443', '[2001:db8::10]', '192.0.2.10\n']) {
    assert.throws(() => assertForwardedAddress(address), String(address));
  }
});
