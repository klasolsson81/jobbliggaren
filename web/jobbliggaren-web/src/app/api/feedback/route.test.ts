// @vitest-environment node
// The route runs on Node; its multipart parsing and FormData forwarding are undici's, not jsdom's.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { rgbaPng } from "../../../../tests/admin/png-encoder";

const { getSessionId, appVersion } = vi.hoisted(() => ({ getSessionId: vi.fn(), appVersion: { value: null as string | null } }));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));
vi.mock("@/lib/env", () => ({
  env: {
    BACKEND_URL: "http://test-backend",
    get APP_VERSION() {
      return appVersion.value;
    },
  },
}));

import { FEEDBACK_SUBMIT_ERRORS } from "@/lib/dto/feedback";
import { GET, POST } from "./route";

const SUBMISSION = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
const PNG = rgbaPng(2, 3);
const SHA = "f6d917bd51a61729a4433bb34cde5180d47615f2";

const fetchMock = vi.fn();

function form(parts: Array<[string, string | Blob, string?]>): FormData {
  const data = new FormData();
  for (const [name, value, filename] of parts) {
    if (typeof value === "string") data.append(name, value);
    else data.append(name, value, filename ?? "upload");
  }
  return data;
}

function payload(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({ submissionKey: SUBMISSION, page: "jobs", rating: 4, comment: "Filtren är bra.", ...overrides });
}

function request(body: BodyInit, headers: Record<string, string> = {}): Request {
  return new Request("http://localhost/api/feedback", {
    method: "POST",
    headers: { host: "localhost", origin: "http://localhost", ...headers },
    body,
  });
}

const send = (parts: Array<[string, string | Blob, string?]>, headers?: Record<string, string>) =>
  POST(request(form(parts), headers));

const backend = (status: number, body?: unknown, headers: Record<string, string> = {}) =>
  new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });

/** The multipart body the route posted on, as the backend would read it. */
function forwarded(): { payload: Record<string, unknown>; screenshot: File | null; init: RequestInit } {
  expect(fetchMock).toHaveBeenCalledTimes(1);
  const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
  expect(url).toBe("http://test-backend/api/v1/me/feedback");
  const body = init.body as FormData;
  const screenshot = body.get("screenshot");
  return {
    payload: JSON.parse(body.get("payload") as string) as Record<string, unknown>,
    screenshot: screenshot === null || typeof screenshot === "string" ? null : screenshot,
    init,
  };
}

