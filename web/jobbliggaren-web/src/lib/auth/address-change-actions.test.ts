import { beforeEach, describe, expect, it, vi } from "vitest";

// #1975 — the Server Action behind /adressbyte. What it pins: the fields are checked before anything is sent; the
// request is anonymous (the client's forwarded headers, no cookie read, never cached); one 410 is every refusal and
// never says which input; a 409 that reads is "not yet"; 429 and 503 are safe to retry; every other answer, a lost
// one and one that does not read among them, claims nothing. The addresses come back to the form, the code never.

const mocks = vi.hoisted(() => ({ fetch: vi.fn(), cookies: vi.fn(), headers: vi.fn() }));

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("next/headers", () => ({ cookies: mocks.cookies, headers: mocks.headers }));

import { completeAddressChange } from "./address-change-actions";

const CURRENT = "anna@exempel.se";
const NEW = "anna.ny@exempel.se";
const CODE = "482915";
const TYPED = { currentEmail: ` ${CURRENT} `, newEmail: `${NEW} ` };

function form(fields: Record<string, string>): FormData {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) data.set(name, value);
  return data;
}

const filled = (overrides: Record<string, string> = {}) => form({ ...TYPED, code: ` ${CODE}`, ...overrides });

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/problem+json" } });
const problem = (status: number, title: string, extra: Record<string, unknown> = {}) =>
  json(status, { type: "about:blank", title, status, detail: "backend text", ...extra });

const complete = (data: FormData = filled()) => completeAddressChange(null, data);

beforeEach(() => {
  vi.clearAllMocks();
  mocks.headers.mockResolvedValue(new Headers({ "x-forwarded-for": "203.0.113.7", "x-forwarded-proto": "https" }));
  vi.stubGlobal("fetch", mocks.fetch);
});

describe("completeAddressChange", () => {
  it("posts the trimmed addresses and the code, anonymously, with the client's forwarded headers, never cached", async () => {
    mocks.fetch.mockResolvedValue(new Response(null, { status: 204 }));

    expect(await complete()).toEqual({ kind: "done" });

    const [url, init] = mocks.fetch.mock.calls[0] ?? [];
    expect(url).toBe("http://backend.test/api/v1/auth/account-email-change/complete");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(init.headers).toEqual({
      "x-forwarded-for": "203.0.113.7",
      "x-forwarded-proto": "https",
      "Content-Type": "application/json",
    });
    expect(JSON.parse(init.body)).toEqual({ currentEmail: CURRENT, newEmail: NEW, code: CODE });
    expect(mocks.cookies).not.toHaveBeenCalled();
  });

  it("checks the fields before anything is sent, and echoes the addresses as typed", async () => {
    expect(await complete(form({ currentEmail: "", newEmail: " ANNA@exempel.se", code: "12a" }))).toEqual({
      kind: "invalid",
      errors: { currentEmail: "required", code: "malformed" },
      values: { currentEmail: "", newEmail: " ANNA@exempel.se" },
    });
    expect(await complete(form({ ...TYPED, newEmail: "ANNA@EXEMPEL.SE", code: CODE }))).toEqual({
      kind: "invalid",
      errors: { newEmail: "same" },
      values: { ...TYPED, newEmail: "ANNA@EXEMPEL.SE" },
    });
    expect(mocks.fetch).not.toHaveBeenCalled();
  });

  it("answers every refusal the same, keeping the addresses and never the code", async () => {
    mocks.fetch.mockResolvedValue(problem(410, "Auth.AccountEmailChangeUnusable"));

    const state = await complete();

    expect(state).toEqual({ kind: "refused", values: TYPED });
    expect(JSON.stringify(state)).not.toContain(CODE);
  });

  it("says not yet, with the earliest instant, when the match came before the delay had run", async () => {
    mocks.fetch.mockResolvedValue(
      problem(409, "Auth.AccountEmailChangeNotYet", { completableFrom: "2026-10-08T12:00:00+00:00" }),
    );

    expect(await complete()).toEqual({
      kind: "notYet",
      completableFrom: "2026-10-08T12:00:00+00:00",
      values: TYPED,
    });
  });

  it.each([
    [429, { kind: "tooManyAttempts", values: TYPED }],
    [503, { kind: "unavailable", values: TYPED }],
  ] as const)("keeps a %i a status the form can retry from, whatever its body", async (status, expected) => {
    mocks.fetch.mockResolvedValue(json(status, { error: "unavailable" }));

    expect(await complete()).toEqual(expected);
  });

  it.each([
    ["a 500, which the backend answers for anything after the swap", () => json(500, {})],
    ["a 200 instead of the 204", () => json(200, {})],
    ["a 400", () => json(400, { errors: { Code: ["x"] } })],
    ["a 404", () => json(404, {})],
    ["a 410 nobody named", () => problem(410, "Auth.SomethingElse")],
    ["a 409 that is not the not-yet", () => problem(409, "Auth.SomethingElse")],
    ["a not-yet without a readable instant", () => problem(409, "Auth.AccountEmailChangeNotYet", { completableFrom: "later" })],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing about %s: the address may have changed", async (_label, answer) => {
    mocks.fetch.mockImplementation(answer);

    expect(await complete()).toEqual({ kind: "unknown", values: TYPED });
  });
});
