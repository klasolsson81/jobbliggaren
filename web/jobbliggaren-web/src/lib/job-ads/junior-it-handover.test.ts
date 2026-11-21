import { describe, expect, it } from "vitest";
import { juniorItHandover } from "./junior-it-handover";
import { juniorItHref, parseJuniorItParams } from "./junior-it-params";
import { buildJobbHref } from "./search-params";

const GOTEBORG = "PVZL_BQT_XtL";

const handover = (pathAndQuery: string) => juniorItHandover(new URL(pathAndQuery, "https://app.test"));

describe("juniorItHandover", () => {
  it("sends the bare list to its own address and to /jobb with the filter alone", () => {
    expect(handover("/junior")).toEqual({ junior: "/junior", jobb: "/jobb?junior=on" });
  });

  it("carries the kommun and the order to /jobb, and the page only on the list's own address", () => {
    expect(handover(`/junior?page=3&sortBy=ExpiresAtAsc&municipality=${GOTEBORG}`)).toEqual({
      junior: `/junior?municipality=${GOTEBORG}&sortBy=ExpiresAtAsc&page=3`,
      jobb: `/jobb?municipality=${GOTEBORG}&junior=on&sortBy=ExpiresAtAsc`,
    });
  });

  it("writes neither address with the default order or an empty kommun", () => {
    expect(handover("/junior?sortBy=PublishedAtDesc&municipality=")).toEqual({ junior: "/junior", jobb: "/jobb?junior=on" });
  });

  it("leaves behind every key the page does not read: the bounce's marker, a click id, a campaign tag", () => {
    expect(handover(`/junior?vidare=1&fbclid=IwAR0abc&utm_source=linkedin&municipality=${GOTEBORG}`)).toEqual({
      junior: `/junior?municipality=${GOTEBORG}`,
      jobb: `/jobb?municipality=${GOTEBORG}&junior=on`,
    });
  });

  it.each([
    ["a repeated kommun", `/junior?municipality=${GOTEBORG}&municipality=AvNB_uwa_6n6`],
    ["a repeated order", "/junior?sortBy=ExpiresAtAsc&sortBy=ExpiresAtAsc"],
    ["a kommun id with other characters", "/junior?municipality=G%C3%B6teborg"],
    ["page 0", "/junior?page=0"],
    ["a page that is not a whole number", "/junior?page=2.5"],
    ["an order /junior does not offer", "/junior?sortBy=PublishedAtAsc"],
  ])("refuses %s, as the page does with its 404", (_label, pathAndQuery) => {
    expect(handover(pathAndQuery)).toBeNull();
    const url = new URL(pathAndQuery, "https://app.test");
    const nextShape = Object.fromEntries(
      [...new Set(url.searchParams.keys())].map((key) => {
        const values = url.searchParams.getAll(key);
        return [key, values.length === 1 ? values[0] : values];
      })
    );
    expect(parseJuniorItParams(nextShape).kind).toBe("noSuchPage");
  });

  it.each(["/junior/x", "/juniorjobb", "/admin/juniorjobb", "/jobb", "/Junior", "/"])("ignores %s", (path) => {
    expect(handover(path)).toBeNull();
  });

  it("builds the list's address with the page's own writer and /jobb's with /jobb's own", () => {
    const query = { municipality: GOTEBORG, sortBy: "ExpiresAtAsc", page: 2 } as const;
    const result = handover(juniorItHref(query));

    expect(result?.junior).toBe(juniorItHref(query));
    expect(result?.jobb).toBe(
      buildJobbHref({
        q: "",
        occupationGroup: [],
        occupationName: [],
        region: [],
        municipality: [GOTEBORG],
        remote: false,
        junior: true,
        employmentType: [],
        worktimeExtent: [],
        matchGrades: [],
        sortBy: query.sortBy,
      })
    );
  });

  it("puts no key on /jobb but the filter, the kommun and the order", () => {
    const jobb = new URL(handover(`/junior?municipality=${GOTEBORG}&sortBy=ExpiresAtAsc&page=9`)?.jobb ?? "", "https://app.test");

    expect(jobb.pathname).toBe("/jobb");
    expect([...jobb.searchParams.keys()].sort()).toEqual(["junior", "municipality", "sortBy"]);
  });
});
