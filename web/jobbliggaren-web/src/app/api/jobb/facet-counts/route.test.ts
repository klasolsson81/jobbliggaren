import { beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

const { getFacetCounts } = vi.hoisted(() => ({ getFacetCounts: vi.fn() }));
vi.mock("@/lib/api/job-ads", () => ({ getFacetCounts }));
import { GET } from "./route";

function request(query: string) {
  return new NextRequest(`http://localhost/api/jobb/facet-counts?${query}`);
}

beforeEach(() => {
  getFacetCounts.mockReset().mockResolvedValue({ kind: "ok", data: {} });
});

describe("GET /api/jobb/facet-counts — the occupation-name axis (#2199)", () => {
  // The hook appends one key per name; the route must hand every one of them on,
  // or the counts beside the other dimensions ignore a name pick.
  it("forwards every repeated occupationName", async () => {
    const response = await GET(
      request(
        "dimension=Municipality&occupationName=z5AM_ayf_WcL&occupationName=1jm3_XFQ_TdJ",
      ),
    );

    expect(response.status).toBe(200);
    expect(getFacetCounts).toHaveBeenCalledTimes(1);
    expect(getFacetCounts).toHaveBeenCalledWith(
      "Municipality",
      expect.objectContaining({
        occupationName: ["z5AM_ayf_WcL", "1jm3_XFQ_TdJ"],
      }),
    );
  });

  it("accepts the OccupationName dimension itself", async () => {
    await GET(request("dimension=OccupationName&occupationGroup=DJh5_yyF_hEM"));

    expect(getFacetCounts).toHaveBeenCalledWith(
      "OccupationName",
      expect.objectContaining({
        occupationName: [],
        occupationGroup: ["DJh5_yyF_hEM"],
      }),
    );
  });
});
