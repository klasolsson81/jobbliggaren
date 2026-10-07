import assert from 'node:assert/strict';
import test from 'node:test';
import { assertLocalDockerEndpoint, assertForwardedAddress, assertBodyAbortEvidence } from '../scripts/edge-probe-guards.mjs';

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

// The local Node fixture emits these shapes; mutations model broken telemetry, not product behavior.
function abortEvidence(status = 200) {
  return {
    bodyMs: 10000,
    controlId: 'control', incompleteId: 'incomplete',
    control: {status: 200, body: JSON.stringify({bytes: 100})},
    response: {elapsed: 10007, data: `HTTP/1.1 ${status} Result\r\nContent-Length: 0\r\n\r\n`},
    events: [
      {request: 'control', event: 'start', declaredBytes: 100, receivedBytes: 0, complete: false, elapsedMs: 0},
      {request: 'control', event: 'complete', declaredBytes: 100, receivedBytes: 100, complete: true, elapsedMs: 1},
      {request: 'control', event: 'close', declaredBytes: 100, receivedBytes: 100, complete: true, elapsedMs: 2},
      {request: 'incomplete', event: 'start', declaredBytes: 100, receivedBytes: 0, complete: false, elapsedMs: 0},
      {request: 'incomplete', event: 'aborted', declaredBytes: 100, receivedBytes: 1, complete: false, elapsedMs: 10000},
      {request: 'incomplete', event: 'close', declaredBytes: 100, receivedBytes: 1, complete: false, elapsedMs: 10001},
    ],
  };
}

test('assertBodyAbortEvidence_ShouldRecordAnomaly_WhenEmpty200HasVerifiedAbort', () => {
  assert.deepEqual(assertBodyAbortEvidence(abortEvidence()),
    {status: 200, protocolStatusAnomaly: true, receivedBytes: 1, declaredBytes: 100, completionDelta: 0});
});

test('assertBodyAbortEvidence_ShouldAccept_When504HasVerifiedAbort', () => {
  assert.equal(assertBodyAbortEvidence(abortEvidence(504)).protocolStatusAnomaly, false);
});

test('assertBodyAbortEvidence_ShouldAccept_WhenEmpty200UsesChunkedTermination', () => {
  const evidence = abortEvidence();
  evidence.response.data = 'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n';
  assert.equal(assertBodyAbortEvidence(evidence).protocolStatusAnomaly, true);
});

// The fixture injects framing outside the valid response contract; only safe rejection is asserted.
for (const [name, headers, body] of [
  ['ContentLengthAndChunkedHaveNoTerminalChunk', ['Content-Length: 0', 'Transfer-Encoding: chunked'], ''],
  ['ContentLengthsConflict', ['Content-Length: 0', 'Content-Length: 100'], ''],
  ['ContentLengthIsDuplicated', ['Content-Length: 0', 'Content-Length: 0'], ''],
  ['ContentLengthIsDuplicatedWithDifferentCase', ['Content-Length: 0', 'content-length: 0'], ''],
  ['ChunkedEncodingIsDuplicated', ['Transfer-Encoding: chunked', 'Transfer-Encoding: chunked'], '0\r\n\r\n'],
  ['ChunkedEncodingIsDuplicatedWithDifferentCase', ['Transfer-Encoding: chunked', 'transfer-encoding: chunked'], '0\r\n\r\n'],
  ['TransferEncodingHeadersConflict', ['Transfer-Encoding: chunked', 'Transfer-Encoding: gzip'], '0\r\n\r\n'],
  ['TransferEncodingIsUnsupported', ['Transfer-Encoding: gzip'], ''],
  ['ChunkedEncodingFollowsAnotherCoding', ['Transfer-Encoding: gzip, chunked'], '0\r\n\r\n'],
  ['ChunkedEncodingPrecedesAnotherCoding', ['Transfer-Encoding: chunked, gzip'], '0\r\n\r\n'],
  ['ContentLengthAndTerminatedChunkedArePresent', ['Content-Length: 0', 'Transfer-Encoding: chunked'], '0\r\n\r\n'],
  ['NonzeroContentLengthAndTerminatedChunkedArePresent', ['Content-Length: 100', 'Transfer-Encoding: chunked'], '0\r\n\r\n'],
]) {
  test(`assertBodyAbortEvidence_ShouldReject_When${name}`, () => {
    const evidence = abortEvidence();
    evidence.response.data = `HTTP/1.1 200 OK\r\n${headers.join('\r\n')}\r\n\r\n${body}`;
    assert.throws(() => assertBodyAbortEvidence(evidence));
  });
}

for (const [name, corrupt] of [
  ['all telemetry is missing', e => {e.events = [];}],
  ['positive instrumentation is missing', e => {e.events = e.events.filter(x => x.request !== 'control');}],
  ['incomplete instrumentation is missing', e => {e.events = e.events.filter(x => x.request !== 'incomplete');}],
  ['control telemetry is late', e => {e.events[2].elapsedMs = 15000;}],
  ['control telemetry is inconsistent', e => {e.events[0].receivedBytes = 1;}],
  ['terminal close is missing', e => {e.events.pop();}],
  ['request identity is wrong', e => {e.events.at(-1).request = 'another';}],
  ['truncated completion is falsely reported', e => {e.events.at(-2).event = 'complete';e.events.at(-2).complete = true;}],
  ['positive receipt is wrong', e => {e.control.body = '{"bytes":1}';}],
  ['control claims completion without all bytes', e => {e.events[1].receivedBytes = 1;}],
  ['client deadline is exceeded', e => {e.response.elapsed = 13500;}],
  ['terminal telemetry is late', e => {e.events.at(-1).elapsedMs = 13500;}],
  ['terminal timing is reversed', e => {e.events.at(-1).elapsedMs = 9999;}],
  ['received byte count is inconsistent', e => {e.events.at(-1).receivedBytes = 2;}],
  ['application success receipt is present', e => {e.response.data += '{"bytes":1}';}],
  ['200 has an unrecognized body', e => {e.response.data += 'success';}],
  ['200 declares an unseen body', e => {e.response.data = e.response.data.replace('Content-Length: 0', 'Content-Length: 100');}],
  ['another 2xx is reported', e => {e.response.data = e.response.data.replace('200', '201');}],
  ['response status is absent', e => {e.response.data = '';}],
]) {
  test(`assertBodyAbortEvidence_ShouldReject_When${name}`, () => {
    const evidence = abortEvidence();
    corrupt(evidence);
    assert.throws(() => assertBodyAbortEvidence(evidence));
  });
}
