import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { AdminFeedbackView } from "@/components/admin/admin-feedback-view";
import { AdminFeedbackScreenshot } from "@/components/admin/admin-feedback-screenshot";
import { AdminPageHeader } from "@/components/admin/admin-page-header";
import { changeFeedbackStatusAction, requeueFeedbackNotificationAction } from "@/lib/actions/admin-feedback";
import {
  FEEDBACK_PAGE_SIZE,
  FEEDBACK_ROUTE,
  parseFeedbackQuery,
  type FeedbackSearchParams,
} from "@/lib/admin/feedback";
import {
  listRegion,
  type AdminFeedbackAvailability,
  type AdminFeedbackItem,
  type AdminFeedbackListPage,
  type AdminFeedbackPageSummary,
  type AdminRegion,
  type AdminValueRegion,
} from "@/lib/admin/view-models";
import {
  getFeedbackAvailability,
  getFeedbackDetail,
  getFeedbackSummary,
  listFeedback,
} from "@/lib/api/admin-feedback";
import type { ApiResult } from "@/lib/dto/_helpers";
import {
  toFeedbackAvailability,
  toFeedbackItem,
  toFeedbackListPage,
  toFeedbackSummary,
  wireFeedbackStatus,
} from "@/lib/dto/admin-feedback";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("admin.feedback");
  return { title: t("meta.title") };
}

/**
 * `/admin/feedback` — feedback from the app's feedback button (#1979, ADR 0150). The URL drives the page:
 * `status`, `sida`, `sidnr`, `fonster` and `id`, so the notice mail's link opens the submission it names.
 * The list, the open submission, the summary and the availability are read side by side, and each read
 * becomes its own region: one that fails never blanks the others (ADR 0150 D2).
 *
 * The status change and the notice's requeue are Server Actions that read the Admin role again; they go to
 * the open submission as references, so the page itself stays a Server Component.
 */
export default async function AdminFeedbackPage({
  searchParams,
}: {
  searchParams: Promise<FeedbackSearchParams>;
}) {
  const t = await getTranslations("admin.feedback");
  const query = parseFeedbackQuery(await searchParams);

  const [list, detail, summary, availability] = await Promise.all([
    listFeedback({
      status: query.status === null ? undefined : wireFeedbackStatus(query.status),
      page: query.page ?? undefined,
      pageNumber: query.pageNumber,
      pageSize: FEEDBACK_PAGE_SIZE,
    }),
    query.id === null ? null : getFeedbackDetail(query.id),
    getFeedbackSummary(query.window),
    getFeedbackAvailability(),
  ]);

  const listState: AdminValueRegion<AdminFeedbackListPage> =
    list.kind === "ok" ? { kind: "loaded", data: toFeedbackListPage(list.data) } : { kind: "failed" };

  // No id opens nothing; an id that names no submission is the region's empty state.
  const detailState: AdminRegion<AdminFeedbackItem> | null =
    detail === null
      ? null
      : detail.kind === "ok"
        ? { kind: "loaded", data: toFeedbackItem(detail.data) }
        : detail.kind === "notFound"
          ? { kind: "empty" }
          : { kind: "failed" };

  const summaryState: AdminRegion<ReadonlyArray<AdminFeedbackPageSummary>> =
    summary.kind === "ok" ? listRegion(toFeedbackSummary(summary.data)) : { kind: "failed" };

  // A failed read is an unknown, never "open".
  const availabilityState: AdminValueRegion<AdminFeedbackAvailability> =
    availability.kind === "ok"
      ? { kind: "loaded", data: toFeedbackAvailability(availability.data) }
      : { kind: "failed" };

  // A refused read says why, since trying again later does not help it; any other failure takes the
  // region's shared line.
  const refusalLine = (result: ApiResult<unknown> | null): string | undefined => {
    switch (result?.kind) {
      case "rateLimited":
        return t("errors.rateLimited", { seconds: result.retryAfterSeconds });
      case "unauthorized":
        return t("errors.unauthorized");
      case "forbidden":
        return t("errors.forbidden");
      default:
        return undefined;
    }
  };

  return (
    <div className="flex flex-col gap-6">
      <AdminPageHeader title={t("heading")} />
      <AdminFeedbackView
        basePath={FEEDBACK_ROUTE}
        query={query}
        availability={availabilityState}
        list={listState}
        detail={detailState}
        screenshot={detailState?.kind === "loaded" ? (
          <AdminFeedbackScreenshot
            key={detailState.data.id}
            id={detailState.data.id}
            metadata={detailState.data.screenshot}
          />
        ) : null}
        summary={summaryState}
        failedLines={{ list: refusalLine(list), detail: refusalLine(detail), summary: refusalLine(summary) }}
        onStatus={changeFeedbackStatusAction}
        onRequeue={requeueFeedbackNotificationAction}
      />
    </div>
  );
}
