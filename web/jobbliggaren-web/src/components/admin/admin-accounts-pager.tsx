"use client";

// "use client": its buttons carry click handlers.
import { useTranslations } from "next-intl";
import { ArrowLeft, ArrowRight } from "lucide-react";

interface AdminAccountsPagerProps {
  readonly page: number;
  readonly pages: number;
  readonly onPage: (page: number) => void;
}

/** "Sida 1 av 3" with Föregående and Nästa; a page that does not exist is never offered. */
export function AdminAccountsPager({ page, pages, onPage }: AdminAccountsPagerProps) {
  const t = useTranslations("admin.users.pager");
  if (pages <= 1) return null;

  return (
    <nav className="jp-adminpager" aria-label={t("label")}>
      <p className="jp-adminpager__position">{t("position", { page, pages })}</p>
      <div className="jp-adminpager__buttons">
        <button
          type="button"
          className="jp-btn jp-btn--sm jp-btn--secondary"
          aria-disabled={page <= 1 || undefined}
          onClick={() => {
            if (page > 1) onPage(page - 1);
          }}
        >
          <ArrowLeft size={16} aria-hidden="true" />
          {t("previous")}
        </button>
        <button
          type="button"
          className="jp-btn jp-btn--sm jp-btn--secondary"
          aria-disabled={page >= pages || undefined}
          onClick={() => {
            if (page < pages) onPage(page + 1);
          }}
        >
          {t("next")}
          <ArrowRight size={16} aria-hidden="true" />
        </button>
      </div>
    </nav>
  );
}

/** "8 av 12 konton", announced as the filter or the search changes it; while newer rows load, it says so. */
export function AdminAccountsSummary({
  shown,
  total,
  busy = false,
}: {
  readonly shown: number;
  readonly total: number;
  readonly busy?: boolean;
}) {
  const t = useTranslations("admin.users");
  return (
    <p className="jp-adminusers__summary" role="status">
      {busy ? t("counterBusy") : t("counter", { shown, total })}
    </p>
  );
}
