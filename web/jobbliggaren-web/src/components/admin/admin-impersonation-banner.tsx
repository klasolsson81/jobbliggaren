import { useTranslations } from "next-intl";
import { UserCog } from "lucide-react";

/**
 * The banner an administrator acting as an account sees on every page (handoff 13). Impersonation
 * is #1984, so only the preview renders this today.
 */
export function AdminImpersonationBanner({
  email,
  onEnd,
}: {
  readonly email: string;
  readonly onEnd: () => void;
}) {
  const t = useTranslations("admin.impersonation");
  return (
    <section className="jp-adminbanner" aria-label={t("label")}>
      <div className="jp-adminbanner__inner">
        <UserCog size={20} aria-hidden="true" />
        <p className="jp-adminbanner__text">{t("text", { email })}</p>
        <button type="button" className="jp-btn jp-btn--sm jp-btn--secondary jp-adminbanner__end" onClick={onEnd}>
          {t("end")}
        </button>
      </div>
    </section>
  );
}
