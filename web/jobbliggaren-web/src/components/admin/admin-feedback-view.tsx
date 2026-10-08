import Link from "next/link";
import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ArrowLeft, ArrowRight } from "lucide-react";
import {
  FEEDBACK_STATUSES,
  FEEDBACK_WINDOWS,
  feedbackHref,
  isFeedbackPageKey,
  withId,
  withoutId,
  withPage,
  withPageNumber,
  withStatus,
  withWindow,
  type AdminFeedbackQuery,
  type AdminFeedbackRefusal,
} from "@/lib/admin/feedback";
import type {
  AdminFeedbackAvailability,
  AdminFeedbackItem,
  AdminFeedbackListItem,
  AdminFeedbackListPage,
  AdminFeedbackPageSummary,
  AdminFeedbackStatus,
  AdminRegion,
  AdminValueRegion,
} from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminFeedbackDetail } from "./admin-feedback-detail";
import { FeedbackFocusLink, FeedbackFocusReceiver, FeedbackPagerLink } from "./admin-feedback-focus";
import { FeedbackNoticeState, FeedbackRating, FeedbackStatusPill, useFeedbackPageLabel } from "./admin-feedback-parts";
import { AdminRegionLine } from "./admin-region-line";
import { AdminTableScroll } from "./admin-table-scroll";
import { AdminUnknown } from "./admin-unknown";

export interface AdminFeedbackViewProps {
  /** "/admin/feedback", or the local preview's own route (ADR 0150 D5). */
  readonly basePath: string;
  /** What the URL asks for; every link keeps it and changes one part. */
  readonly query: AdminFeedbackQuery;
  readonly availability: AdminValueRegion<AdminFeedbackAvailability>;
  /** A list that answered with no submissions is loaded and empty: its counts are still known. */
  readonly list: AdminValueRegion<AdminFeedbackListPage>;
  /** Null while the URL opens no submission; `empty` when the one it names does not exist. */
  readonly detail: AdminRegion<AdminFeedbackItem> | null;
  readonly screenshot?: ReactNode;
  readonly summary: AdminRegion<ReadonlyArray<AdminFeedbackPageSummary>>;
  /** A refused read's own line, by region; a region without one shows the shared failed line. */
  readonly failedLines?: { readonly list?: string; readonly detail?: string; readonly summary?: string };
  readonly onStatus: (id: string, status: AdminFeedbackStatus) => Promise<AdminFeedbackRefusal>;
  readonly onRequeue: (id: string, acknowledgeDuplicateRisk: boolean) => Promise<AdminFeedbackRefusal>;
}

const sameId = (left: string, right: string | null) => right !== null && left.toLowerCase() === right.toLowerCase();

/**
 * Feedback from the app's feedback button (#1979, ADR 0150): a status line while feedback is closed, the
 * submissions as a master/detail layout and the ratings per page. Everything is driven by the URL, so the
 * notice mail's link opens the submission it names, and every filter, page and window is a link. Each
 * region is one state of ADR 0150 D2's union, and one failing read never blanks another.
 *
 * Below 1100 px an open submission is a step of its own: the filter, the page filter and the list step
 * aside, and the submission leads back to them. The DOM order is the visual order at every width.
 *
 * No directive: on the live page the list, the filters and the summary render on the server, and only the
 * open submission, which holds a status choice and runs commands, and the links that move focus are
 * client islands.
 */
