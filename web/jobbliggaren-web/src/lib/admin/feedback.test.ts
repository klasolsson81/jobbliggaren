import { describe, expect, it } from "vitest";
import {
  DEFAULT_FEEDBACK_QUERY,
  FEEDBACK_PAGE_KEYS,
  feedbackHref,
  isFeedbackId,
  isFeedbackPageKey,
  parseFeedbackQuery,
  withId,
  withoutId,
  withPage,
  withPageNumber,
  withStatus,
  withWindow,
} from "./feedback";

const BASE = "/admin/feedback";
const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

describe("parseFeedbackQuery (#1979)", () => {
  it("reads the bare route as the first page of everything, over 30 days, with nothing open", () => {
    expect(parseFeedbackQuery({})).toEqual(DEFAULT_FEEDBACK_QUERY);
    expect(DEFAULT_FEEDBACK_QUERY).toEqual({ status: null, page: null, pageNumber: 1, id: null, window: 30 });
  });

  it("reads the notice mail's link as the submission it names", () => {
    expect(parseFeedbackQuery({ id: ID })).toEqual({ ...DEFAULT_FEEDBACK_QUERY, id: ID });
  });

  it("reads every part the page's links write", () => {
    expect(parseFeedbackQuery({ status: "pagar", sida: "cv-review", sidnr: "3", fonster: "7", id: ID })).toEqual({
      status: "inProgress",
      page: "cv-review",
      pageNumber: 3,
      id: ID,
      window: 7,
    });
    expect(parseFeedbackQuery({ status: "ny" }).status).toBe("new");
    expect(parseFeedbackQuery({ status: "atgardad" }).status).toBe("resolved");
    expect(parseFeedbackQuery({ status: "avstar" }).status).toBe("declined");
    expect(parseFeedbackQuery({ fonster: "90" }).window).toBe(90);
  });

  it.each([
    ["an unknown status", { status: "New" }, { status: null }],
    ["a page outside the set", { sida: "../admin" }, { page: null }],
    ["a page number that is not a whole positive number", { sidnr: "2.5" }, { pageNumber: 1 }],
    ["page number zero", { sidnr: "0" }, { pageNumber: 1 }],
    ["a page number past the cap", { sidnr: "99999" }, { pageNumber: 1 }],
    ["a window the summary does not offer", { fonster: "14" }, { window: 30 }],
    ["an empty id", { id: "  " }, { id: null }],
    ["an id of the wrong shape", { id: "not-a-guid" }, { id: null }],
    ["an id with a path behind it", { id: `${ID}/status` }, { id: null }],
  ])("reads %s as the default", (_label, params, expected) => {
    expect(parseFeedbackQuery(params)).toEqual({ ...DEFAULT_FEEDBACK_QUERY, ...expected });
  });

  it("never writes an id of the wrong shape into a link", () => {
    const query = parseFeedbackQuery({ status: "ny", id: "kalle@example.test" });
    expect(feedbackHref(BASE, withPageNumber(query, 2))).toBe(`${BASE}?status=ny&sidnr=2`);
  });

  it("reads the first of a repeated key", () => {
    expect(parseFeedbackQuery({ status: ["avstar", "ny"] }).status).toBe("declined");
    expect(parseFeedbackQuery({ id: [ID, "not-a-guid"] }).id).toBe(ID);
  });
});

describe("feedbackHref (#1979)", () => {
  it("leaves every default out, so the bare route is the first page of everything", () => {
    expect(feedbackHref(BASE, DEFAULT_FEEDBACK_QUERY)).toBe(BASE);
  });

  it("writes what parseFeedbackQuery reads back", () => {
    const query = { status: "declined", page: "job-ad", pageNumber: 2, id: ID, window: 90 } as const;
    const href = feedbackHref(BASE, query);
    expect(href).toBe(`${BASE}?status=avstar&sida=job-ad&sidnr=2&fonster=90&id=${ID}`);
    expect(parseFeedbackQuery(Object.fromEntries(new URL(href, "https://example.test").searchParams))).toEqual(query);
  });

  it("starts a status or page filter from the first page and keeps the open submission open", () => {
    const query = { ...DEFAULT_FEEDBACK_QUERY, pageNumber: 4, id: ID };
    expect(withStatus(query, "new")).toEqual({ ...query, status: "new", pageNumber: 1 });
    expect(withPage(query, "jobs")).toEqual({ ...query, page: "jobs", pageNumber: 1 });
    expect(withPageNumber(query, 2)).toEqual({ ...query, pageNumber: 2 });
    expect(withWindow(query, 7)).toEqual({ ...query, window: 7 });
    expect(withId(DEFAULT_FEEDBACK_QUERY, ID)).toEqual({ ...DEFAULT_FEEDBACK_QUERY, id: ID });
  });

  it("leads back from an open submission to the list it was opened from", () => {
    const query = { status: "inProgress", page: "jobs", pageNumber: 2, id: ID, window: 7 } as const;
    expect(withoutId(query)).toEqual({ ...query, id: null });
    expect(feedbackHref(BASE, withoutId(query))).toBe(`${BASE}?status=pagar&sida=jobs&sidnr=2&fonster=7`);
  });
});

describe("the fixed sets (#1979)", () => {
  it("knows the backend's 20 page keys, and only those", () => {
    expect(FEEDBACK_PAGE_KEYS).toHaveLength(20);
    expect(new Set(FEEDBACK_PAGE_KEYS).size).toBe(20);
    expect(isFeedbackPageKey("my-pages")).toBe(true);
    expect(isFeedbackPageKey("My-pages")).toBe(false);
  });

  it("takes only a GUID as a submission id", () => {
    expect(isFeedbackId(ID)).toBe(true);
    expect(isFeedbackId(ID.toUpperCase())).toBe(true);
    expect(isFeedbackId(`${ID}/status`)).toBe(false);
    expect(isFeedbackId(undefined)).toBe(false);
  });
});
