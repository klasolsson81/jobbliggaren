import { beforeEach, describe, expect, it, vi } from "vitest";

// #1979 — the admin feedback reads. What it pins: the list's query string, an id that is no GUID never
// becoming a path segment, a missing submission as `notFound`, and no read without a session.

const { getSessionIdMock, authedFetchMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn(async () => "session-under-test" as string | null),
  authedFetchMock: vi.fn(),
}));

vi.mock("@/lib/auth/session", () => ({ getSessionId: getSessionIdMock }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));

import { feedbackListQuery, getFeedbackDetail, getFeedbackSummary, listFeedback } from "./admin-feedback";

const ID = "00000000-0000-4000-8000-000000000501";

beforeEach(() => {
  vi.clearAllMocks();
  getSessionIdMock.mockResolvedValue("session-under-test");
});

describe("the admin feedback reads (#1979)", () => {
  it("leaves a missing status and page out of the list's query, so the backend filters on neither", () => {
    expect(feedbackListQuery({ pageNumber: 1, pageSize: 25 })).toBe("pageNumber=1&pageSize=25");
    expect(feedbackListQuery({ status: "Declined", page: "cv-review", pageNumber: 2, pageSize: 25 })).toBe(
      "status=Declined&page=cv-review&pageNumber=2&pageSize=25",
    );
  });

  it("reads the list and the summary on their own paths", async () => {
    authedFetchMock.mockResolvedValue(new Response("{}", { status: 500 }));

    await listFeedback({ status: "New", pageNumber: 1, pageSize: 25 });
    await getFeedbackSummary(90);

    expect(authedFetchMock.mock.calls.map((call) => call[1])).toEqual([
      "/api/v1/admin/feedback?status=New&pageNumber=1&pageSize=25",
      "/api/v1/admin/feedback/summary?days=90",
    ]);
  });

  it("answers an id that is no GUID as no submission, before anything is sent", async () => {
    expect(await getFeedbackDetail("../summary")).toEqual({ kind: "notFound" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("answers a submission the backend does not have as notFound", async () => {
    authedFetchMock.mockResolvedValue(new Response(null, { status: 404 }));

    expect(await getFeedbackDetail(ID)).toEqual({ kind: "notFound" });
    expect(authedFetchMock).toHaveBeenCalledWith("session-under-test", `/api/v1/admin/feedback/${ID}`);
  });

  it("reads nothing without a session", async () => {
    getSessionIdMock.mockResolvedValue(null);

    expect(await listFeedback({ pageNumber: 1, pageSize: 25 })).toEqual({ kind: "unauthorized" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});
