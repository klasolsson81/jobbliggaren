import "server-only";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import type { CodeProof } from "@/lib/auth/reauth-action-state";
import { parseResponse } from "@/lib/dto/_helpers";
import { changeEmailGrantSchema, reauthGrantSchema } from "@/lib/dto/reauth";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";

// Presents a bound code and answers the grant it buys. Deliberately NOT a Server Action: an exported
// `"use server"` function is a public POST endpoint, and this one would hand out grants. It is called
// only from inside the actions that spend the grant in the same request, so a grant never leaves the
// server (ADR 0142 D5, #1740).
//
// It classifies and never translates: the caller owns the copy. The code, the challenge id and the grant
// never reach a console.

/** Which bound challenge the code answers: the account's own address, or the new one. */
export type BoundCodeStep = "reauth" | "changeEmail";

export type BoundCodeRefusal =
  /** The code was wrong; `lastAttempt` when exactly one attempt is left before it burns. */
  | { kind: "wrongCode"; lastAttempt?: true }
  /** The code can no longer be used. Only `burned` has a known cause (three wrong attempts). */
  | { kind: "deadCode"; reason: "expired" | "burned" }
  /** Nothing the user typed: a throttle, a lapsed session, or the service. */
  | { kind: "status"; cause: "tooManyAttempts" | "notLoggedIn" | "unavailable" };

export type BoundCodeResult = { ok: true; grant: string } | ({ ok: false } & BoundCodeRefusal);

const STEPS = {
  reauth: {
    path: "/api/v1/auth/reauth/verify",
    read: async (res: Response) =>
      (await parseResponse(res, reauthGrantSchema, "POST /api/v1/auth/reauth/verify")).reauthGrant,
  },
  changeEmail: {
    path: "/api/v1/auth/change-email/verify",
    read: async (res: Response) =>
      (await parseResponse(res, changeEmailGrantSchema, "POST /api/v1/auth/change-email/verify"))
        .changeEmailGrant,
  },
} as const;

export async function verifyBoundCode(
  step: BoundCodeStep,
  sessionId: string,
  proof: CodeProof
): Promise<BoundCodeResult> {
  const { path, read } = STEPS[step];

  let res: Response;
  try {
    res = await authedFetch(sessionId, path, {
      method: "POST",
      body: JSON.stringify({ challengeId: proof.challengeId, code: proof.code }),
    });
  } catch {
    return { ok: false, kind: "status", cause: "unavailable" };
  }

  if (res.status === 400) {
    // Exact titles only. Any other 400 is a malformed request the input schemas exist to prevent,
    // and a wrong-code message for it would claim a cause nobody measured.
    const title = await readProblemTitle(res);
    if (title === AUTH_ERROR_CODES.LoginCodeWrongLastAttempt) {
      return { ok: false, kind: "wrongCode", lastAttempt: true };
    }
    if (title === AUTH_ERROR_CODES.LoginCodeWrong) return { ok: false, kind: "wrongCode" };
    return { ok: false, kind: "status", cause: "unavailable" };
  }
  if (res.status === 410) {
    // `expired` names no cause, so it can stand for every 410 that is not the burn: the backend
    // answers the same for a code that ran out, was used, was replaced, or never had a record.
    const title = await readProblemTitle(res);
    return {
      ok: false,
      kind: "deadCode",
      reason: title === AUTH_ERROR_CODES.LoginCodeBurned ? "burned" : "expired",
    };
  }
  if (res.status === 429) return { ok: false, kind: "status", cause: "tooManyAttempts" };
  if (res.status === 401) return { ok: false, kind: "status", cause: "notLoggedIn" };
  if (!res.ok) return { ok: false, kind: "status", cause: "unavailable" };

  try {
    return { ok: true, grant: await read(res) };
  } catch {
    return { ok: false, kind: "status", cause: "unavailable" };
  }
}
