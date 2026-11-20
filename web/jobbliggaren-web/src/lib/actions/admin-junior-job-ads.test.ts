import { beforeEach, describe, expect, it, vi } from "vitest";

// #2264 — the junior queue's decision. What it pins: the input and the Admin role are checked before anything
// reaches the backend; the wire body for each decision and for the hand-back; a success revalidates the page; a
// documented refusal changed nothing and revalidates nothing; and a 5xx or a lost response claims nothing.
// The translator returns "namespace.key", so assertions check the resolved key.

const { getSessionIdMock, getServerSessionMock, authedFetchMock, revalidatePathMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
  getServerSessionMock: vi.fn(),
  authedFetchMock: vi.fn(),
  revalidatePathMock: vi.fn(),
}));

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string, values?: Record<string, unknown>) =>
    values === undefined ? `${namespace}.${key}` : `${namespace}.${key} ${JSON.stringify(values)}`,
}));
vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));
vi.mock("@/lib/auth/session", () => ({
  ROLES: { Admin: "Admin" },
  getSessionId: getSessionIdMock,
  getServerSession: getServerSessionMock,
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));

import { decideJuniorJobAdAction } from "./admin-junior-job-ads";
import type { AdminJuniorChoice } from "@/lib/admin/junior-jobs";

const ID = "00000000-0000-4000-8000-000000000901";
const DECISION_PATH = `/api/v1/admin/junior-job-ads/${ID}/decision`;
const ROUTE = "/admin/juniorjobb";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers });
const problem = (status: number, title: string) =>
  json(status, { type: "about:blank", title, status, detail: "backend text" });
const noContent = () => new Response(null, { status: 204 });
const refused = (error: string) => ({ success: false, error });

beforeEach(() => {
  vi.clearAllMocks();
  getSessionIdMock.mockResolvedValue("session-under-test");
  getServerSessionMock.mockResolvedValue({ userId: "admin-id", email: "admin@example.test", roles: ["Admin"] });
});

describe("decideJuniorJobAdAction", () => {
  const decide = (decision: AdminJuniorChoice = "junior") => decideJuniorJobAdAction(ID, decision);

  it.each([
    ["junior", "Junior"],
    ["notJunior", "NotJunior"],
    [null, null],
  ] as const)("posts %j as %j, answers success and revalidates the page", async (decision, wire) => {
    authedFetchMock.mockResolvedValue(noContent());

    expect(await decide(decision)).toEqual({ success: true });
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", DECISION_PATH, {
      method: "POST",
      body: JSON.stringify({ decision: wire }),
    });
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it("refuses an id that is no GUID, and a decision it does not know, before anything is sent", async () => {
    expect(await decideJuniorJobAdAction("../decision", "junior")).toEqual(refused("admin.juniorJobs.errors.gone"));
    // Declared unreachable for a typed caller: a direct Server Action POST can send any value.
    expect(await decideJuniorJobAdAction(ID, "Unclear" as unknown as AdminJuniorChoice)).toEqual(
      refused("admin.juniorJobs.errors.refused"),
    );
    expect(await decideJuniorJobAdAction(ID, undefined as unknown as AdminJuniorChoice)).toEqual(
      refused("admin.juniorJobs.errors.refused"),
    );
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["no session", null, "admin.juniorJobs.errors.unauthorized"],
    ["a session without the Admin role", { userId: "u", email: "medlem@example.test", roles: [] }, "admin.juniorJobs.errors.forbidden"],
  ])("sends nothing for %s", async (_label, session, refusal) => {
    getServerSessionMock.mockResolvedValue(session);

    expect(await decide()).toEqual(refused(refusal));
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("sends nothing when the session id is gone after the role was read", async () => {
    getSessionIdMock.mockResolvedValue(null);

    expect(await decide()).toEqual(refused("admin.juniorJobs.errors.unauthorized"));
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["an ad that is no longer listed or decidable", () => problem(404, "JobAd.NotFound"), "admin.juniorJobs.errors.gone"],
    ["a decision the backend refuses", () => json(400, { errors: { Decision: ["Ogiltigt beslut."] } }), "admin.juniorJobs.errors.refused"],
    ["a session that ended on the way", () => json(401, {}), "admin.juniorJobs.errors.unauthorized"],
    ["a session without the role by now", () => json(403, {}), "admin.juniorJobs.errors.forbidden"],
    ["a rate limit", () => json(429, {}, { "Retry-After": "7" }), 'admin.juniorJobs.errors.rateLimited {"seconds":7}'],
  ])("answers %s as a refusal that changed nothing and revalidates nothing", async (_label, answer, refusal) => {
    authedFetchMock.mockImplementation(async () => answer());

    expect(await decide()).toEqual(refused(refusal));
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a 500", () => json(500, {})],
    ["a 404 nobody named", () => json(404, {})],
    ["a 404 naming something else", () => problem(404, "Feedback.NotFound")],
    ["a 409", () => problem(409, "JobAd.Conflict")],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing about %s: the decision may have been saved", async (_label, answer) => {
    authedFetchMock.mockImplementation(async () => answer());

    expect(await decide()).toEqual(refused("admin.juniorJobs.errors.unknown"));
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});
