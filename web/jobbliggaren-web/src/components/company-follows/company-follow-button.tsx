"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Building2 } from "lucide-react";
import {
  followCompanyAction,
  unfollowCompanyAction,
} from "@/lib/actions/company-follows";

interface CompanyFollowButtonProps {
  /** The company's unmasked org.nr (the follow key). Masked/sole-prop rows never render this button. */
  orgNr: string;
  /** The company name — used to give each row's button a DISTINCT accessible name (WCAG 2.4.4). */
  companyName: string;
  /**
   * Server-fetched CompanyWatchId, or null when the user does not follow this company. Present ⇒ the user
   * follows it (rendered "Bevakar"); the id is used to unfollow by opaque id (D8(c)).
   */
  initialCompanyWatchId: string | null;
}

/**
 * #560 PR-C (ADR 0087 D8(c)) — the per-row "Bevaka" toggle on the /foretag/sok register-search results.
 * The org.nr-keyed sibling of {@link FollowCompanyToggle} (which is jobAdId-keyed): here the search row
 * already carries the unmasked org.nr, so it follows directly via `followCompanyAction(orgNr)`.
 *
 * <para>Parity FollowCompanyToggle: optimistic + rollback, NEVER `disabled` (Klas PR5 — the backend is
 * idempotent so a double-click is race-safe); pending shows via subtle opacity; `jp-btn--secondary`. The
 * visible label is compact ("Bevaka"/"Bevakar") — the company name sits in the same row — but the
 * accessible name interpolates the company so 20 buttons in a table are individually distinguishable
 * (WCAG 2.4.4). Crucially the accessible name LEADS WITH the visible label word ("Bevaka {company}" /
 * "Bevakar {company}") so it CONTAINS the visible text (WCAG 2.5.3 label-in-name — a speech-input user
 * saying "Bevakar" reaches the button); the toggle state rides `aria-pressed`, never a divergent
 * action verb in the name.</para>
 */
export function CompanyFollowButton({
  orgNr,
  companyName,
  initialCompanyWatchId,
}: CompanyFollowButtonProps) {
  const t = useTranslations("pages.foretag.criteria.browse");
  const [companyWatchId, setCompanyWatchId] = useState<string | null>(
    initialCompanyWatchId
  );
  // Optimistic "follow in flight" — the real CompanyWatchId only arrives on success, so a boolean carries
  // the immediate visual while the id resolves (unfollow needs the id, so it flips companyWatchId).
  const [pendingFollow, setPendingFollow] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const following = pendingFollow || companyWatchId !== null;

  function handleClick() {
    setError(null);

    if (companyWatchId !== null) {
      // Unfollow (optimistic): flip immediately, roll back on failure.
      const previousId = companyWatchId;
      setCompanyWatchId(null);
      startTransition(async () => {
        const result = await unfollowCompanyAction(previousId);
        if (!result.success) {
          setCompanyWatchId(previousId);
          setError(result.error);
        }
      });
      return;
    }

    if (pendingFollow) return; // a follow is already resolving its id

    // Follow (optimistic via pendingFollow): resolve the id on success, clear the flag either way.
    setPendingFollow(true);
    startTransition(async () => {
      const result = await followCompanyAction(orgNr);
      if (result.success) {
        setCompanyWatchId(result.companyWatchId);
      } else {
        setError(result.error);
      }
      setPendingFollow(false);
    });
  }

  const label = following ? t("following") : t("follow");
  // Accessible name leads with the visible label word so it contains the visible text (WCAG 2.5.3);
  // the company keeps each row's button distinct (WCAG 2.4.4); state rides aria-pressed.
  const ariaLabel = following
    ? t("followingAria", { company: companyName })
    : t("followAria", { company: companyName });
  const opacity = isPending ? 0.7 : 1;

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className="jp-btn jp-btn--secondary"
        aria-label={ariaLabel}
        aria-pressed={following}
        onClick={handleClick}
        style={{ opacity }}
      >
        <Building2
          size={14}
          aria-hidden="true"
          className={following ? "text-success-600" : undefined}
        />{" "}
        {label}
      </button>
      {error && (
        <span role="alert" className="text-micro text-danger-700">
          {error}
        </span>
      )}
    </div>
  );
}
