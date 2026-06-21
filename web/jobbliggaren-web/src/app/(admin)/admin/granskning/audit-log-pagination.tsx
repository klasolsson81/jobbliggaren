import Link from "next/link";
import { useTranslations } from "next-intl";

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
  // Synchronous next-intl translator — håller AuditLogPagination en icke-async RSC.
  const t = useTranslations("admin");
  const hasPrev = page > 1;
  const hasNext = page < totalPages;

  return (
    <nav
      aria-label={t("audit.pagination.navLabel")}
      className="flex items-center justify-between gap-4 border-t border-border pt-4 text-body-sm"
    >
      <p className="text-text-secondary">
        {t("audit.pagination.summary", {
          page,
          totalPages: Math.max(totalPages, 1),
          totalCount,
        })}
      </p>
      <div className="flex items-center gap-2">
        {hasPrev ? (
          <Link
            href={buildHref(page - 1)}
            className="rounded-md border border-border bg-background px-3 py-1.5 text-text-primary hover:bg-surface-tertiary"
            rel="prev"
          >
            {t("audit.pagination.previous")}
          </Link>
        ) : (
          <span
            aria-disabled="true"
            className="cursor-not-allowed rounded-md border border-border bg-surface-secondary px-3 py-1.5 text-text-secondary"
          >
            {t("audit.pagination.previous")}
          </span>
        )}
        {hasNext ? (
          <Link
            href={buildHref(page + 1)}
            className="rounded-md border border-border bg-background px-3 py-1.5 text-text-primary hover:bg-surface-tertiary"
            rel="next"
          >
            {t("audit.pagination.next")}
          </Link>
        ) : (
          <span
            aria-disabled="true"
            className="cursor-not-allowed rounded-md border border-border bg-surface-secondary px-3 py-1.5 text-text-secondary"
          >
            {t("audit.pagination.next")}
          </span>
        )}
      </div>
    </nav>
  );
}
