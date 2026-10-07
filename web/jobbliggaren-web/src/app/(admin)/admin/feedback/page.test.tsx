import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  FeedbackAvailabilityDto,
  FeedbackDetailDto,
  FeedbackListResponse,
  FeedbackSummaryDto,
} from "@/lib/dto/admin-feedback";
import type { FeedbackSearchParams } from "@/lib/admin/feedback";
import AdminFeedbackPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

const api = vi.hoisted(() => ({
  listFeedback: vi.fn<() => Promise<ApiResult<FeedbackListResponse>>>(),
  getFeedbackDetail: vi.fn<() => Promise<ApiResult<FeedbackDetailDto>>>(),
  getFeedbackSummary: vi.fn<() => Promise<ApiResult<FeedbackSummaryDto>>>(),
  getFeedbackAvailability: vi.fn<() => Promise<ApiResult<FeedbackAvailabilityDto>>>(),
}));
vi.mock("@/lib/api/admin-feedback", () => api);

const actions = vi.hoisted(() => ({
  changeFeedbackStatusAction: vi.fn(async () => null),
  requeueFeedbackNotificationAction: vi.fn(async () => null),
}));
vi.mock("@/lib/actions/admin-feedback", () => actions);

const ID = "00000000-0000-4000-8000-000000000501";
const OTHER = "00000000-0000-4000-8000-000000000502";

const LIST: FeedbackListResponse = {
  items: {
    items: [
      {
        id: ID,
        pageKey: "jobs",
        rating: 4,
        excerpt: "Sökningen på kommun ger träffar från hela länet.",
        status: "New",
        submittedAt: "2026-10-04T05:12:00+00:00",
        notificationState: "Accepted",
      },
      {
        id: OTHER,
        pageKey: "cv",
        rating: null,
        excerpt: null,
        status: "Resolved",
        submittedAt: "2026-10-03T05:12:00+00:00",
        notificationState: "Failed",
      },
    ],
    totalCount: 2,
    page: 1,
    pageSize: 25,
    totalPages: 1,
  },
  counts: { all: 2, new: 1, inProgress: 0, resolved: 1, declined: 0 },
};

const DETAIL: FeedbackDetailDto = {
  id: ID,
  pageKey: "jobs",
  rating: 4,
  comment: "Sökningen på kommun ger träffar från hela länet.",
  status: "New",
  submittedAt: "2026-10-04T05:12:00+00:00",
  statusChangedAt: null,
  reporterEmail: "konto.b@example.test",
  client: {
    viewportWidth: 1440,
    viewportHeight: 789,
    screenWidth: 1440,
    screenHeight: 900,
    pixelRatio: 1,
    theme: "Light",
    deviceClass: "Desktop",
    osFamily: "Windows",
    browserFamily: "Firefox",
  },
  appVersion: "4f2a91c",
  notification: { state: "Accepted", attempts: 1, nextAttemptAt: "2026-10-04T05:12:00+00:00" },
};

const SUMMARY: FeedbackSummaryDto = {
  days: 30,
  pages: [
    { pageKey: "jobs", submissions: 3, raters: 2, rated1: 0, rated2: 0, rated3: 1, rated4: 1, rated5: 0, mean: 3.5 },
  ],
};

async function renderPage(params: FeedbackSearchParams = {}) {
  return render(await AdminFeedbackPage({ searchParams: Promise.resolve(params) }));
}

beforeEach(() => {
  vi.clearAllMocks();
  api.listFeedback.mockResolvedValue({ kind: "ok", data: LIST });
  api.getFeedbackDetail.mockResolvedValue({ kind: "ok", data: DETAIL });
  api.getFeedbackSummary.mockResolvedValue({ kind: "ok", data: SUMMARY });
  api.getFeedbackAvailability.mockResolvedValue({ kind: "ok", data: { availability: "Open" } });
});

const list = () => screen.getByRole("region", { name: "Inskick" });

