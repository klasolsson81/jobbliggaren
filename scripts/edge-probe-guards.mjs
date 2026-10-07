import assert from 'node:assert/strict';
import {isIP} from 'node:net';

export function assertLocalDockerEndpoint(value) {
  assert(typeof value === 'string' &&
    (/^npipe:\/\/\/\/\.\/pipe\/[^/\\\s]+$/.test(value) ||
      /^unix:\/\/\/[^\s]+$/.test(value)),
  'Only a local Docker socket or local Windows named pipe is permitted');
}

export function assertForwardedAddress(value) {
  assert(typeof value === 'string' && isIP(value) !== 0,
    'The upstream must receive one valid forwarded IP address');
}

export function assertBodyAbortEvidence({response, bodyMs, control, controlId, incompleteId, events}) {
  assert(typeof controlId === 'string' && controlId.length > 0 &&
    typeof incompleteId === 'string' && incompleteId.length > 0 && controlId !== incompleteId,
  'Control and incomplete requests must have distinct identities');
  assert(Array.isArray(events) && events.length > 0, 'Body telemetry is required');
  for (const event of events) {
    assert(event && [controlId, incompleteId].includes(event.request) &&
      ['start', 'complete', 'aborted', 'close'].includes(event.event) &&
      event.declaredBytes === 100 && Number.isInteger(event.receivedBytes) &&
      event.receivedBytes >= 0 && event.receivedBytes <= 100 &&
      typeof event.complete === 'boolean' && Number.isFinite(event.elapsedMs) && event.elapsedMs >= 0,
    'Malformed or unrelated body telemetry');
  }
  const positive = events.filter(e => e.request === controlId);
  assert.deepEqual(positive.map(e => e.event), ['start', 'complete', 'close'],
    'Positive control must have complete terminal telemetry');
  assert.equal(control.status, 200);
  assert.equal(JSON.parse(control.body).bytes, 100, 'Positive control receipt must name 100 bytes');
  assert(positive[0].receivedBytes === 0 && positive.every((e, i) =>
    e.elapsedMs < 15000 && (i === 0 || e.elapsedMs >= positive[i - 1].elapsedMs)),
  'Positive control telemetry must be consistent and timely');
  assert(positive.slice(1).every(e => e.receivedBytes === 100 && e.complete),
    'Positive control must prove complete reception');
  const negative = events.filter(e => e.request === incompleteId);
  assert.deepEqual(negative.map(e => e.event), ['start', 'aborted', 'close'],
    'Incomplete request must have observed abort and terminal close');
  assert(negative.every(e => !e.complete) && negative[0].receivedBytes === 0 &&
    negative.slice(1).every(e => e.receivedBytes === 1),
  'Only the identified one-byte incomplete upload is evidence');
  assert(Number.isFinite(bodyMs) && bodyMs > 0 && bodyMs <= 15000);
  assert(negative.every((e, i) => e.elapsedMs < bodyMs + 3500 &&
    (i === 0 || e.elapsedMs >= negative[i - 1].elapsedMs)),
  'Missing, late or inconsistent terminal evidence fails');
  assert(Number.isFinite(response.elapsed) && response.elapsed >= bodyMs * 0.8 &&
    response.elapsed < bodyMs + 3500, 'Incomplete request exceeded its deadline');
  assert.equal(typeof response.data, 'string');
  const status = Number(response.data.match(/^HTTP\/1\.1 (\d{3})[^\r\n]*\r\n/)?.[1]);
  assert([200, 504].includes(status), 'Unexpected body-timeout wire status');
  const separator = response.data.indexOf('\r\n\r\n');
  assert(separator >= 0, 'Complete response headers are required');
  const body = response.data.slice(separator + 4);
  assert(!/"bytes"\s*:/.test(body), 'An application receipt contradicts an incomplete-upload abort');
  if (status === 200) {
    const fields = response.data.slice(0, separator).split('\r\n').slice(1).map(line => {
      const field = line.match(/^([^:\s]+):[ \t]*(.*)$/);
      assert(field, 'Malformed response field');
      return {name: field[1].toLowerCase(), value: field[2].trim().toLowerCase()};
    });
    const lengths = fields.filter(field => field.name === 'content-length');
    const encodings = fields.filter(field => field.name === 'transfer-encoding');
    assert((lengths.length === 1 && lengths[0].value === '0' && encodings.length === 0 && body === '') ||
      (lengths.length === 0 && encodings.length === 1 && encodings[0].value === 'chunked' && body === '0\r\n\r\n'),
    'An empty 200 requires one unambiguous framing field');
  }
  return {status, protocolStatusAnomaly: status === 200, receivedBytes: 1, declaredBytes: 100,
    completionDelta: negative.filter(e => e.event === 'complete').length};
}
