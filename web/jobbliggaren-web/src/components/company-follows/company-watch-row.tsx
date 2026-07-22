"use client";

import { useId, useState, useTransition } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { Filter, ShieldAlert, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import { formatOrgNr } from "@/lib/company-follows/org-nr";
import { unfollowCompanyAction } from "@/lib/actions/company-follows";
import type { CompanyWatch } from "@/lib/dto/company-follows";
import type { TaxonomyRegion } from "@/lib/dto/taxonomy";
import { InfoDialog } from "@/components/common/info-dialog";
import { WatchFilterDialog } from "./watch-filter-dialog";

/**
 * #452 — which per-company count the hub toggle is emphasising. `matching` (default) leads with the
 * "X matchande annonser" signal (or the not-assessed nudge); `all` leads with the public active-ad
 * count. The active-ad count is always kept as a secondary fact regardless of mode (#447/#448).
 */
export type CompanyWatchViewMode = "matching" | "all";

// #452 — the canonical route the "set up matching" nudge links to, kept as a repeated inline literal
// per the established pattern (JobAdMatchSection, oversikt-page, jobb-results-toolbar all inline it).
const MATCH_SETTINGS_HREF = "/installningar#matchning";

// The primary per-company matching line sits between the title and the meta row. Token-styled inline
// (no new globals.css rule): primary ink for high contrast (never gray, per design), sized/weighted
// like `.jp-job__company`. Both themes resolve via the same `--jp-*` tokens.

interface CompanyWatchRowProps {
  item: CompanyWatch;
  mode: CompanyWatchViewMode;
  /** Taxonomins län (med kommuner) för filter-dialogens ort-picker. Tom lista → picker degraderar civilt. */
  regions: ReadonlyArray<TaxonomyRegion>;
}

/**
 * #311 #448 (ADR 0087 D2/D8(c)) — one followed-company row on `/foretag`. Identifies the employer by
 * `companyName` (public Platsbanken data resolved server-side). org.nr is rendered ONLY when the
 * backend supplied it (`!isProtectedIdentity && organizationNumber` — a legal-entity number); a
 * personnummer-shaped org.nr arrives masked (`organizationNumber` null, `isProtectedIdentity` true)
 * and is shown as a "skyddad identitet" note, never a raw number. `activeAdCount` is public open-role
 * data (#447), surfaced even when the org.nr is masked.
 *
 * <para>#452 — the primary per-company signal follows `mode`: in `matching` mode the row leads with the
 * "X matchande annonser" count (ads of this employer matching the user's profile at grade >= Good),
 * or an honest not-assessed nudge when `matchingAdCount` is null (the user stated no occupation — never
 * a false "0", parity /jobb + /matchningar). In `all` mode that line is hidden. The public
 * "X aktiva annonser just nu" count (#447) is always kept as a secondary fact. The matching count is a
 * count of ADS over a named grade threshold — never rendered as a score, percentage, or meter
 * (Goodhart, ADR 0071).</para>
 *
 * <para>Unfollow: server action + `revalidatePath` drives the row removal (CTO Q4 2026-07-01 — server
 * state over a client-side optimistic copy, §5). `useTransition` covers the DELETE latency
 * (`aria-busy`); parity with FollowCompanyToggle the button is never `disabled` (the backend is
 * idempotent, so a mis-click is recoverable). On failure the row stays and shows the error inline.</para>
 *
 * <para><b>Bevakning F4b — the RESTING-state filter disclosure (BC-9′) is load-bearing, not polish.</b>
 * An active filter narrows this watch's notifications AND the Översikt "nya annonser"-count, while the
 * row's own numbers stay deliberately filter-UNaware (RF-8 — they answer a different question). Worse:
 * when every watch suppresses everything, no digest email is sent at all, so the email cannot disclose
 * anything either — silence is indistinguishable from "nothing was published". This row is therefore the
 * ONLY surface that can carry the transparency guarantee in that case, which is why the disclosure must
 * be visible WITHOUT opening anything. It names the axes, and counts the orter rather than listing them
 * (a whole-län pick can cover ~49 kommuner; the names live one click away, in the editor).</para>
 */
export function CompanyWatchRow({ item, mode, regions }: CompanyWatchRowProps) {
  const t = useTranslations("jobads.companyWatches");
  const format = useFormatter();
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();
  const [filterOpen, setFilterOpen] = useState(false);
  const hintId = useId();

  const displayName = item.companyName ?? t("unknownCompany");
  const followedSince = formatDate(format, item.followedAt);

  // Antalet valda ORTER är summan av de två axlarna: ett helt-läns-val är ETT val (och lagras som ett
  // läns-id), inte länets alla kommuner. Att räkna dem var för sig vore att ljuga om vad användaren valt.
  const ortCount = item.filter
    ? item.filter.municipalities.length + item.filter.regions.length
    : 0;
  const onlyMatchedActive = item.filter?.onlyMatched ?? false;

  // Frånvaro = inget filter. Ingen "Inget filter"-rad, ingen tom chip.
  const filterLine = !item.filter
    ? null
    : onlyMatchedActive && ortCount > 0
      ? t("filter.activeBoth", { count: ortCount })
      : onlyMatchedActive
        ? t("filter.activeOnlyMatched")
        : t("filter.activeOrter", { count: ortCount });

  function handleUnfollow() {
    setError(null);
    startTransition(async () => {
      const result = await unfollowCompanyAction(item.id);
      // On success `revalidatePath("/foretag/bevakade")` re-renders the RSC list without this row.
      if (!result.success) setError(result.error);
    });
  }

  return (
    <li>
      {/* `jp-job--static`: raden bär ett chassi som delas med /jobb, där kortet ÄR klickbart. Här är det
          bara knapparna som är det — med en andra knapp i raden blir en falsk klick-affordans (pekare +
          hover-accentkant) aktivt vilseledande, så modifiern tar bort den. /jobb rörs inte. */}
      <article
        className="jp-job jp-job--static"
        style={{ gridTemplateColumns: "1fr auto" }}
      >
        <div className="jp-job__body">
          <h3 className="jp-job__title">{displayName}</h3>
          {mode === "matching" &&
            (item.matchingAdCount === null ? (
              // Honest not-assessed: the user stated no occupation, so matching is undefined. Render a
              // civic nudge to state occupations, never a false "0" (parity /jobb + /matchningar). Copy
              // + link style mirror the JobAdMatchSection not-assessed signpost (SPOT, no drift).
              <p className="jp-matchline">
                {t("matchNudge")}{" "}
                <Link href={MATCH_SETTINGS_HREF} className="jp-nudgelink">
                  {t("matchNudgeCta")}
                </Link>
              </p>
            ) : (
              // A count of ADS over a named grade threshold (>= Good) — high-contrast primary ink,
              // tabular-nums for stable digits (#448), NEVER a score/percentage/meter (ADR 0071).
              <p className="jp-matchline tabular-nums">
                {t("matchingAds", { count: item.matchingAdCount })}
              </p>
            ))}
          <div className="jp-job__meta">
            {item.isProtectedIdentity ? (
              <>
                <span aria-describedby={hintId}>
                  <ShieldAlert size={14} aria-hidden="true" /> {t("protectedIdentity")}
                </span>
                {/* The reason the org.nr is hidden, reachable by screen readers
                    (a non-focusable `title` is not) — keeps the meta visually compact. */}
                <span id={hintId} className="sr-only">
                  {t("protectedIdentityHint")}
                </span>
              </>
            ) : (
              // Backend (ADR 0087 D8(c)) is the SINGLE authoritative personnummer guard: a
              // personnummer-shaped sole-prop org.nr arrives as organizationNumber=null +
              // isProtectedIdentity=true, so this branch only ever sees a legal-entity number. The FE
              // renders on that contract and adds NO shape-heuristic of its own — D8 rejected a
              // FE-layer heuristic (imperfect, wrong layer); the regression tripwire is the build-time
              // OrganizationNumberSurfacingGuardTests, not a runtime FE check (senior-cto-advisor 2026-07-01).
              item.organizationNumber && (
                <span>{t("orgNr", { orgNr: formatOrgNr(item.organizationNumber) })}</span>
              )
            )}
            <span className="tabular-nums">
              {t("activeAds", { count: item.activeAdCount })}
            </span>
            {followedSince && <span>{t("followedSince", { date: followedSince })}</span>}
          </div>
          {/* BC-9′ — the resting-state disclosure. Visible without opening anything, because it is the
              only surface that can tell the user their notifications are narrowed when no email is sent
              at all. The InfoDialog is a SIBLING of the text (never a child of a control) and explains
              the one thing this line cannot: that the row's COUNTS are not filter-aware. */}
          {filterLine && (
            <p className="jp-transparency-note jp-transparency-note--compact jp-transparency-note--inline-control mt-2">
              <Filter size={14} aria-hidden="true" />
              {filterLine}
              <InfoDialog
                title={t("filter.scopeHelpTitle")}
                paragraphs={[
                  t("filter.scopeHelpBody1"),
                  t("filter.scopeHelpBody2"),
                ]}
                ariaLabel={t("filter.scopeHelpAria", { company: displayName })}
              />
            </p>
          )}
          {error && (
            <p role="alert" className="mt-2 text-body-sm text-danger-700">
              {error}
            </p>
          )}
        </div>
        <div
          className="jp-job__actions"
          style={{ flexDirection: "row", alignItems: "center" }}
        >
          {/* Text, aldrig icon-only: en ikon-tratt är en gåta i en civic-utility. Det tillgängliga
              namnet bär företaget, annars hör en skärmläsar-användare "Filtrera" N gånger utan kontext
              (och den synliga etiketten ingår i namnet — WCAG 2.5.3). */}
          <button
            type="button"
            className="jp-rowbtn"
            aria-label={t("filter.openAria", { company: displayName })}
            onClick={() => setFilterOpen(true)}
          >
            {t("filter.open")}
          </button>
          {/* Destruktiv åtgärd sist. */}
          <button
            type="button"
            className="jp-icon-btn"
            aria-label={t("unfollowAria", { company: displayName })}
            aria-busy={isPending || undefined}
            onClick={handleUnfollow}
            style={isPending ? { opacity: 0.6 } : undefined}
          >
            <Trash2 size={16} aria-hidden="true" />
          </button>
        </div>
      </article>

      {/* Monteras bara när den öppnas, och `key` på det persisterade filtret monterar om den efter en
          save — draften kan därför aldrig visa ett inaktuellt värde. */}
      {filterOpen && (
        <WatchFilterDialog
          key={JSON.stringify(item.filter)}
          open={filterOpen}
          onOpenChange={setFilterOpen}
          companyWatchId={item.id}
          companyName={displayName}
          filter={item.filter}
          regions={regions}
          // Samma diskriminator som radens nudge (SPOT): null = användaren har inte angett något yrke,
          // så "matchande" är odefinierat — aldrig en falsk 0.
          matchingNotAssessed={item.matchingAdCount === null}
        />
      )}
    </li>
  );
}
