import { describe, expect, it } from "vitest";
import {
  juniorJobAdQueueSchema,
  toJuniorJobsPage,
  wireJuniorDecision,
  wireJuniorView,
} from "./admin-junior-job-ads";

// The wire shape below is the one `GET /api/v1/admin/junior-job-ads` serialises (#2264): camelCase properties,
// enums as their .NET names, a DateTimeOffset as an ISO string with an offset, and a null written out.

const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

const ITEM = {
  id: ID,
  title: "Junior systemutvecklare .NET",
  companyName: "Arbetsgivare A",
  municipalityName: "Göteborg",
  publishedAt: "2026-11-19T08:15:00+00:00",
  expiresAt: "2026-12-17T22:59:59+00:00",
  verdict: "Unclear",
  ruleId: "candidate-only",
  decision: "Junior",
};

const QUEUE = {
  items: { items: [ITEM], totalCount: 26, page: 2, pageSize: 25, totalPages: 2 },
  counts: { unclear: 26, shown: 40, decided: 3 },
};

const with_ = (patch: Record<string, unknown>) => ({ ...QUEUE, items: { ...QUEUE.items, items: [{ ...ITEM, ...patch }] } });

describe("the admin junior queue's wire shape (#2264)", () => {
  it("reads a page and the counts into the view model", () => {
    expect(toJuniorJobsPage(juniorJobAdQueueSchema.parse(QUEUE))).toEqual({
      items: [
        {
          id: ID,
          title: "Junior systemutvecklare .NET",
          company: "Arbetsgivare A",
          municipality: "Göteborg",
          publishedAt: "2026-11-19T08:15:00+00:00",
          expiresAt: "2026-12-17T22:59:59+00:00",
          verdict: "unclear",
          ruleId: "candidate-only",
          decision: "junior",
        },
      ],
      page: 2,
      totalPages: 2,
      totalCount: 26,
      counts: { unclear: 26, shown: 40, decided: 3 },
    });
  });

  it("reads an ad with no kommun, no last day and no decision", () => {
    const page = toJuniorJobsPage(
      juniorJobAdQueueSchema.parse(with_({ municipalityName: null, expiresAt: null, decision: null, verdict: "NotJunior" })),
    );
    expect(page.items[0]).toMatchObject({ municipality: null, expiresAt: null, decision: null, verdict: "notJunior" });
  });

  it("keeps a rule id it does not know as it came: the page names it, not the schema", () => {
    expect(juniorJobAdQueueSchema.safeParse(with_({ ruleId: "title-intern" })).success).toBe(true);
  });

  it("reads an empty tab with its counts", () => {
    const empty = { items: { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 }, counts: { unclear: 0, shown: 4, decided: 1 } };
    expect(toJuniorJobsPage(juniorJobAdQueueSchema.parse(empty))).toMatchObject({ items: [], totalCount: 0, totalPages: 0 });
  });

  it.each([
    ["a verdict it does not know", with_({ verdict: "Maybe" })],
    ["Unclear as a decision", with_({ decision: "Unclear" })],
    ["an id that is no GUID", with_({ id: "not-a-guid" })],
    ["an empty rule id", with_({ ruleId: "" })],
    ["an instant that does not read", with_({ publishedAt: "igår" })],
    [
      "a missing kommun field",
      {
        ...QUEUE,
        items: { ...QUEUE.items, items: [Object.fromEntries(Object.entries(ITEM).filter(([key]) => key !== "municipalityName"))] },
      },
    ],
    ["a field the queue does not know on an ad", with_({ description: "Annonsens text." })],
    ["a field the queue does not know on the counts", { ...QUEUE, counts: { ...QUEUE.counts, all: 69 } }],
    ["a field the queue does not know on the page", { ...QUEUE, items: { ...QUEUE.items, hasNextPage: false } }],
    ["a missing count", { ...QUEUE, counts: { unclear: 1, shown: 2 } }],
    ["a negative count", { ...QUEUE, counts: { ...QUEUE.counts, decided: -1 } }],
    ["page zero", { ...QUEUE, items: { ...QUEUE.items, page: 0 } }],
  ])("refuses %s", (_label, body) => {
    expect(juniorJobAdQueueSchema.safeParse(body).success).toBe(false);
  });

  it("names the tabs and the decisions as the backend does", () => {
    expect([wireJuniorView("unclear"), wireJuniorView("shown"), wireJuniorView("decided")]).toEqual([
      "Unclear",
      "Shown",
      "Decided",
    ]);
    expect([wireJuniorDecision("junior"), wireJuniorDecision("notJunior"), wireJuniorDecision(null)]).toEqual([
      "Junior",
      "NotJunior",
      null,
    ]);
  });
});
