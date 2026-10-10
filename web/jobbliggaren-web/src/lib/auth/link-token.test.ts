import { describe, expect, it } from "vitest";
import { linkChallengeId } from "./link-token";

// The fixture is built the way RedisLoginChallengeStore.PutAsync builds a token: Base64Url of the challenge id's
// 16 bytes followed by a 16-byte secret.
const idBytes = Buffer.from(Array.from({ length: 16 }, (_, i) => 200 + i));
const token = Buffer.concat([idBytes, Buffer.alloc(16, 3)]).toString("base64url");

describe("linkChallengeId", () => {
  it("reads the challenge id in the form the challenge response gives it", () => {
    expect(token).toHaveLength(43);
    expect(linkChallengeId(token)).toBe(idBytes.toString("base64url"));
  });

  it.each(["", "link-token", `${token}A`, token.slice(1), `${token.slice(0, 42)}+`, `${token.slice(0, 42)}=`])(
    "is null for %j, which is not a token of that layout",
    (value) => {
      expect(linkChallengeId(value)).toBeNull();
    }
  );
});
