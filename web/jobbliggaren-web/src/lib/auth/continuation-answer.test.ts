import { describe, expect, it } from "vitest";
import { NextRequest } from "next/server";
import { continuationAnswer, hopTarget } from "./continuation-answer";

const request = (cookie?: string) =>
  new NextRequest("https://app.test/matchningar", { headers: cookie ? { cookie } : {} });

describe("continuationAnswer", () => {
  it("answers 200 with the document, refreshing to the target with the marker", async () => {
    const res = continuationAnswer(request(), "/matchningar");

    expect(res?.status).toBe(200);
    expect(res?.headers.get("cache-control")).toBe("no-store");
    expect(res?.headers.get("cross-origin-opener-policy")).toBe("same-origin");
    expect(res?.headers.get("set-cookie")).toBeNull();
    expect(await res?.text()).toContain('<meta http-equiv="refresh" content="0;url=/matchningar?vidare=1">');
  });

  it("answers in English when the locale cookie says so", async () => {
    expect(await continuationAnswer(request("NEXT_LOCALE=en"), "/matchningar")?.text()).toContain('<html lang="en">');
  });

  it.each([
    "//evil.example",
    "https://evil.example",
    "https://evil.example/matchningar",
    "/\\evil.example",
    "matchningar",
    `/jobb/${"a".repeat(2050)}`,
  ])("refuses %j, which is no path on this site: no document, so no refresh to another host", (target) => {
    expect(continuationAnswer(request(), target)).toBeNull();
  });

  it("keeps the target's own query and adds the marker last", () => {
    expect(hopTarget("/admin/feedback?id=3f2504e0-4f89-41d3-9a0c-0305e82c3301")).toBe(
      "/admin/feedback?id=3f2504e0-4f89-41d3-9a0c-0305e82c3301&vidare=1"
    );
  });
});
