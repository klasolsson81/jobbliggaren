import { describe, expect, it } from "vitest";
import {
  feedbackAvailabilitySchema,
  feedbackDetailSchema,
  feedbackListResponseSchema,
  feedbackSummarySchema,
  toFeedbackAvailability,
  toFeedbackItem,
  toFeedbackListPage,
  toFeedbackSummary,
  wireFeedbackStatus,
} from "./admin-feedback";

// The wire shapes below are the ones the admin feedback endpoints serialise (#1979): camelCase
// properties, enums as their .NET names, instants as ISO strings.

const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

const DETAIL = {
  id: ID,
  pageKey: "job-ad",
  rating: 4,
  comment: "Texten.",
  status: "InProgress",
  submittedAt: "2026-10-04T05:12:00+00:00",
  statusChangedAt: null,
  reporterEmail: "konto.b@example.test",
  client: {
    viewportWidth: 390,
    viewportHeight: 844,
    screenWidth: 390,
    screenHeight: 844,
    pixelRatio: 2.5,
    theme: "Dark",
    deviceClass: "Mobile",
    osFamily: "Ios",
    browserFamily: "SamsungInternet",
  },
  appVersion: "4f2a91c",
  notification: {
    state: "Unknown",
    attempts: 1,
    nextAttemptAt: "2026-10-04T05:12:00+00:00",
    acceptedAt: null,
    stateChangedAt: "2026-10-04T05:23:00+00:00",
  },
};

describe("the admin feedback wire shapes (#1979)", () => {
  it("reads a list page and its counts into the view model", () => {
    const parsed = feedbackListResponseSchema.parse({
      items: {
        items: [
          {
            id: ID,
            pageKey: "cv",
            rating: null,
            excerpt: null,
            status: "Declined",
            submittedAt: "2026-10-04T05:12:00+00:00",
            notificationState: "Failed",
          },
        ],
        totalCount: 26,
        page: 2,
        pageSize: 25,
        totalPages: 2,
      },
      counts: { all: 30, new: 4, inProgress: 0, resolved: 0, declined: 26 },
    });

    expect(toFeedbackListPage(parsed)).toEqual({
      items: [
        {
          id: ID,
          page: "cv",
          rating: null,
          excerpt: null,
          status: "declined",
          submittedAt: "2026-10-04T05:12:00+00:00",
          notice: "failed",
        },
      ],
      page: 2,
      totalPages: 2,
      totalCount: 26,
      counts: { all: 30, new: 4, inProgress: 0, resolved: 0, declined: 26 },
    });
  });

  it("reads one submission with what its browser reported, and its notice's state and attempts", () => {
    expect(toFeedbackItem(feedbackDetailSchema.parse(DETAIL))).toEqual({
      id: ID,
      page: "job-ad",
      rating: 4,
      comment: "Texten.",
      status: "inProgress",
      submittedAt: "2026-10-04T05:12:00+00:00",
      statusChangedAt: null,
      reporterEmail: "konto.b@example.test",
      client: {
        viewportWidth: 390,
        viewportHeight: 844,
        screenWidth: 390,
        screenHeight: 844,
        pixelRatio: 2.5,
        theme: "dark",
        deviceClass: "mobile",
        os: "ios",
        browser: "samsungInternet",
      },
      appVersion: "4f2a91c",
      notice: { state: "unknown", attempts: 1, nextAttemptAt: "2026-10-04T05:12:00+00:00" },
    });
  });

  // Declared unreachable today: the backend's families, themes, statuses, states and page keys are closed
  // sets. The three tests below pin only how this side degrades when the backend learns a value first.
  it("reads a browser family it does not know yet as unknown, never refusing the submission", () => {
    const item = toFeedbackItem(
      feedbackDetailSchema.parse({
        ...DETAIL,
        client: { ...DETAIL.client, osFamily: "HarmonyOs", browserFamily: null, theme: "Sepia" },
      }),
    );
    expect(item.client).toMatchObject({ os: null, browser: null, theme: null, deviceClass: "mobile" });
  });

  it("refuses a status it does not know, since the status decides what the page offers", () => {
    expect(feedbackDetailSchema.safeParse({ ...DETAIL, status: "Archived" }).success).toBe(false);
    expect(
      feedbackDetailSchema.safeParse({ ...DETAIL, notification: { ...DETAIL.notification, state: "Bounced" } }).success,
    ).toBe(false);
    expect(feedbackDetailSchema.safeParse({ ...DETAIL, rating: 6 }).success).toBe(false);
  });

  it("orders the summary as the app orders its pages, a key it does not know last", () => {
    const row = (pageKey: string) => ({
      pageKey,
      submissions: 1,
      raters: 1,
      rated1: 0,
      rated2: 0,
      rated3: 0,
      rated4: 1,
      rated5: 0,
      mean: 4,
    });
    const summary = toFeedbackSummary(
      feedbackSummarySchema.parse({ days: 30, pages: [row("statistics"), row("future-page"), row("cv"), row("jobs")] }),
    );

    expect(summary.map((page) => page.page)).toEqual(["jobs", "statistics", "cv", "future-page"]);
    expect(summary[0]).toEqual({ page: "jobs", submissions: 1, raters: 1, ratings: [0, 0, 0, 1, 0], mean: 4 });
  });

  it("names the availability and the status the way each side does", () => {
    expect(toFeedbackAvailability(feedbackAvailabilitySchema.parse({ availability: "NoRecipient" }))).toBe("noRecipient");
    expect(toFeedbackAvailability(feedbackAvailabilitySchema.parse({ availability: "CannotDeliver" }))).toBe(
      "cannotDeliver",
    );
    expect(wireFeedbackStatus("inProgress")).toBe("InProgress");
    expect(wireFeedbackStatus("declined")).toBe("Declined");
  });
});