describe("/admin/feedback — driven by its URL (#1979, ADR 0150)", () => {
  it("reads the first page of every submission, the 30-day summary and the availability, and opens nothing", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Feedback" })).toBeInTheDocument();
    expect(api.listFeedback).toHaveBeenCalledWith({ status: undefined, page: undefined, pageNumber: 1, pageSize: 25 });
    expect(api.getFeedbackSummary).toHaveBeenCalledWith(30);
    expect(api.getFeedbackAvailability).toHaveBeenCalled();
    expect(api.getFeedbackDetail).not.toHaveBeenCalled();
    expect(within(list()).getAllByRole("link")).toHaveLength(2);
    expect(screen.queryByRole("region", { name: "Valt inskick" })).toBeNull();
  });

  it("opens the submission the notice mail's link names, beside the list of every submission", async () => {
    await renderPage({ id: ID });

    expect(api.getFeedbackDetail).toHaveBeenCalledWith(ID);
    const detail = screen.getByRole("region", { name: "Valt inskick" });
    expect(detail).toHaveTextContent("Sökningen på kommun ger träffar från hela länet.");
    expect(detail).toHaveTextContent("konto.b@example.test");
    expect(within(list()).getAllByRole("link")[0]).toHaveAttribute("aria-current", "true");
  });

  it("passes the status, the page, the page number and the window to the backend by its own names", async () => {
    await renderPage({ status: "pagar", sida: "jobs", sidnr: "2", fonster: "7", id: OTHER });

    expect(api.listFeedback).toHaveBeenCalledWith({ status: "InProgress", page: "jobs", pageNumber: 2, pageSize: 25 });
    expect(api.getFeedbackSummary).toHaveBeenCalledWith(7);
    expect(api.getFeedbackDetail).toHaveBeenCalledWith(OTHER);
    expect(screen.getByText("Sida: Jobb")).toBeInTheDocument();
  });

  it("says an id that names no submission does not exist", async () => {
    api.getFeedbackDetail.mockResolvedValue({ kind: "notFound" });
    await renderPage({ id: ID });

    expect(screen.getByRole("region", { name: "Valt inskick" })).toHaveTextContent("Inskicket finns inte.");
  });

  it("never blanks the page for one failing read: each region shows its own state", async () => {
    api.listFeedback.mockResolvedValue({ kind: "error" });
    api.getFeedbackAvailability.mockResolvedValue({ kind: "rateLimited", retryAfterSeconds: 6 });
    await renderPage({ id: ID });

    expect(within(list()).getByRole("alert")).toHaveTextContent("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
    expect(screen.getByText("Det går inte att se om feedback är öppen.")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Valt inskick" })).toHaveTextContent("konto.b@example.test");
    expect(screen.getByRole("table", { name: "Betyg per sida de senaste 30 dygnen" })).toBeInTheDocument();
  });

  it("shows a failed summary and a failed submission as their own lines, the list kept", async () => {
    api.getFeedbackSummary.mockResolvedValue({ kind: "error" });
    api.getFeedbackDetail.mockResolvedValue({ kind: "error" });
    await renderPage({ id: ID });

    expect(screen.getByRole("region", { name: "Betyg per sida" })).toHaveTextContent(
      "Uppgifterna kunde inte hämtas. Försök igen om en stund.",
    );
    expect(screen.getByRole("region", { name: "Valt inskick" })).toHaveTextContent(
      "Uppgifterna kunde inte hämtas. Försök igen om en stund.",
    );
    expect(within(list()).getAllByRole("link")).toHaveLength(2);
  });

  it("says why a refused read was refused, since trying again later does not help it", async () => {
    api.listFeedback.mockResolvedValue({ kind: "forbidden" });
    api.getFeedbackDetail.mockResolvedValue({ kind: "unauthorized" });
    api.getFeedbackSummary.mockResolvedValue({ kind: "rateLimited", retryAfterSeconds: 6 });
    await renderPage({ id: ID });

    expect(within(list()).getByRole("alert")).toHaveTextContent("Din session saknar Admin-rollen.");
    expect(within(screen.getByRole("region", { name: "Valt inskick" })).getByRole("alert")).toHaveTextContent(
      "Du är inte inloggad längre. Logga in och försök igen.",
    );
    expect(within(screen.getByRole("region", { name: "Betyg per sida" })).getByRole("alert")).toHaveTextContent(
      "För många förfrågningar. Försök igen om 6 sekunder.",
    );
  });

  it("says why feedback is closed, and nothing while it is open", async () => {
    const { unmount } = await renderPage();
    expect(screen.queryByText(/Feedback är/)).toBeNull();
    unmount();

    api.getFeedbackAvailability.mockResolvedValue({ kind: "ok", data: { availability: "Disabled" } });
    await renderPage();
    expect(screen.getByText("Feedback är avstängd.")).toBeInTheDocument();
  });

  it("changes the status through its Server Action", async () => {
    await renderPage({ id: ID });

    const detail = screen.getByRole("region", { name: "Valt inskick" });
    await userEvent.selectOptions(within(detail).getByRole("combobox", { name: "Status" }), "Åtgärdad");
    await userEvent.click(within(detail).getByRole("button", { name: "Spara status" }));

    expect(actions.changeFeedbackStatusAction).toHaveBeenCalledWith(ID, "resolved");
  });
});
