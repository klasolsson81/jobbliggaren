import { describe, expect, it } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import { publicJobAdTitle } from "./public-job-ad-title";
import { publicJobAdPath, publicJobAdUrl } from "./public-job-ad-url";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";

describe("publicJobAdPath / publicJobAdUrl", () => {
  it("is the path under /jobb, and the canonical is that path on the public origin with no query or fragment", () => {
    expect(publicJobAdPath(ID)).toBe(`/jobb/${ID}`);
    expect(publicJobAdUrl(ID)).toBe(`${SITE_URL}/jobb/${ID}`);
    expect(publicJobAdUrl(ID)).not.toMatch(/[?#]/);
  });
});

describe("publicJobAdTitle", () => {
  it("is the ad's title and employer, from the structured fields only", () => {
    expect(publicJobAdTitle({ title: "Sjuksköterska", companyName: "Region Exempel" })).toBe("Sjuksköterska – Region Exempel");
  });
});