export function AdminFeedbackView({
  basePath,
  query,
  availability,
  list,
  detail,
  screenshot,
  summary,
  failedLines,
  onStatus,
  onRequeue,
}: AdminFeedbackViewProps) {
  const t = useTranslations("admin.feedback");
  const open = detail !== null;

  return (
    <>
      <FeedbackAvailabilityLine region={availability} />
      <div className={open ? "jp-adminfeedback jp-adminfeedback--open" : "jp-adminfeedback"}>
        <FeedbackStatusFilter basePath={basePath} query={query} list={list} />
        <FeedbackScope basePath={basePath} query={query} />
        <div className={open ? "jp-adminfeedback__grid" : undefined}>
          <section
            aria-labelledby="admin-feedback-list"
            tabIndex={-1}
            data-feedback-focus="list"
            className="jp-adminfeedback__master"
          >
            <h2 id="admin-feedback-list" className="sr-only">
              {t("list.label")}
            </h2>
            <FeedbackList basePath={basePath} query={query} region={list} failed={failedLines?.list} />
          </section>
          {detail === null ? null : (
            <AdminFeedbackDetail
              key={query.id ?? ""}
              region={detail}
              screenshot={screenshot}
              openId={query.id ?? ""}
              backHref={feedbackHref(basePath, withoutId(query))}
              failed={failedLines?.detail}
              onStatus={onStatus}
              onRequeue={onRequeue}
            />
          )}
        </div>
      </div>
      <FeedbackSummary basePath={basePath} query={query} region={summary} failed={failedLines?.summary} />
      <FeedbackFocusReceiver location={feedbackHref(basePath, query)} />
    </>
  );
}

/** Shown only while feedback is closed, or while whether it is open cannot be read: never as "open". */
function FeedbackAvailabilityLine({ region }: { readonly region: AdminValueRegion<AdminFeedbackAvailability> }) {
  const t = useTranslations("admin.feedback.availability");
  if (region.kind === "failed") {
    return (
      <p className="jp-adminfeedback__availability" data-state="unknown">
        {t("unknown")}
      </p>
    );
  }
  if (region.kind !== "loaded" || region.data === "open") return null;
  return <p className="jp-adminfeedback__availability">{t(region.data)}</p>;
}

/**
 * One link per status, the house `.jp-subnav`: each status is its own URL (ADR 0150 D8). A count is shown
 * only when the list answered, so an unknown count is never "(0)" (ADR 0150 D2).
 */
function FeedbackStatusFilter({
  basePath,
  query,
  list,
}: {
  readonly basePath: string;
  readonly query: AdminFeedbackQuery;
  readonly list: AdminValueRegion<AdminFeedbackListPage>;
}) {
  const t = useTranslations("admin.feedback");
  if (list.kind === "unavailable") return null;
  const counts = list.kind === "loaded" ? list.data.counts : null;
  const options: ReadonlyArray<AdminFeedbackStatus | null> = [null, ...FEEDBACK_STATUSES];

  return (
    <nav className="jp-subnav" aria-label={t("filter.label")}>
      {options.map((status) => {
        const active = status === query.status;
        const label = status === null ? t("filter.all") : t(`status.${status}`);
        const count = counts === null ? null : status === null ? counts.all : counts[status];
        return (
          <Link
            key={status ?? "all"}
            href={feedbackHref(basePath, withStatus(query, status))}
            className="jp-subnav__item"
            data-active={active}
            aria-current={active ? "true" : undefined}
          >
            {count === null ? label : t("filter.count", { label, count })}
          </Link>
        );
      })}
    </nav>
  );
}

/**
 * The page filter in force, set from the summary's page names, and the link that lifts it. The line takes
 * focus after a page name sets it; lifting it leaves focus on the list's first submission.
 */
function FeedbackScope({ basePath, query }: { readonly basePath: string; readonly query: AdminFeedbackQuery }) {
  const t = useTranslations("admin.feedback.scope");
  const pageLabel = useFeedbackPageLabel();
  if (query.page === null) return null;
  return (
    <p className="jp-adminfeedback__scope" tabIndex={-1} data-feedback-focus="scope">
      <span>{t("page", { page: pageLabel(query.page) })}</span>
      <FeedbackFocusLink
        href={feedbackHref(basePath, withPage(query, null))}
        focusTo="list"
        className="jp-adminfeedback__textlink"
      >
        {t("clear")}
      </FeedbackFocusLink>
    </p>
  );
}

