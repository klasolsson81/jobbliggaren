"use client";

import { useId, useState, useTransition } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { Filter, Info, ShieldAlert, Trash2 } from "lucide-react";
import { formatDate } from "@/lib/i18n/format";
import {
  buildCompanyJobsHref,
  isLinkableOrgNr,
} from "@/lib/job-ads/company-jobs-href";
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
  // Distans är den tredje granulariteten på samma axel och "räknas som en egen ort" —
  // ordagrant ur strängen dialogen själv renderar (matchPrefs.cascade.distansHint).
  // Utan den termen visades ett distans-only-filter som "Filtrerat: 0 orter": ett
  // filter som finns, beskrivet som noll orter.
  const ortCount = item.filter
    ? item.filter.municipalities.length
      + item.filter.regions.length
      + (item.filter.remote ? 1 : 0)
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

  // #1547 — the app's only originator of an `?employer=` value.
  //
  // `!isProtectedIdentity` is the gate `search-params.ts` describes as guarding an empty set, and
  // it guards a real set again here. `organizationNumber === null` covers two rows the FE SCHEMA
  // cannot tell apart — a masked sole-prop, and a BRAND_GROUP watch whose counts are summed over
  // member org.nrs. The backend DTO does carry `targetType` and `brandGroupId`; it is
  // `companyWatchSchema` that stops short, which is what #1566 owns.
  //
  // `isLinkableOrgNr` is shared with the href builder so this gate and that one read ONE value.
  // Without it the two could disagree — the row asking "is the field non-null", the builder
  // asking "is it ten digits" — and a row could carry a count with neither a link nor the note
  // that explains its absence. The state is contract-impossible (`OrganizationNumber.Create`
  // enforces the shape; the one other on-wire form is an HMAC token the handler masks), which is
  // why closing it by construction costs nothing rather than why it can be skipped.
  const linkableOrgNr =
    !item.isProtectedIdentity &&
    item.organizationNumber &&
    isLinkableOrgNr(item.organizationNumber)
      ? item.organizationNumber
      : null;

  // A count of 0 is a negation, not a number ("Inga aktiva annonser just nu"), so it gets no link:
  // an offer to open an empty list beside a sentence saying the list is empty contradicts itself.
  // The two gates are independent — 136 active with 0 matching is an ordinary row.
  const activeAdsHref =
    linkableOrgNr !== null && item.activeAdCount > 0
      ? buildCompanyJobsHref(linkableOrgNr, "all")
      : null;
  const matchingAdsHref =
    linkableOrgNr !== null && item.matchingAdCount !== null && item.matchingAdCount > 0
      ? buildCompanyJobsHref(linkableOrgNr, "matching")
      : null;

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
                {matchingAdsHref && (
                  <>
                    {" "}
                    <Link
                      href={matchingAdsHref}
                      className="jp-nudgelink"
                      aria-label={t("viewMatchingAdsAria", { company: displayName })}
                    >
                      {t("viewMatchingAds")}
                    </Link>
                  </>
                )}
              </p>
            ))}
          <div className="jp-job__meta">
            {item.isProtectedIdentity ? (
              <>
                {/* Preflight's `svg { display: block }` put the glyph on a line of its own and
                    pushed the label down. The parent `.jp-job__meta` is a flex row, so this child
                    blockifies and computes to `flex` rather than `inline-flex` — either resolves
                    it; `gap-1` carries the spacing the JSX space no longer can. */}
                <span
                  className="inline-flex items-center gap-1"
                  aria-describedby={hintId}
                >
                  <ShieldAlert size={14} aria-hidden="true" />
                  {t("protectedIdentity")}
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
            {/* The link lives INSIDE the count's own element, never as a sibling in the meta
                strip: `.jp-job__meta` wraps with a 6px/16px gap, so a sibling would break away
                from the number it refers to and read as a third, unrelated fact. */}
            <span className="tabular-nums">
              {t("activeAds", { count: item.activeAdCount })}
              {activeAdsHref && (
                <>
                  {" "}
                  <Link
                    href={activeAdsHref}
                    className="jp-nudgelink"
                    aria-label={t("viewAdsAria", { company: displayName })}
                  >
                    {t("viewAds")}
                  </Link>
                </>
              )}
            </span>
            {followedSince && <span>{t("followedSince", { date: followedSince })}</span>}
          </div>
          {/* #1547 — EVERY row without a linkable org.nr says so, not just the masked one. Before
              this delta all four rows were equally silent; the links create the asymmetry, and a
              missing affordance with no visible reason reads as a defect rather than a rule. The
              two branches are one treatment: the masked row can name its cause (the badge above
              already shows it), the brand-group row cannot — the FE schema cannot even tell that
              is what it is — so its sentence claims nothing about why.
              Shown only where a link would otherwise have rendered, so a 0-ad row stays quiet.
              ⚠ The masked copy deliberately offers NO next step. "Search the company name under
              Jobb" was the obvious remedy and it is measured FALSE: `search_vector` is title +
              description only (20260521090234_F6P4FtsSearchVector.cs:23) and `SuggestionKind` has
              no `Employer`, so that path returns zero hits — issue #1546. If #1546 lands a
              name-reachable employer route, that sentence goes stale and nothing detects it
              automatically; #1546 carries a comment naming this key for its closing PR. */}
          {linkableOrgNr === null &&
            (item.activeAdCount > 0 || (item.matchingAdCount ?? 0) > 0) && (
              <p className="jp-transparency-note jp-transparency-note--compact mt-2">
                {item.isProtectedIdentity ? (
                  <ShieldAlert size={14} aria-hidden="true" />
                ) : (
                  <Info size={14} aria-hidden="true" />
                )}
                <span>
                  {item.isProtectedIdentity ? t("adsNotLinkable") : t("adsNotLinkableUnknown")}
                </span>
              </p>
            )}
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
