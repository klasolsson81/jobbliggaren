import { LogOut } from "lucide-react";
import { logoutAction } from "@/lib/auth/actions";

/**
 * Logga ut-kort. Server-action via form-element (samma mönster som
 * UserMenu i app-shell — single source of truth för logout-flödet).
 * Server component eftersom inget client-state behövs.
 */
export function LogoutCard() {
  return (
    <section className="jp-card">
      <h2 className="jp-card__title">Logga ut</h2>
      <p className="text-body-sm text-text-secondary">
        Avsluta denna session. Du kan logga in igen när som helst.
      </p>
      <form action={logoutAction} style={{ marginTop: 12 }}>
        <button type="submit" className="jp-btn jp-btn--secondary">
          <LogOut size={16} aria-hidden="true" />
          <span>Logga ut</span>
        </button>
      </form>
    </section>
  );
}
