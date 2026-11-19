import { describe, expect, it } from "vitest";
import { juniorItHref, parseJuniorItParams } from "./junior-it-params";

describe("parseJuniorItParams", () => {
  it("reads no parameter as every kommun, newest first, page 1", () => {
    expect(parseJuniorItParams({})).toEqual({
      kind: "query",
      query: { municipality: null, sortBy: "PublishedAtDesc", page: 1 },
    });
  });

  it("reads the three parameters by the names /jobb uses", () => {
    expect(parseJuniorItParams({ municipality: "PVZL_BQT_XtL", sortBy: "ExpiresAtAsc", page: "3" })).toEqual({
      kind: "query",
      query: { municipality: "PVZL_BQT_XtL", sortBy: "ExpiresAtAsc", page: 3 },
    });
  });

  it("treats an empty kommun and an empty order as the defaults, as the GET form sends them", () => {
    expect(parseJuniorItParams({ municipality: "", sortBy: "" })).toEqual({
      kind: "query",
      query: { municipality: null, sortBy: "PublishedAtDesc", page: 1 },
    });
  });

  it("ignores every other parameter, a LinkedIn click id included", () => {
    expect(parseJuniorItParams({ trk: "x", utm_source: "linkedin", q: "junior" })).toEqual({
      kind: "query",
      query: { municipality: null, sortBy: "PublishedAtDesc", page: 1 },
    });
  });

  it.each([
    [{ municipality: ["a", "b"] }],
    [{ municipality: "Göteborg" }],
    [{ municipality: "x".repeat(33) }],
    [{ sortBy: "Relevance" }],
    [{ sortBy: "MatchDesc" }],
    [{ page: "0" }],
    [{ page: "02" }],
    [{ page: "abc" }],
    [{ page: "99999999999999999999" }],
    [{ page: ["1", "2"] }],
  ])("refuses %j as no such page", (params) => {
    expect(parseJuniorItParams(params)).toEqual({ kind: "noSuchPage" });
  });
});

describe("juniorItHref", () => {
  it("writes the bare path for the defaults", () => {
    expect(juniorItHref({ municipality: null, sortBy: "PublishedAtDesc", page: 1 })).toBe("/junior");
  });

  it("writes only what differs from the defaults, in one fixed order", () => {
    expect(juniorItHref({ municipality: "PVZL_BQT_XtL", sortBy: "ExpiresAtAsc", page: 2 })).toBe(
      "/junior?municipality=PVZL_BQT_XtL&sortBy=ExpiresAtAsc&page=2"
    );
    expect(juniorItHref({ municipality: null, sortBy: "PublishedAtDesc", page: 4 })).toBe("/junior?page=4");
  });

  it("reads back what it writes", () => {
    const query = { municipality: "PVZL_BQT_XtL", sortBy: "ExpiresAtAsc", page: 7 } as const;
    const params = Object.fromEntries(new URL(juniorItHref(query), "https://example.test").searchParams);
    expect(parseJuniorItParams(params)).toEqual({ kind: "query", query });
  });
});