function FeedbackList({
  basePath,
  query,
  region,
  failed,
}: {
  readonly basePath: string;
  readonly query: AdminFeedbackQuery;
  readonly region: AdminValueRegion<AdminFeedbackListPage>;
  readonly failed: string | undefined;
}) {
  const t = useTranslations("admin.feedback.list");
  if (region.kind !== "loaded") return <AdminRegionLine kind={region.kind} failed={failed} region />;

  const { items, page, totalPages, totalCount } = region.data;
  if (items.length === 0 && totalCount > 0) {
    // A page number past the last page: the submissions exist, this page of them does not.
    return (
      <p className="jp-adminsoon jp-adminsoon--region">
        {t("pageMissing")}{" "}
        <FeedbackFocusLink
          href={feedbackHref(basePath, withPageNumber(query, 1))}
          focusTo="list"
          className="jp-adminfeedback__textlink"
        >
          {t("firstPage")}
        </FeedbackFocusLink>
      </p>
    );
  }
  if (items.length === 0) {
    const filtered = query.status !== null || query.page !== null;
    return <AdminRegionLine kind="empty" empty={filtered ? t("emptyFiltered") : t("empty")} region />;
  }

  return (
    <>
      <ol className="jp-adminfeedback__list">
        {items.map((item) => (
          <li key={item.id}>
            <FeedbackListLink
              item={item}
              href={feedbackHref(basePath, withId(query, item.id))}
              open={sameId(item.id, query.id)}
            />
          </li>
        ))}
      </ol>
      <FeedbackPager basePath={basePath} query={query} page={page} pages={totalPages} />
    </>
  );
}

/**
 * One submission: its status, page, rating, time, excerpt and notice, and never the reporter's address.
 * Opening it keeps the scroll where it is; the open submission takes focus itself.
 */
function FeedbackListLink({
  item,
  href,
  open,
}: {
  readonly item: AdminFeedbackListItem;
  readonly href: string;
  readonly open: boolean;
}) {
  const format = useFormatter();
  const pageLabel = useFeedbackPageLabel();
  return (
    <Link
      href={href}
      scroll={false}
      className="jp-adminfeedback__item"
      data-feedback-item={item.id.toLowerCase()}
      aria-current={open ? "true" : undefined}
    >
      <span className="jp-adminfeedback__itemhead">
        <FeedbackStatusPill status={item.status} />
        <span className="jp-adminfeedback__page">{pageLabel(item.page)}</span>
        <span className="jp-adminfeedback__rating">
          <FeedbackRating rating={item.rating} />
        </span>
        <span className="jp-adminfeedback__time">{formatDateTime(format, item.submittedAt) ?? <AdminUnknown />}</span>
      </span>
      {item.excerpt === null ? null : <span className="jp-adminfeedback__excerpt">{item.excerpt}</span>}
      {item.notice === null ? null : (
        <span className="jp-adminfeedback__notice">
          <FeedbackNoticeState state={item.notice} named />
        </span>
      )}
    </Link>
  );
}

/** "Sida 2 av 3" with Föregående and Nästa on every page, in the same place (`FeedbackPagerLink`). */
function FeedbackPager({
  basePath,
  query,
  page,
  pages,
}: {
  readonly basePath: string;
  readonly query: AdminFeedbackQuery;
  readonly page: number;
  readonly pages: number;
}) {
  const t = useTranslations("admin.users.pager");
  if (pages <= 1) return null;
  const first = page <= 1;
  const last = page >= pages;
  return (
    <nav className="jp-adminpager" aria-label={t("label")}>
      <p className="jp-adminpager__position" role="status">
        {t("position", { page, pages })}
      </p>
      <div className="jp-adminpager__buttons">
        <FeedbackPagerLink
          href={feedbackHref(basePath, withPageNumber(query, first ? page : page - 1))}
          disabled={first}
          rel="prev"
          className="jp-btn jp-btn--sm jp-btn--secondary"
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("previous")}
        </FeedbackPagerLink>
        <FeedbackPagerLink
          href={feedbackHref(basePath, withPageNumber(query, last ? page : page + 1))}
          disabled={last}
          rel="next"
          className="jp-btn jp-btn--sm jp-btn--secondary"
        >
          {t("next")}
          <ArrowRight size={16} aria-hidden="true" />
        </FeedbackPagerLink>
      </div>
    </nav>
  );
}

