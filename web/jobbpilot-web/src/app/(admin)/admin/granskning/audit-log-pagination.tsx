import Link from "next/link";

interface AuditLogPaginationProps {
  page: number;
  totalPages: number;
  totalCount: number;
  buildHref: (targetPage: number) => string;
}

/**
 * Server-renderad paginering. Föregående/Nästa som <a>-länkar med URL-params
 * → backknapp i browsern fungerar, sökmotorindexering möjlig (även om vi inte
 * publicerar admin-yta i sitemap).
 */
export function AuditLogPagination({
  page,
  totalPages,
  totalCount,
  buildHref,
}: AuditLogPaginationProps) {
  const hasPrev = page > 1;
  const hasNext = page < totalPages;

  return (
    <nav
      aria-label="Sidnavigering"
      className="flex items-center justify-between gap-4 border-t border-border pt-4 text-body-sm"
    >
      <p className="text-text-secondary">
        Sida {page} av {Math.max(totalPages, 1)} · {totalCount} poster totalt
      </p>
      <div className="flex items-center gap-2">
        {hasPrev ? (
          <Link
            href={buildHref(page - 1)}
            className="rounded-md border border-border bg-background px-3 py-1.5 text-text-primary hover:bg-surface-tertiary"
            rel="prev"
          >
            ← Föregående
          </Link>
        ) : (
          <span
            aria-disabled="true"
            className="rounded-md border border-border bg-surface-secondary px-3 py-1.5 text-text-tertiary"
          >
            ← Föregående
          </span>
        )}
        {hasNext ? (
          <Link
            href={buildHref(page + 1)}
            className="rounded-md border border-border bg-background px-3 py-1.5 text-text-primary hover:bg-surface-tertiary"
            rel="next"
          >
            Nästa →
          </Link>
        ) : (
          <span
            aria-disabled="true"
            className="rounded-md border border-border bg-surface-secondary px-3 py-1.5 text-text-tertiary"
          >
            Nästa →
          </span>
        )}
      </div>
    </nav>
  );
}
