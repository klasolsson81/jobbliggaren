import { afterEach, describe, expect, it, vi } from "vitest";
import { resolveOccupationSearch } from "./resolve-occupation-search";

const BODY = {
  rows: [
    {
      kind: "Group",
      groupConceptId: "UxT1_tPF_Kbg",
      groupLabel: "Övriga IT-specialister",
      matchedNames: [{ occupationNameConceptId: "iwJU_gW4_SxT", nameLabel: "UX-designer" }],
      labelHit: false,
      headWord: null,
      variants: [],
      exactMatch: false,
    },
  ],
  totalRows: 1,
};

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
});

describe("resolveOccupationSearch (#2198)", () => {
  it("reads the BFF route uncached, with only q, under the caller's signal", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(BODY));
    global.fetch = fetchMock;
    const controller = new AbortController();

    const outcome = await resolveOccupationSearch("ux", controller.signal);

    expect(outcome).toEqual({ kind: "ok", result: BODY });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/me/occupation-search?q=ux");
    expect(init).toEqual({ signal: controller.signal, cache: "no-store" });
  });

  it("maps 401 to unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await resolveOccupationSearch("chef", new AbortController().signal)).toEqual({
      kind: "unauthorized",
    });
  });

  it.each([429, 502, 500, 403])("maps %i to unavailable", async (status) => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status }));
    expect(await resolveOccupationSearch("chef", new AbortController().signal)).toEqual({
      kind: "unavailable",
    });
  });

  it("maps a network failure and a body that does not parse to unavailable", async () => {
    global.fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
    expect(await resolveOccupationSearch("chef", new AbortController().signal)).toEqual({
      kind: "unavailable",
    });

    global.fetch = vi.fn().mockResolvedValue(Response.json({ rows: [{ kind: "Name" }], totalRows: 1 }));
    expect(await resolveOccupationSearch("chef", new AbortController().signal)).toEqual({
      kind: "unavailable",
    });

    global.fetch = vi.fn().mockResolvedValue(new Response("not json", { status: 200 }));
    expect(await resolveOccupationSearch("chef", new AbortController().signal)).toEqual({
      kind: "unavailable",
    });
  });

  it("lets an abort through, so the caller can tell a superseded word from a failed search", async () => {
    const controller = new AbortController();
    const abort = new DOMException("The operation was aborted.", "AbortError");
    global.fetch = vi.fn().mockImplementation(() => {
      controller.abort();
      return Promise.reject(abort);
    });

    await expect(resolveOccupationSearch("chef", controller.signal)).rejects.toBe(abort);
  });
});