const RATING_COLUMNS = [1, 2, 3, 4, 5] as const;
const SUMMARY_CAPTION_ID = "admin-feedback-summary-caption";

/**
 * Betyg per sida over the window: how many rated each page, the 1–5 spread as numbers, the mean with one
 * decimal and how many submissions arrived. A page name filters the list to that page, and focus goes to
 * the line that says so.
 */
function FeedbackSummary({
  basePath,
  query,
  region,
  failed,
}: {
  readonly basePath: string;
  readonly query: AdminFeedbackQuery;
  readonly region: AdminRegion<ReadonlyArray<AdminFeedbackPageSummary>>;
  readonly failed: string | undefined;
}) {
  const t = useTranslations("admin.feedback.summary");
  const format = useFormatter();
  const pageLabel = useFeedbackPageLabel();

  return (
    <section aria-labelledby="admin-feedback-summary" className="jp-adminfeedback__summary">
      <h2 id="admin-feedback-summary" className="jp-h2">
        {t("heading")}
      </h2>
      {region.kind === "unavailable" ? null : (
        <nav className="jp-subnav" aria-label={t("windowLabel")}>
          {FEEDBACK_WINDOWS.map((days) => {
            const active = days === query.window;
            return (
              <Link
                key={days}
                href={feedbackHref(basePath, withWindow(query, days))}
                scroll={false}
                className="jp-subnav__item"
                data-active={active}
                aria-current={active ? "true" : undefined}
              >
                {t(`windows.d${days}`)}
              </Link>
            );
          })}
        </nav>
      )}
      {region.kind !== "loaded" ? (
        <AdminRegionLine kind={region.kind} empty={t("empty")} failed={failed} region />
      ) : (
        <AdminTableScroll labelledBy={SUMMARY_CAPTION_ID}>
          <table className="jp-table jp-admintable">
            <caption id={SUMMARY_CAPTION_ID} className="sr-only">
              {t("caption", { days: query.window })}
            </caption>
            <thead>
              <tr>
                <th scope="col">{t("page")}</th>
                <th scope="col" className="jp-admintable__num">
                  {t("raters")}
                </th>
                {RATING_COLUMNS.map((rating) => (
                  <th key={rating} scope="col" className="jp-admintable__num">
                    <span aria-hidden="true">{rating}</span>
                    <span className="sr-only">{t("rated", { rating })}</span>
                  </th>
                ))}
                <th scope="col" className="jp-admintable__num">
                  {t("mean")}
                </th>
                <th scope="col" className="jp-admintable__num">
                  {t("submissions")}
                </th>
              </tr>
            </thead>
            <tbody>
              {region.data.map((row) => (
                <tr key={row.page}>
                  <td>
                    {isFeedbackPageKey(row.page) ? (
                      <FeedbackFocusLink
                        href={feedbackHref(basePath, withoutId(withPage(query, row.page)))}
                        focusTo="scope"
                        className="jp-adminfeedback__pagelink jp-adminfeedback__textlink"
                        aria-current={row.page === query.page ? "true" : undefined}
                      >
                        {pageLabel(row.page)}
                      </FeedbackFocusLink>
                    ) : (
                      row.page
                    )}
                  </td>
                  <td className="jp-admintable__num">{format.number(row.raters)}</td>
                  {row.ratings.map((ratingCount, index) => (
                    // The five columns are fixed, so their position is their identity.
                    <td key={index} className="jp-admintable__num">
                      {format.number(ratingCount)}
                    </td>
                  ))}
                  <td className="jp-admintable__num">
                    {row.mean === null ? (
                      <AdminUnknown />
                    ) : (
                      format.number(row.mean, { minimumFractionDigits: 1, maximumFractionDigits: 1 })
                    )}
                  </td>
                  <td className="jp-admintable__num">{format.number(row.submissions)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </AdminTableScroll>
      )}
    </section>
  );
}