beforeEach(() => {
  getSessionId.mockReset().mockResolvedValue("session");
  appVersion.value = null;
  fetchMock.mockReset().mockResolvedValue(backend(201, { id: "11111111-1111-4111-8111-111111111111", replayed: false }));
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => vi.unstubAllGlobals());

describe("the feedback submission BFF — what it accepts", () => {
  it("refuses a foreign origin before reading a session", async () => {
    const response = await send([["payload", payload()]], { origin: "https://foreign.invalid" });
    expect(response.status).toBe(403);
    expect(getSessionId).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers signed out without a session", async () => {
    getSessionId.mockResolvedValue(null);
    const response = await send([["payload", payload()]]);
    expect(response.status).toBe(401);
    expect(await response.json()).toEqual({ outcome: "signedOut" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a body that is not multipart", async () => {
    const response = await POST(request(payload(), { "content-type": "application/json" }));
    expect(response.status).toBe(415);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a declared length over the cap before reading", async () => {
    const response = await POST(request(form([["payload", payload()]]), { "content-length": String(6 * 1024 * 1024) }));
    expect(response.status).toBe(413);
    expect(await response.json()).toEqual({ outcome: "tooLarge" });
  });

  it("refuses an undeclared body once it passes the cap", async () => {
    const chunk = new Uint8Array(1024 * 1024);
    let sent = 0;
    const stream = new ReadableStream<Uint8Array>({
      pull(controller) {
        if (sent++ < 7) controller.enqueue(chunk);
        else controller.close();
      },
    });
    const response = await POST(
      new Request("http://localhost/api/feedback", {
        method: "POST",
        headers: { host: "localhost", origin: "http://localhost", "content-type": "multipart/form-data; boundary=x" },
        body: stream,
        duplex: "half",
      } as RequestInit & { duplex: "half" }),
    );
    expect(response.status).toBe(413);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a request with no body as invalid", async () => {
    const response = await POST(
      new Request("http://localhost/api/feedback", {
        method: "POST",
        headers: { host: "localhost", origin: "http://localhost", "content-type": "multipart/form-data; boundary=x" },
      }),
    );
    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ outcome: "refused", reason: "invalid" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["no payload", [] as Array<[string, string | Blob]>],
    ["two payloads", [["payload", payload()], ["payload", payload()]] as Array<[string, string | Blob]>],
    ["an unknown part", [["payload", payload()], ["note", "x"]] as Array<[string, string | Blob]>],
    ["a screenshot sent as text", [["payload", payload()], ["screenshot", "not a file"]] as Array<[string, string | Blob]>],
    ["two screenshots", [["payload", payload()], ["screenshot", new Blob([PNG])], ["screenshot", new Blob([PNG])]] as Array<[string, string | Blob]>],
  ])("refuses %s", async (_, parts) => {
    const response = await send(parts);
    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ outcome: "refused", reason: "invalid" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a payload that is not JSON", "{"],
    ["an app version the browser set itself", payload({ appVersion: SHA })],
    ["a page outside the closed set", payload({ page: "admin" })],
    ["a fractional rating", payload({ rating: 4.5 })],
    ["a rating out of range", payload({ rating: 6 })],
    ["a comment over 2 000 characters", payload({ comment: "x".repeat(2001) })],
    ["a key that is not a UUID", payload({ submissionKey: "x" })],
  ])("refuses %s", async (_, body) => {
    const response = await send([["payload", body]]);
    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ outcome: "refused", reason: "invalid" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["neither a rating nor a comment", [["payload", payload({ rating: undefined, comment: "   " })]]],
    ["an image alone", [["payload", payload({ rating: undefined, comment: undefined })], ["screenshot", new Blob([PNG])]]],
  ] as Array<[string, Array<[string, string | Blob]>]>)("answers empty for %s", async (_, parts) => {
    const response = await send(parts);
    expect(await response.json()).toEqual({ outcome: "refused", reason: "empty" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a file that is not an image", new Blob(["%PDF-1.4"])],
    ["an empty file", new Blob([])],
  ])("refuses %s as a screenshot", async (_, file) => {
    const response = await send([["payload", payload()], ["screenshot", file]]);
    expect(await response.json()).toEqual({ outcome: "refused", reason: "screenshot" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("the feedback submission BFF — what it posts on", () => {
  it("stamps the server's app version and trims the comment", async () => {
    appVersion.value = SHA;
    expect(await (await send([["payload", payload({ comment: "  Filtren är bra.  " })]])).json()).toEqual({ outcome: "saved" });
    expect(forwarded().payload).toEqual({ submissionKey: SUBMISSION, page: "jobs", rating: 4, comment: "Filtren är bra.", appVersion: SHA });
  });

  it("leaves the version out when the image carries none", async () => {
    await send([["payload", payload()]]);
    expect(forwarded().payload).not.toHaveProperty("appVersion");
  });

  it("drops a blank comment from a rated submission", async () => {
    await send([["payload", payload({ comment: "  " })]]);
    expect(forwarded().payload).not.toHaveProperty("comment");
  });

  describe("the opted-in device context (consent evidence, CTO M1)", () => {
    const client = { viewportWidth: 1280, viewportHeight: 720, pixelRatio: 1.5, deviceClass: "desktop", osFamily: "windows", browserFamily: "edge" };

    it("goes on when the page was rendered by the version now answering", async () => {
      appVersion.value = SHA;
      await send([["payload", payload({ client, renderedVersion: SHA })]]);
      const sent = forwarded().payload;
      expect(sent.client).toEqual(client);
      expect(sent.appVersion).toBe(SHA);
      expect(sent).not.toHaveProperty("renderedVersion");
    });

    it("is left out when the page came from another build, and the rest still goes", async () => {
      appVersion.value = SHA;
      expect(await (await send([["payload", payload({ client, renderedVersion: "0b163d1b48fafe74a60ea497392142a461aa449f" })]])).json())
        .toEqual({ outcome: "saved" });
      const sent = forwarded().payload;
      expect(sent).not.toHaveProperty("client");
      expect(sent).toMatchObject({ rating: 4, comment: "Filtren är bra.", appVersion: SHA });
    });

    it("is left out when the image carries no version", async () => {
      await send([["payload", payload({ client, renderedVersion: SHA })]]);
      expect(forwarded().payload).not.toHaveProperty("client");
    });

    it("is left out when the page did not say which version it was", async () => {
      appVersion.value = SHA;
      await send([["payload", payload({ client })]]);
      expect(forwarded().payload).not.toHaveProperty("client");
    });
  });

  it("sends the screenshot as a named file part with its bytes, and the session as Bearer", async () => {
    await send([["payload", payload()], ["screenshot", new Blob([PNG], { type: "image/png" }), "skärmbild från klistra in.png"]]);
    const { screenshot, init } = forwarded();
    expect(screenshot).not.toBeNull();
    expect(screenshot!.name).toBe("screenshot");
    expect(new Uint8Array(await screenshot!.arrayBuffer())).toEqual(PNG);
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer session");
    expect(init.cache).toBe("no-store");
  });
});

describe("the feedback submission BFF — what the browser hears back", () => {
  it.each([
    ["a new submission", backend(201, { id: "11111111-1111-4111-8111-111111111111", replayed: false }), 200, { outcome: "saved" }],
    ["a replay", backend(200, { id: "11111111-1111-4111-8111-111111111111", replayed: true }), 200, { outcome: "saved" }],
    [FEEDBACK_SUBMIT_ERRORS.empty, backend(400, { title: FEEDBACK_SUBMIT_ERRORS.empty, detail: "private" }), 400, { outcome: "refused", reason: "empty" }],
    [FEEDBACK_SUBMIT_ERRORS.screenshotInvalid, backend(400, { title: FEEDBACK_SUBMIT_ERRORS.screenshotInvalid }), 400, { outcome: "refused", reason: "screenshot" }],
    ["another 400", backend(400, { title: "Feedback.AppVersionInvalid" }), 400, { outcome: "refused", reason: "invalid" }],
    ["a validator 400", backend(400, { errors: { PageKey: ["x"] } }), 400, { outcome: "refused", reason: "invalid" }],
    ["a lapsed session", backend(401), 401, { outcome: "signedOut" }],
    ["the closed gate", backend(404, { title: "Feedback.Closed" }), 404, { outcome: "closed" }],
    ["a busy decoder", backend(409, { title: "Feedback.ScreenshotBusy" }), 409, { outcome: "busy" }],
    ["a body over the backend's cap", backend(413), 413, { outcome: "tooLarge" }],
    ["the global limiter", backend(429), 409, { outcome: "busy" }],
    ["a server error", backend(500, { title: "x", detail: "stack trace" }), 502, { outcome: "unknown" }],
  ] as Array<[string, Response, number, unknown]>)("maps %s", async (_, answer, status, body) => {
    fetchMock.mockResolvedValue(answer);
    const response = await send([["payload", payload()]]);
    expect(response.status).toBe(status);
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(await response.json()).toEqual(body);
  });

  it("passes the per-user window's wait on", async () => {
    fetchMock.mockResolvedValue(backend(429, undefined, { "Retry-After": "240" }));
    const response = await send([["payload", payload()]]);
    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("240");
    expect(await response.json()).toEqual({ outcome: "rateLimited", retryAfterSeconds: 240 });
  });

  it("answers unknown when the backend cannot be reached", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    const response = await send([["payload", payload()]]);
    expect(response.status).toBe(502);
    expect(await response.json()).toEqual({ outcome: "unknown" });
  });

  it("never echoes the backend's body", async () => {
    fetchMock.mockResolvedValue(backend(400, { title: "Feedback.RatingOutOfRange", detail: "private backend detail" }));
    expect(await (await send([["payload", payload()]])).text()).not.toContain("private");
  });

  it("answers busy while four uploads are already in flight", async () => {
    let session = 0;
    getSessionId.mockImplementation(async () => `session-${++session}`);
    const pending: Array<(value: Response) => void> = [];
    fetchMock.mockImplementation(() => new Promise<Response>((resolve) => pending.push(resolve)));
    const first = Array.from({ length: 4 }, () => send([["payload", payload()]]));
    await vi.waitFor(() => expect(pending).toHaveLength(4));
    const fifth = await send([["payload", payload()]]);
    expect(fifth.status).toBe(409);
    expect(await fifth.json()).toEqual({ outcome: "busy" });
    for (const resolve of pending) resolve(backend(201, { id: "11111111-1111-4111-8111-111111111111", replayed: false }));
    expect((await Promise.all(first)).map((response) => response.status)).toEqual([200, 200, 200, 200]);
  });

  it("answers busy to a second upload from the same session, while another session still gets a slot", async () => {
    const pending: Array<(value: Response) => void> = [];
    fetchMock.mockImplementation(() => new Promise<Response>((resolve) => pending.push(resolve)));
    const first = send([["payload", payload()]]);
    await vi.waitFor(() => expect(pending).toHaveLength(1));

    const second = await send([["payload", payload()]]);
    expect(second.status).toBe(409);
    expect(await second.json()).toEqual({ outcome: "busy" });

    getSessionId.mockResolvedValue("another session");
    const other = send([["payload", payload()]]);
    await vi.waitFor(() => expect(pending).toHaveLength(2));
    for (const resolve of pending) resolve(backend(201, { id: "11111111-1111-4111-8111-111111111111", replayed: false }));
    expect((await Promise.all([first, other])).map((response) => response.status)).toEqual([200, 200]);
  });

  it("answers 405 to anything but POST", () => {
    const response = GET();
    expect(response.status).toBe(405);
    expect(response.headers.get("allow")).toBe("POST");
  });
});
