"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Building2 } from "lucide-react";
import {
  followCompanyFromJobAdAction,
  unfollowCompanyAction,
} from "@/lib/actions/company-follows";

interface FollowCompanyToggleProps {
  /** The ad whose employer is followed. The org.nr is resolved server-side (never on the wire). */
  jobAdId: string;
  /**
   * Server-fetched CompanyWatchId, or null when the user does not follow this employer. Present ⇒ the
   * user follows it (rendered as "Bevakar"); the id is used to unfollow by opaque id (D8(c)).
   */
  initialCompanyWatchId: string | null;
}

/**
 * #311 #455 (ADR 0087 D3/D8(c)) — "Bevaka företaget" toggle in the job-ad detail footer (ADR
 * 0053), alongside Spara / Har-ansökt. Keyed by JobAdId: following resolves the employer org.nr
 * server-side (it never crosses the wire — a sole-prop org.nr can be a personnummer); unfollowing
 * addresses the opaque CompanyWatchId returned by the follow.
 *
 * <para>Parity SaveJobAdToggle: optimistic + rollback, NEVER `disabled` (Klas PR5 — undo without waiting
 * on the pending action; the backend is idempotent so a double-click is race-safe). Pending shows via
 * subtle opacity. `jp-btn--secondary` (no competing CTA hierarchy). Following ≠ notification consent —
 * that is a separate opt-in in settings (ADR 0087 D5, out of #455 scope).</para>
 */
export function FollowCompanyToggle({
  jobAdId,
  initialCompanyWatchId,
}: FollowCompanyToggleProps) {
  const t = useTranslations("jobads.follow");
  const [companyWatchId, setCompanyWatchId] = useState<string | null>(
    initialCompanyWatchId
  );
  // Optimistic "follow in flight" — the real CompanyWatchId only arrives on success, so a boolean
  // carries the immediate visual while the id resolves (unfollow needs the id, so it flips companyWatchId).
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
      const result = await followCompanyFromJobAdAction(jobAdId);
      if (result.success) {
        setCompanyWatchId(result.companyWatchId);
      } else {
        setError(result.error);
      }
      setPendingFollow(false);
    });
  }

  const label = following ? t("following") : t("follow");
  const opacity = isPending ? 0.7 : 1;

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className="jp-btn jp-btn--secondary"
        // No aria-label override: the accessible name is the visible text ("Bevaka företaget" /
        // "Bevakar företaget") so it always contains the visible label (WCAG 2.5.3); the toggle state
        // rides aria-pressed, never a divergent action verb ("Sluta bevaka…") that would break 2.5.3.
        aria-pressed={following}
        onClick={handleClick}
        style={{ opacity }}
      >
        {/* #1000 (V1) — NO green tint on the followed icon: green is reserved for match-grade +
            interaction accent (ADR 0068), so a green follow-icon mis-read as a grade/success. The
            follow STATE is carried by the aria-pressed button + the header BEVAKAR tag (--jp-follow),
            not a colour on the icon. */}
        <Building2 size={14} aria-hidden="true" />{" "}
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
