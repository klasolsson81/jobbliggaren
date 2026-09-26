import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://test-backend" } }));

import { getExternalLoginProviders } from "./oauth-providers";

function backendAnswers(status: number, body: string) {
  const fetchMock = vi.fn<typeof fetch>(async () => new Response(body, { status }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe("getExternalLoginProviders", () => {
  it("reads the api's list, cached for the five minutes the api allows", async () => {
    const fetchMock = backendAnswers(200, '["google"]');

    expect(await getExternalLoginProviders()).toEqual(["google"]);
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("http://test-backend/api/v1/auth/oauth/providers");
    expect(init?.next).toEqual({ revalidate: 300 });
  });

  it("drops a key this build cannot start", async () => {
    backendAnswers(200, '["linkedin","google","myspace"]');

    expect(await getExternalLoginProviders()).toEqual(["google"]);
  });

  it.each([
    ["an empty list", 200, "[]"],
    ["a 404 from an api without the route", 404, ""],
    ["an unavailable api", 503, ""],
    ["a body that is not a list", 200, '{"google":true}'],
    ["a body that is not json", 200, "<html>"],
  ])("offers nothing on %s", async (_, status, body) => {
    backendAnswers(status, body);

    expect(await getExternalLoginProviders()).toEqual([]);
  });

  it("offers nothing when the api cannot be reached", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Promise.reject(new TypeError("fetch failed"))));

    expect(await getExternalLoginProviders()).toEqual([]);
  });
});
