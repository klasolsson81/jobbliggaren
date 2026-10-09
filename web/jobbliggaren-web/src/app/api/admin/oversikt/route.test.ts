import { beforeEach, describe, expect, it, vi } from "vitest";
import type { NextRequest } from "next/server";
import { overviewSnapshotFixture } from "@/test/fixtures/admin-overview";
const { load } = vi.hoisted(() => ({ load: vi.fn() }));
vi.mock("@/lib/api/admin-overview", () => ({ loadAdminOverview: load }));
import { GET } from "./route";

beforeEach(() => load.mockReset());

describe("GET /api/admin/oversikt", () => {
  it("propagates request cancellation and returns only the loader's projected observations privately", async () => {
    const data = overviewSnapshotFixture();
    load.mockResolvedValueOnce({ kind: "ok", data });
    const request = new Request("https://localhost/api/admin/oversikt") as NextRequest;
    const response = await GET(request);
    expect(load).toHaveBeenCalledWith(request.signal);
    expect(response.headers.get("cache-control")).toBe("private, no-store");
    expect(await response.json()).toEqual(data);
  });
  it.each([["unauthorized", 401], ["forbidden", 403]])("returns private %s without observations", async (kind, status) => {
    load.mockResolvedValueOnce({ kind });
    const response = await GET(new Request("https://localhost/api/admin/oversikt") as NextRequest);
    expect(response.status).toBe(status);
    expect(response.headers.get("cache-control")).toBe("private, no-store");
    expect(await response.json()).toEqual({ error: kind });
  });
});