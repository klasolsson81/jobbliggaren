import Link from "next/link";
import { useTranslations } from "next-intl";

interface JobAdPaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  buildHref: (targetPage: number) => string;
  /**
   * Whether the summary line may state `totalCount` (ADR 0120 clause 4, #1149). Default TRUE —
   * `/jobb` counts a bounded set, so "N träffar totalt" is a true sentence there. (It is the only
   * caller that takes the default: `ApplicationsPager` reuses `buildPageItems` alone and renders
   * no summary line.)
   *
   * Pass FALSE where `totalCount` SATURATES at a servable cap: the register surfaces cap it at
   * `MaxServableRows` (2 000 at pageSize 20), so the word *totalt* turns a ceiling into a
   * completeness claim — measured 2026-08-01 as "Sida 1 av 100 (2000 träffar totalt)" against
   * 743 654 active companies. The page COUNT stays either way: `TotalPages ≤ MaxPage` holds by
   * construction, so it is a navigation quantity (how far you can go), not a claim about how many
   * rows exist. Those surfaces carry their honest number in their own magnitude line instead.
   */
  showTotalCount?: boolean;
}

/**
 * Numeric pagination i GOV.UK-stil. Visar första, sista, aktuell sida +
 * grannar samt ellipsis vid hopp. Civic-utility-konvention per CTO-rond
 * 2026-05-13 Q4 (vs prev/next-only eller infinite-scroll).
 *
 * A11y per jobbliggaren-design-a11y skill: `<nav aria-label="Paginering">`,
 * `aria-current="page"` på aktiv sida, dolda sr-only-etiketter på siffror,
 * fungerar med tangentbord (Link-element) och skärmläsare.
 */
export function JobAdPagination({
  page,
  pageSize,
  totalCount,
  buildHref,
  showTotalCount = true,
}: JobAdPaginationProps) {
  // Synchronous next-intl translator — keeps JobAdPagination a non-async RSC.
  const t = useTranslations("jobads.ui");
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (totalPages <= 1) return null;

  const items = buildPageItems(page, totalPages);

  // Touch floor (#1384, DESIGN.md §5) on every control below. The at-rule is written out
  // rather than `max-[768px]:`, which is EXCLUSIVE of 768, while every CSS home of this rule
  // uses `@media (max-width: 768px)`, which includes it. Tidying these back to the shorthand
  // reopens a gap at exactly 768px where the sort control bumps and this pager does not.
  return (
    <nav
      aria-label={t("pagination.navLabel")}
      className="flex flex-col gap-3 border-t border-border pt-4"
    >
      <ol className="flex flex-wrap items-center gap-1">
        {page > 1 && (
          <li>
            <Link
              href={buildHref(page - 1)}
              rel="prev"
              className="inline-flex items-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary [@media(max-width:768px)]:min-h-11"
            >
              {t("pagination.previous")}
            </Link>
          </li>
        )}
        {items.map((item, idx) =>
          item === "ellipsis" ? (
            <li
              key={`gap-${idx}`}
              aria-hidden="true"
              className="px-2 text-body-sm text-text-secondary"
            >
              …
            </li>
          ) : item === page ? (
            <li key={item}>
              <span
                aria-current="page"
                className="inline-flex min-w-[2.5rem] items-center justify-center rounded-md border border-brand-700 bg-brand-50 px-3 py-2 text-body-sm font-medium text-brand-700 [@media(max-width:768px)]:min-h-11 [@media(max-width:768px)]:min-w-11"
              >
                <span className="sr-only">{t("pagination.pagePrefix")}</span>
                {item}
              </span>
            </li>
          ) : (
            <li key={item}>
              <Link
                href={buildHref(item)}
                className="inline-flex min-w-[2.5rem] items-center justify-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary [@media(max-width:768px)]:min-h-11 [@media(max-width:768px)]:min-w-11"
              >
                <span className="sr-only">{t("pagination.pagePrefix")}</span>
                {item}
              </Link>
            </li>
          )
        )}
        {page < totalPages && (
          <li>
            <Link
              href={buildHref(page + 1)}
              rel="next"
              className="inline-flex items-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary [@media(max-width:768px)]:min-h-11"
            >
              {t("pagination.next")}
            </Link>
          </li>
        )}
      </ol>
      <p className="text-body-sm text-text-secondary">
        {showTotalCount
          ? t("pagination.summary", { page, totalPages, totalCount })
          : t("pagination.summaryPagesOnly", { page, totalPages })}
      </p>
    </nav>
  );
}

type PageItem = number | "ellipsis";

/**
 * GOV.UK Pagination-pattern: visa första + sista + aktuell ± 1, ellipsis
 * vid hopp. Vid totalPages <= 7 visas alla siffror utan ellipsis.
 */
export function buildPageItems(current: number, totalPages: number): PageItem[] {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  const items: PageItem[] = [1];
  const start = Math.max(2, current - 1);
  const end = Math.min(totalPages - 1, current + 1);

  if (start > 2) items.push("ellipsis");
  for (let i = start; i <= end; i++) items.push(i);
  if (end < totalPages - 1) items.push("ellipsis");

  items.push(totalPages);
  return items;
}
