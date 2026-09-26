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
