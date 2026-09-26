import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { authedFetchMock } = vi.hoisted(() => ({ authedFetchMock: vi.fn() }));

vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));

import { verifyBoundCode } from "./reauth-code";

// The backend's shapes, as its endpoints write them: a DomainError becomes ProblemDetails with the
// machine code in `title` (`DomainErrorResults.ToProblemResult`); a validator failure is `{ errors }`
// with no title (`Program.cs`, the ValidationException handler).
const problem = (status: number, title: string) =>
  new Response(JSON.stringify({ type: "about:blank", title, status, detail: "backend text" }), {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const SESSION = "session-under-test";
const PROOF = { challengeId: "challenge-under-test", code: "123456" };

describe("verifyBoundCode", () => {
  beforeEach(() => {
    authedFetchMock.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it.each([
    ["reauth", "/api/v1/auth/reauth/verify"],
    ["changeEmail", "/api/v1/auth/change-email/verify"],
  ] as const)("posts the challenge id and the code, and nothing else, for %s", async (step, path) => {
    authedFetchMock.mockResolvedValue(problem(410, "Auth.LoginCodeExpired"));

    await verifyBoundCode(step, SESSION, PROOF);

    expect(authedFetchMock).toHaveBeenCalledWith(SESSION, path, {
      method: "POST",
      body: JSON.stringify({ challengeId: "challenge-under-test", code: "123456" }),
    });
  });

  it("answers the re-authentication grant", async () => {
    authedFetchMock.mockResolvedValue(json(200, { reauthGrant: "grant-under-test" }));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({ ok: true, grant: "grant-under-test" });
  });

  it("answers the change-email grant", async () => {
    authedFetchMock.mockResolvedValue(json(200, { changeEmailGrant: "grant-under-test" }));

    expect(await verifyBoundCode("changeEmail", SESSION, PROOF)).toEqual({
      ok: true,
      grant: "grant-under-test",
    });
  });

  it("reads each step's own grant member: a reauth body is not a change-email grant", async () => {
    authedFetchMock.mockResolvedValue(json(200, { reauthGrant: "grant-under-test" }));

    expect(await verifyBoundCode("changeEmail", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "status",
      cause: "unavailable",
    });
  });

  it("tells the first wrong code from the one that leaves a single attempt", async () => {
    authedFetchMock.mockResolvedValueOnce(problem(400, "Auth.LoginCodeWrong"));
    authedFetchMock.mockResolvedValueOnce(problem(400, "Auth.LoginCodeWrongLastAttempt"));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({ ok: false, kind: "wrongCode" });
    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "wrongCode",
      lastAttempt: true,
    });
  });

  it("does not call a 400 without a code title a wrong code: it names no cause it cannot see", async () => {
    authedFetchMock.mockResolvedValue(json(400, { errors: { Code: ["'Code' is not in the correct format."] } }));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "status",
      cause: "unavailable",
    });
  });

  it("names the burn, and calls every other 410 expired", async () => {
    authedFetchMock.mockResolvedValueOnce(problem(410, "Auth.LoginCodeBurned"));
    authedFetchMock.mockResolvedValueOnce(problem(410, "Auth.LoginCodeExpired"));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "deadCode",
      reason: "burned",
    });
    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "deadCode",
      reason: "expired",
    });
  });

  it.each([
    [429, "tooManyAttempts"],
    [401, "notLoggedIn"],
    [503, "unavailable"],
    [500, "unavailable"],
  ] as const)("answers %i as a status: %s", async (status, cause) => {
    authedFetchMock.mockResolvedValue(json(status, { error: "x" }));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({ ok: false, kind: "status", cause });
  });

  it("answers a transport failure and a malformed 200 as unavailable, never as a grant", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    authedFetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    authedFetchMock.mockResolvedValueOnce(json(200, { reauthGrant: "" }));

    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "status",
      cause: "unavailable",
    });
    expect(await verifyBoundCode("reauth", SESSION, PROOF)).toEqual({
      ok: false,
      kind: "status",
      cause: "unavailable",
    });
  });

  it("writes the code, the challenge id and the grant to no console, even when a body fails to parse", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((level) =>
      vi.spyOn(console, level).mockImplementation(() => {})
    );
    authedFetchMock.mockResolvedValueOnce(json(200, { reauthGrant: "grant-under-test" }));
    authedFetchMock.mockResolvedValueOnce(json(200, { reauthGrant: 42 }));
    authedFetchMock.mockResolvedValueOnce(problem(400, "Auth.LoginCodeWrong"));

    await verifyBoundCode("reauth", SESSION, PROOF);
    await verifyBoundCode("reauth", SESSION, PROOF);
    await verifyBoundCode("reauth", SESSION, PROOF);

    const written = JSON.stringify(spies.flatMap((spy) => spy.mock.calls));
    for (const secret of ["123456", "challenge-under-test", "grant-under-test", SESSION]) {
      expect(written).not.toContain(secret);
    }
  });

  it("is server-only and never a Server Action: as an action it would be a public endpoint handing out grants", () => {
    const source = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "reauth-code.ts"), "utf8");

    expect(source).toMatch(/^import "server-only";/m);
    expect(source).not.toMatch(/^["']use server["']/m);
  });
});
