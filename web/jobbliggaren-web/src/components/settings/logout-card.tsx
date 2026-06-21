import { LogOut } from "lucide-react";
import { useTranslations } from "next-intl";
import { logoutAction } from "@/lib/auth/actions";

/**
 * Logga ut-kort. Server-action via form-element (samma mönster som
 * UserMenu i app-shell — single source of truth för logout-flödet).
 * Server component eftersom inget client-state behövs; `useTranslations`
 * resolverar synkront i en sync server component (next-intl v4).
 */
export function LogoutCard() {
  const t = useTranslations("settings");
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{t("logout.title")}</h2>
      <p className="text-body-sm text-text-secondary">
        {t("logout.description")}
      </p>
      <form action={logoutAction} style={{ marginTop: 12 }}>
        <button type="submit" className="jp-btn jp-btn--secondary">
          <LogOut size={16} aria-hidden="true" />
          <span>{t("logout.action")}</span>
        </button>
      </form>
    </section>
  );
}
