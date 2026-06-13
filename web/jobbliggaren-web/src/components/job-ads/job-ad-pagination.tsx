import Link from "next/link";

interface JobAdPaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  buildHref: (targetPage: number) => string;
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
}: JobAdPaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (totalPages <= 1) return null;

  const items = buildPageItems(page, totalPages);

  return (
    <nav
      aria-label="Paginering"
      className="flex flex-col gap-3 border-t border-border pt-4"
    >
      <ol className="flex flex-wrap items-center gap-1">
        {page > 1 && (
          <li>
            <Link
              href={buildHref(page - 1)}
              rel="prev"
              className="inline-flex items-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary"
            >
              Föregående
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
                className="inline-flex min-w-[2.5rem] items-center justify-center rounded-md border border-brand-700 bg-brand-50 px-3 py-2 text-body-sm font-medium text-brand-700"
              >
                <span className="sr-only">Sida </span>
                {item}
              </span>
            </li>
          ) : (
            <li key={item}>
              <Link
                href={buildHref(item)}
                className="inline-flex min-w-[2.5rem] items-center justify-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary"
              >
                <span className="sr-only">Sida </span>
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
              className="inline-flex items-center rounded-md border border-border bg-card px-3 py-2 text-body-sm text-text-primary hover:bg-surface-secondary"
            >
              Nästa
            </Link>
          </li>
        )}
      </ol>
      <p className="text-body-sm text-text-secondary">
        Sida {page} av {totalPages} ({totalCount} träffar totalt)
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
