import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import type { ActionResult } from "@/lib/actions/_action-result";
import {
  isJuniorRuleId,
  JUNIOR_TABS,
  juniorChoices,
  juniorJobsHref,
  withPage,
  withTab,
  type AdminJuniorChoice,
  type AdminJuniorJobRow,
  type AdminJuniorJobsPage,
  type AdminJuniorJobsQuery,
} from "@/lib/admin/junior-jobs";
import type { AdminValueRegion } from "@/lib/admin/view-models";
import { formatDate } from "@/lib/i18n/format";
import { AdminJuniorJobsList, type AdminJuniorPagerView, type AdminJuniorRowView } from "./admin-junior-jobs-list";
import { AdminPageHeader } from "./admin-page-header";
import { AdminRegionLine } from "./admin-region-line";

export interface AdminJuniorJobsViewProps {
  /** "/admin/juniorjobb", or the local preview's own route (ADR 0150 D5). */
  readonly basePath: string;
  /** What the URL asks for; every link keeps it and changes one part. */
  readonly query: AdminJuniorJobsQuery;
  /** A tab that answered with no ads is loaded and empty: its counts are still known. */
  readonly region: AdminValueRegion<AdminJuniorJobsPage>;
  /** A refused read's own line; without one a failed read shows the shared line. */
  readonly failed?: string;
  readonly onDecide: (id: string, decision: AdminJuniorChoice) => Promise<ActionResult>;
}

const HEADING_ID = "admin-junior-heading";
const LIST_HEADING_ID = "admin-junior-list";

/**
 * The admin junior queue (#2264, ADR 0187): the listed Data/IT ads the rules left unclear, the ones on /junior
 * now, and the ones an administrator has decided, one tab each, every tab and page a link. A row shows what the ad
 * itself shows and which rule placed it, never the ad's text, and the ad opens on its own page to be read.
 *
 * No directive: on the live page the header, the tabs and the rows' words render on the server, and only the list,
 * which runs the decisions and moves focus, is a client island. The preview renders it on the client.
 */
export function AdminJuniorJobsView({ basePath, query, region, failed, onDecide }: AdminJuniorJobsViewProps) {
  const t = useTranslations("admin.juniorJobs");

  return (
    <>
      <AdminPageHeader title={t("heading")} headingId={HEADING_ID} />
      <div className="jp-adminjunior">
        <JuniorTabs basePath={basePath} query={query} region={region} />
        <section aria-labelledby={LIST_HEADING_ID}>
          <h2 id={LIST_HEADING_ID} className="sr-only">
            {t(`tabs.${query.tab}`)}
          </h2>
          <JuniorQueue basePath={basePath} query={query} region={region} failed={failed} onDecide={onDecide} />
        </section>
      </div>
    </>
  );
}

/**
 * One link per tab, the house `.jp-subnav`: each tab is its own URL. A count is shown only when the queue answered,
 * so an unknown count is never "(0)" (ADR 0150 D2).
 */
function JuniorTabs({
  basePath,
  query,
  region,
}: {
  readonly basePath: string;
  readonly query: AdminJuniorJobsQuery;
  readonly region: AdminValueRegion<AdminJuniorJobsPage>;
}) {
  const t = useTranslations("admin.juniorJobs.tabs");
  if (region.kind === "unavailable") return null;
  const counts = region.kind === "loaded" ? region.data.counts : null;

  return (
    <nav className="jp-subnav" aria-label={t("label")}>
      {JUNIOR_TABS.map((tab) => {
        const active = tab === query.tab;
        const label = t(tab);
        return (
          <Link
            key={tab}
            href={juniorJobsHref(basePath, withTab(tab))}
            className="jp-subnav__item"
            data-active={active}
            aria-current={active ? "true" : undefined}
          >
            {counts === null ? label : t("count", { label, count: counts[tab] })}
          </Link>
        );
      })}
    </nav>
  );
}

function JuniorQueue({
  basePath,
  query,
  region,
  failed,
  onDecide,
}: {
  readonly basePath: string;
  readonly query: AdminJuniorJobsQuery;
  readonly region: AdminValueRegion<AdminJuniorJobsPage>;
  readonly failed: string | undefined;
  readonly onDecide: AdminJuniorJobsViewProps["onDecide"];
}) {
  const t = useTranslations("admin.juniorJobs");
  const pagerCopy = useTranslations("admin.users.pager");
  const format = useFormatter();
  if (region.kind !== "loaded") return <AdminRegionLine kind={region.kind} failed={failed} region />;

  const { items, page, totalPages, totalCount } = region.data;

  // A page number past the last page: the ads exist, this page of them does not.
  const empty =
    totalCount > 0 ? (
      <p className="jp-adminsoon jp-adminsoon--region">
        {t("list.pageMissing")}{" "}
        <Link href={juniorJobsHref(basePath, withPage(query, 1))} className="jp-adminjunior__textlink">
          {t("list.firstPage")}
        </Link>
      </p>
    ) : (
      <AdminRegionLine kind="empty" empty={t(`list.empty.${query.tab}`)} region />
    );

  const ruleLabel = (ruleId: string) => (isJuniorRuleId(ruleId) ? t(`rules.${ruleId}`) : t("unknownRule"));
  const choiceLabel = (choice: AdminJuniorChoice) => t(`choices.${choice ?? "release"}`);

  const rows = items.map(
    (row: AdminJuniorJobRow): AdminJuniorRowView => ({
      id: row.id,
      title: row.title,
      company: row.company,
      municipality: row.municipality,
      published: formatDate(format, row.publishedAt),
      expires: formatDate(format, row.expiresAt),
      reason: ruleLabel(row.ruleId),
      decided:
        row.decision === null
          ? null
          : {
              rules: t("row.rulesValue", { outcome: t(`outcomes.${row.verdict}`), rule: ruleLabel(row.ruleId) }),
              decision: t(`outcomes.${row.decision}`),
            },
      choices: juniorChoices(query.tab, row.decision).map((choice) => ({
        decision: choice,
        label: choiceLabel(choice),
        receipt: t(`receipts.${choice ?? "release"}`, { title: row.title }),
      })),
    }),
  );

  const first = page <= 1;
  const last = page >= totalPages;
  const pager: AdminJuniorPagerView | null =
    items.length === 0 || totalPages <= 1
      ? null
      : {
          label: pagerCopy("label"),
          position: pagerCopy("position", { page, pages: totalPages }),
          previous: {
            href: juniorJobsHref(basePath, withPage(query, first ? page : page - 1)),
            label: pagerCopy("previous"),
            disabled: first,
          },
          next: {
            href: juniorJobsHref(basePath, withPage(query, last ? page : page + 1)),
            label: pagerCopy("next"),
            disabled: last,
          },
        };

  return (
    <AdminJuniorJobsList
      rows={rows}
      empty={empty}
      pager={pager}
      labels={{
        municipality: t("row.municipality"),
        published: t("row.published"),
        expires: t("row.expires"),
        reason: t("row.reason"),
        rules: t("row.rules"),
        decision: t("row.decision"),
        busy: t("choices.busy"),
      }}
      headingId={HEADING_ID}
      onDecide={onDecide}
    />
  );
}
