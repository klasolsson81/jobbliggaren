import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { getSessionId, authedFetch } = vi.hoisted(() => ({ getSessionId: vi.fn(), authedFetch: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch }));
import { POST, GET, HEAD } from "./route";
import { rgbaPng } from "../../../../../../tests/admin/png-encoder";

const ID = "00000000-0000-4000-8000-000000000501";
const PNG = rgbaPng(2, 3);

function request(body: unknown = { id: ID }, headers: Record<string, string> = {}) {
  return new Request("http://localhost/api/admin/feedback/screenshot", {
    method: "POST", headers: { host: "localhost", origin: "http://localhost", "content-type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
}

async function expectRefusal(response: Response, status: number) {
  expect(response.status).toBe(status);
  expect(response.headers.get("cache-control")).toBe("no-store");
  expect(response.headers.get("x-content-type-options")).toBe("nosniff");
  expect(response.headers.get("content-type")).toContain("application/json");
  expect(await response.json()).toHaveProperty("error");
}

beforeEach(() => { getSessionId.mockReset().mockResolvedValue("session"); authedFetch.mockReset(); });
afterEach(() => vi.restoreAllMocks());

describe("the private feedback screenshot BFF", () => {
  it("returns PNG with private-read headers and forwards cancellation", async () => {
    authedFetch.mockResolvedValue(new Response(PNG, { headers: { "content-type": "image/png" } }));
    const incoming = request();
    const response = await POST(incoming);
    expect(response.status).toBe(200);
    expect(response.headers.get("content-type")).toBe("image/png");
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(response.headers.get("x-content-type-options")).toBe("nosniff");
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(PNG);
    expect(authedFetch).toHaveBeenCalledWith("session", `/api/v1/admin/feedback/${ID}/screenshot`, { signal: incoming.signal });
  });

  it.each([401, 403, 404, 429, 500])("keeps backend refusal %i as JSON", async (status) => {
    authedFetch.mockResolvedValue(new Response("private backend detail", { status }));
    const response = await POST(request());
    await expectRefusal(response, status === 500 ? 502 : status);
  });

  it("refuses a foreign origin before reading a session or contacting the backend", async () => {
    await expectRefusal(await POST(request({ id: ID }, { origin: "https://foreign.invalid" })), 403);
    expect(getSessionId).not.toHaveBeenCalled();
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("requires the existing session", async () => {
    getSessionId.mockResolvedValue(null);
    await expectRefusal(await POST(request()), 401);
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it.each([{ id: "../cv" }, { id: 42 }, {}])("validates the id before forwarding", async (body) => {
    await expectRefusal(await POST(request(body)), 400);
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("bounds the JSON body", async () => {
    await expectRefusal(await POST(request({ id: ID, extra: "x".repeat(1024) })), 413);
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("rejects a successful response carrying HTML", async () => {
    authedFetch.mockResolvedValue(new Response("<script>unsafe</script>", { headers: { "content-type": "text/html" } }));
    await expectRefusal(await POST(request()), 502);
  });

  it("rejects bytes without the PNG signature even under a PNG content type", async () => {
    authedFetch.mockResolvedValue(new Response("unsafe", { headers: { "content-type": "image/png" } }));
    await expectRefusal(await POST(request()), 502);
  });

  it("bounds a chunked upstream and cancels it when it exceeds 5 MiB", async () => {
    const cancelled = vi.fn();
    const stream = new ReadableStream<Uint8Array>({
      start(controller) { controller.enqueue(new Uint8Array(5 * 1024 * 1024)); controller.enqueue(new Uint8Array(1)); },
      cancel: cancelled,
    });
    authedFetch.mockResolvedValue(new Response(stream, { headers: { "content-type": "image/png" } }));
    await expectRefusal(await POST(request()), 502);
    expect(cancelled).toHaveBeenCalled();
  });

  it("returns bounded JSON on a transport failure", async () => {
    authedFetch.mockRejectedValue(new Error("upstream failure with private context"));
    await expectRefusal(await POST(request()), 502);
  });

  it("keeps refusal headers when the method is not POST", async () => {
    await expectRefusal(GET(), 405);
    const response = HEAD();
    expect(response.status).toBe(405);
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(response.headers.get("x-content-type-options")).toBe("nosniff");
    expect(response.headers.get("allow")).toBe("POST");
  });
});
