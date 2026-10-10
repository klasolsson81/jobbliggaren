import "server-only";

/** The two halves of a login link token, each 16 bytes (`RedisLoginChallengeStore.PutAsync`). */
const CHALLENGE_ID_BYTES = 16;
const TOKEN_BYTES = 32;

const BASE64URL_TOKEN = /^[A-Za-z0-9_-]{43}$/;

/**
 * The challenge a login link belongs to: the token is the Base64Url of the challenge id's 16 bytes followed by
 * a 16-byte secret, and the id comes back in the form the challenge response gave it. Null for anything else,
 * so a change to the token's layout reads as "no match" rather than as a wrong one.
 */
export function linkChallengeId(token: string): string | null {
  if (!BASE64URL_TOKEN.test(token)) return null;
  const bytes = Buffer.from(token, "base64url");
  if (bytes.length !== TOKEN_BYTES) return null;
  return bytes.subarray(0, CHALLENGE_ID_BYTES).toString("base64url");
}
