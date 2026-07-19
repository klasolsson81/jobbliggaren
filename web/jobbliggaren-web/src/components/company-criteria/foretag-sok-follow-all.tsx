"use client";

// "use client": the CTA holds a useTransition around the create action plus a saved/error result
// state, none of which runs in a Server Component. It acts on the ACTIVE (URL-applied) filter passed
// as props — never the draft in ForetagSokFilters — so "follow all matches" always means what is on
// screen. The page keys this component on the filter signature, so a filter change remounts it and
// clears any prior saved/error state.

import { useId, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { createCriterionAction } from "@/lib/actions/company-criteria";
import {
  evaluateFollowAllGate,
  type FollowAllBlockedKind,
} from "@/lib/company-search/follow-all-gate";

interface ForetagSokFollowAllProps {
  /** The active filter axes, parsed from the URL (not the draft). */
  readonly namn: string;
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
}

/**
 * Map each non-ready gate kind to its i18n explainer key. `as const satisfies` keeps the values as
 * literal keys (not widened to `string`) so next-intl's typed `t()` accepts them, while `satisfies`
 * still enforces exhaustive coverage of every blocked kind at compile time.
 */
const REASON_KEY = {
  nameTerm: "reasonName",
  empty: "reasonEmpty",
  sniMissing: "reasonSni",
  kommunMissing: "reasonKommun",
} as const satisfies Record<FollowAllBlockedKind, string>;

/**
 * #560 PR-D — "Bevaka alla träffar": save the active `/foretag/sok` filter as a criterion watch
 * (pure reuse of `createCriterionAction` / `CreateCompanyWatchCriterionCommand` — no new domain, no
 * new command). The CTA is disabled-with-explainer whenever the filter is not criterion-shaped
 * (SNI ∧ kommun ∧ no name term — CTO F4). The button stays FOCUSABLE while blocked (`aria-disabled`,
 * not the native `disabled`) so a keyboard/screen-reader user can reach it and hear WHY it is blocked
 * via its `aria-describedby` explainer — the four-state honesty of the criterion dialog's preview
 * line, applied here.
 *
 * On success the button is replaced by a confirmation + a link to the hub: the create handler does
 * NOT dedupe criteria (racing/repeat creates are an accepted cosmetic overshoot), so removing the
 * button prevents an accidental duplicate from a second click on the same filter. The criteria LIST
 * stays on `/foretag` (this page only creates; the action revalidates `/foretag`).
 */
export function ForetagSokFollowAll({ namn, sni, kommun }: ForetagSokFollowAllProps) {
  const t = useTranslations("pages.foretag.sok.followAll");
  const headingId = useId();
  const explainerId = useId();
  const [isSaving, startSaving] = useTransition();
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const gate = evaluateFollowAllGate(namn, sni, kommun);
  const ready = gate.kind === "ready";
  const blocked = !ready || isSaving;

  function handleSave() {
    // Guard: aria-disabled (not native disabled) keeps the button clickable, so the guard is the
    // real barrier against a save on a non-criterion-shaped filter or a double-submit.
    if (!ready || isSaving) return;
    setError(null);
    startSaving(async () => {
      const result = await createCriterionAction({
        sniCodes: [...sni],
        municipalityCodes: [...kommun],
      });
      if (result.success) {
        setSaved(true);
      } else {
        setError(result.error);
      }
    });
  }

  return (
    <section
      aria-labelledby={headingId}
      className="flex flex-col gap-3 rounded-md border border-border p-4 md:p-6"
    >
      <h2 id={headingId} className="text-h3 font-semibold text-text-primary">
        {t("heading")}
      </h2>

      {saved ? (
        <p role="status" className="text-body-sm text-text-primary">
          {t("successBody")}{" "}
          <Link href="/foretag" className="text-brand-700 underline underline-offset-2">
            {t("successLink")}
          </Link>
        </p>
      ) : (
        <>
          <p id={explainerId} className="text-body-sm text-text-primary">
            {ready ? t("ready") : t(REASON_KEY[gate.kind])}
          </p>
          <div>
            <button
              type="button"
              className="jp-btn jp-btn--primary"
              onClick={handleSave}
              aria-disabled={blocked || undefined}
              aria-describedby={explainerId}
              aria-busy={isSaving || undefined}
            >
              {isSaving ? t("saving") : t("button")}
            </button>
          </div>
          {error && (
            <p role="alert" className="text-body-sm text-danger-700">
              {error}
            </p>
          )}
        </>
      )}
    </section>
  );
}
