import { beforeEach, describe, expect, it, vi } from "vitest";

// #1979 — the feedback page's two commands. What it pins: the input and the Admin role are checked before
// anything reaches the backend; the wire bodies; a success and a documented refusal each answer as such;
// a 5xx or a lost response claims nothing; only the status the submission already has refuses the value
// chosen; and the page is revalidated when what it shows has moved on.
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

import { changeFeedbackStatusAction, requeueFeedbackNotificationAction } from "./admin-feedback";
import type { AdminFeedbackStatus } from "@/lib/admin/view-models";

const ID = "00000000-0000-4000-8000-000000000501";
const STATUS_PATH = `/api/v1/admin/feedback/${ID}/status`;
const REQUEUE_PATH = `/api/v1/admin/feedback/${ID}/notification/requeue`;
const ROUTE = "/admin/feedback";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers });
const problem = (status: number, title: string) =>
  json(status, { type: "about:blank", title, status, detail: "backend text" });
const noContent = () => new Response(null, { status: 204 });
/** A refusal of the command: the field keeps its state. */
const command = (text: string) => ({ text, about: "command" });

beforeEach(() => {
  vi.clearAllMocks();
  getSessionIdMock.mockResolvedValue("session-under-test");
  getServerSessionMock.mockResolvedValue({ userId: "admin-id", email: "admin@example.test", roles: ["Admin"] });
});

describe("changeFeedbackStatusAction", () => {
  const change = (status: AdminFeedbackStatus = "inProgress") => changeFeedbackStatusAction(ID, status);

  it("posts the status by its backend name, answers null and revalidates the page", async () => {
    authedFetchMock.mockResolvedValue(noContent());

    expect(await change("declined")).toBeNull();
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", STATUS_PATH, {
      method: "POST",
      body: JSON.stringify({ status: "Declined" }),
    });
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it("refuses an id that is no GUID, and a status it does not know, before anything is sent", async () => {
    expect(await changeFeedbackStatusAction("../summary", "new")).toEqual(command("admin.feedback.errors.gone"));
    // Declared unreachable for a typed caller: a direct Server Action POST can send any value.
    expect(await changeFeedbackStatusAction(ID, "archived" as AdminFeedbackStatus)).toEqual(
      command("admin.feedback.errors.statusRefused"),
    );
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["no session", null, "admin.feedback.errors.unauthorized"],
    ["a session without the Admin role", { userId: "u", email: "medlem@example.test", roles: [] }, "admin.feedback.errors.forbidden"],
  ])("sends nothing for %s", async (_label, session, refusal) => {
    getServerSessionMock.mockResolvedValue(session);

    expect(await change()).toEqual(command(refusal));
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    [
      "the status the submission already has, as a refusal of the value chosen",
      () => problem(400, "Feedback.StatusUnchanged"),
      { text: "admin.feedback.detail.status.unchanged", about: "value" },
    ],
    [
      "a status the backend refuses",
      () => json(400, { errors: { Status: ["Okänd status."] } }),
      command("admin.feedback.errors.statusRefused"),
    ],
    ["a session that ended on the way", () => json(401, {}), command("admin.feedback.errors.unauthorized")],
    ["a session without the role by now", () => json(403, {}), command("admin.feedback.errors.forbidden")],
    ["a rate limit", () => json(429, {}, { "Retry-After": "7" }), command('admin.feedback.errors.rateLimited {"seconds":7}')],
  ])("answers %s as a refusal that changed nothing", async (_label, answer, refusal) => {
    authedFetchMock.mockImplementation(async () => answer());

    expect(await change()).toEqual(refusal);
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("answers a submission that is gone as such, and revalidates so the page shows it gone", async () => {
    authedFetchMock.mockResolvedValue(problem(404, "Feedback.NotFound"));

    expect(await change()).toEqual(command("admin.feedback.errors.gone"));
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it.each([
    ["a 500", () => json(500, {})],
    ["a 404 nobody named", () => json(404, {})],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
  ])("claims nothing about %s: the status may have been saved", async (_label, answer) => {
    authedFetchMock.mockImplementation(async () => answer());

    expect(await change()).toEqual(command("admin.feedback.errors.statusUnknown"));
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});

describe("requeueFeedbackNotificationAction", () => {
  it.each([true, false])("posts the acknowledgement (%s), answers null and revalidates the page", async (acknowledge) => {
    authedFetchMock.mockResolvedValue(noContent());

    expect(await requeueFeedbackNotificationAction(ID, acknowledge)).toBeNull();
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", REQUEUE_PATH, {
      method: "POST",
      body: JSON.stringify({ acknowledgeDuplicateRisk: acknowledge }),
    });
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it("refuses an id that is no GUID, an acknowledgement that is no boolean, and a caller who is no administrator", async () => {
    expect(await requeueFeedbackNotificationAction("../x", true)).toEqual(command("admin.feedback.errors.gone"));
    // Declared unreachable for a typed caller: a direct Server Action POST can send any value.
    expect(await requeueFeedbackNotificationAction(ID, "yes" as unknown as boolean)).toEqual(
      command("admin.feedback.errors.noticeRefused"),
    );
    getServerSessionMock.mockResolvedValue({ userId: "u", email: "medlem@example.test", roles: [] });
    expect(await requeueFeedbackNotificationAction(ID, true)).toEqual(command("admin.feedback.errors.forbidden"));
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a notice already queued or sent", "Feedback.NotificationNotRequeueable", "admin.feedback.errors.noticeAlreadyQueued"],
    ["an unknown outcome sent without the acknowledgement", "Feedback.DuplicateRiskNotAcknowledged", "admin.feedback.errors.noticeChanged"],
  ])("answers %s, and revalidates so the page shows where the notice is now", async (_label, title, refusal) => {
    authedFetchMock.mockResolvedValue(problem(409, title));

    expect(await requeueFeedbackNotificationAction(ID, false)).toEqual(command(refusal));
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it("answers a submission that is gone as such", async () => {
    authedFetchMock.mockResolvedValue(problem(404, "Feedback.NotFound"));

    expect(await requeueFeedbackNotificationAction(ID, true)).toEqual(command("admin.feedback.errors.gone"));
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });

  it.each([
    ["a 500", () => json(500, {})],
    ["a lost response", () => Promise.reject(new Error("socket hang up"))],
    ["a 410 nobody named", () => problem(410, "Other.Unavailable")],
  ])("claims nothing about %s: the notice may have been queued", async (_label, answer) => {
    authedFetchMock.mockImplementation(async () => answer());

    expect(await requeueFeedbackNotificationAction(ID, true)).toEqual(command("admin.feedback.errors.noticeUnknown"));
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("states a deleted reporter's refusal definitively and rereads current feedback", async () => {
    authedFetchMock.mockResolvedValue(problem(410, "Feedback.ReporterUnavailable"));

    expect(await requeueFeedbackNotificationAction(ID, true)).toEqual(command("admin.feedback.errors.noticeRefused"));
    expect(authedFetchMock).toHaveBeenCalledTimes(1);
    expect(revalidatePathMock).toHaveBeenCalledWith(ROUTE);
  });
});
