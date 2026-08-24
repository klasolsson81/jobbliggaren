import { redirect } from "next/navigation";
import { getTranslations, getFormatter } from "next-intl/server";
import { formatMagnitude } from "@/lib/company-criteria/format-magnitude";
import { Announce } from "@/components/company-criteria/foretag-sok-announcer";
import { CompanyBrowseList } from "./company-browse-list";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";
import { InfoDialog } from "@/components/common/info-dialog";
import { searchCompanies } from "@/lib/api/company-search";
import { getCompanyWatchStatusByOrgNr } from "@/lib/api/company-follows";
import { buildPageHref, MAX_PAGE, PAGE_SIZE } from "@/lib/company-search/search-params";
import type { CriterionReference } from "@/lib/dto/company-criteria";

interface ForetagSokResultsProps {
  readonly namn: string;
  readonly sni: ReadonlyArray<string>;
  readonly kommun: ReadonlyArray<string>;
  readonly page: number;
  readonly reference: CriterionReference;
}

/**
 * #560 PR-B — the async results region of `/foretag/sok`, Suspense-streamed under the page. Carries the
 * same parts as the criterion-browse body (`bevakningar/[id]`) — a section heading, a mandatory
 * säteskommun explainer, the register table, pagination that preserves the active filter, and the
 * mandatory source attribution (DPIA C-D2/M-C4) — but no longer the same HEADING SHAPE: here the heading
 * is invariant and the honest magnitude sits on its own line beneath ("10 000+" when saturated, and NEVER
 * the pagination `totalCount`, which saturates at the servable cap), whereas the sibling still renders
 * its count inside the `<h2>`. Do not read this file as a description of that one. An empty filter
 * browses the whole register (Klas bind: browse-all default) and then carries NO number at all; a
 * zero-match filter shows the empty state.
 *
 * #1149 — `magnitude === null` is the single thing that distinguishes browse-all from a search here: it
 * decides the count line, the table's accessible name, the end-of-load announcement (#1092), and
 * nothing else re-derives it.
 */
export async function ForetagSokResults({
  namn,
  sni,
  kommun,
  page,
  reference,
}: ForetagSokResultsProps) {
  const t = await getTranslations("pages.foretag.sok");
  const format = await getFormatter();

  const result = await searchCompanies({
    name: namn,
    sniCodes: sni,
    municipalityCodes: kommun,
    page,
    pageSize: PAGE_SIZE,
  });

  switch (result.kind) {
    case "ok":
      break;
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return <ErrorShell title={t("loadErrorTitle")} body={t("rateLimited")} />;
    case "notFound":
    case "forbidden":
    case "error":
      return <ErrorShell title={t("loadErrorTitle")} body={t("loadErrorBody")} />;
  }

  const { companies, magnitude } = result.data;
  // NULL means an unfiltered browse-all, by contract — the ruling and the measurement behind it
  // live in GetCompanySearchMagnitudeQueryHandler. It is the ONE thing this surface branches on:
  // a second, local `hasFilter` derived from the URL axes would be a third answer to a question
  // the backend already answered over the normalized criteria, and it could disagree with the
  // number actually rendered.
  const filterState = { namn, sni, kommun };

  // #560 PR-C — follow-state overlay for the "Bevaka"-per-row affordance. A SEPARATE company_watches read
  // composed at the RSC edge (never a server-side join against the firewalled register — DPIA C-D4/M-C5),
  // sequenced after the search since its input is the search rows' own org.nrs. Masked/sole-prop rows
  // carry no org.nr key → excluded (non-followable); an empty/all-masked page skips the request entirely.
  const followableOrgNrs = companies.items.flatMap((company) =>
    company.organizationNumber !== null && !company.isProtectedIdentity
      ? [company.organizationNumber]
      : []
  );
  const followStatuses = await getCompanyWatchStatusByOrgNr(followableOrgNrs);
  const followStateByOrgNr = new Map<string, string | null>();
  followableOrgNrs.forEach((orgNr, i) => {
    followStateByOrgNr.set(orgNr, followStatuses[i]?.companyWatchId ?? null);
  });

  // #1092 — the end-of-load status message, routed to the surface's persistent live region rather
  // than announced from the element that renders it (WCAG 4.1.3; the mechanism and why it has to be
  // this way are in `foretag-sok-announcer.tsx`).
  //
  // Three cases, and they are deliberately not one sentence. A zero-match search is W3C's own
  // "No results returned" example and today reaches a screen reader through nothing at all — the
  // empty state has no live region of any kind. A count is its "18 results returned". A browse-all
  // renders NO number by #1149's ruling, so it gets a plain completion sentence instead of an
  // invented figure: announcing the start and then never closing it would leave a screen reader
  // waiting on a load that has in fact finished, which is worse than the silence it replaced.
  const announcement =
    companies.items.length === 0
      ? t("emptyTitle")
      : magnitude !== null
        ? `${formatMagnitude(format, magnitude)} ${t("resultsCountUnit", { count: magnitude.magnitude })}`
        : t("announceResultsReady");

  return (
    <div className="mt-8">
      <Announce message={announcement} />
      {companies.items.length === 0 ? (
        // Empty state carries the statement + next step; the magnitude headline + seat explainer are
        // suppressed here so a zero-match search does not double the "no companies" message (they
        // reference a table that is not shown).
        <div className="jp-empty">
          <div className="jp-empty__title">{t("emptyTitle")}</div>
          <p className="jp-empty__body text-body-sm text-text-primary">{t("emptyBody")}</p>
        </div>
      ) : (
        <>
          {/* The heading is INVARIANT — it names the section, it does not report a number. It used
              to be both, mutating between a label ("Företag i registret") and a statement ("1 234
              företag matchar sökningen"), which is the stats-card-heading shape the copy rules
              reject: a count belongs in its own line above the table, not inside the heading.
              `jp-h2` and not `text-h2`: the latter mints only the SIZE, and there is no base rule
              for `h2`, so the utility alone renders a heading at body weight. */}
          <h2 className="jp-h2">{t("resultsHeading")}</h2>

          {/* The count, and only when there IS one.
              It carries NO `role="status"` of its own (#1092): the search commits with
              `router.push`, so the result of the user's own action does have to be announced — but
              this element renders together with its text, which is exactly the shape ARIA22 rules
              out. The sentence goes to the persistent region above instead; keeping a role here as
              well would announce the same count twice.
              `.jp-results-count` is the house count line (`/jobb` uses it): sans with tabular
              figures and the number in `<b>`, never monospace, because DESIGN.md forbids mono for
              information-bearing digits. The number and the noun are separate arguments because
              the magnitude renders as a STRING when it saturates ("10 000+") while the plural has
              to select on the NUMBER. */}
          {magnitude !== null && (
            <p className="jp-results-count mt-1">
              <b>{formatMagnitude(format, magnitude)}</b>{" "}
              {t("resultsCountUnit", { count: magnitude.magnitude })}
            </p>
          )}

          {/* The browsable ceiling, stated once, wherever matches are actually LOST to it. Klas's
              ruling governs WHICH number may be rendered; it does not license leaving the cap
              unexplained, and both states can hit it — saturated shows "10 000+" above a pager
              that stops at 2 000, and a browse-all shows no number at all against 743 654 rows.
              The figure is derived from the caps, never restated: MaxPage × pageSize.

              Gated on the MAGNITUDE, not on `totalCount`, and on `>` rather than `>=`. The
              pagination count is itself capped, so `totalCount >= cap` is also true at exactly
              2 000 matches — where every match IS reachable and "hitta fler" would be a claim that
              more exist when none do. The magnitude is exact up to its own ceiling, so it is the
              only quantity on the page that can tell those two apart, and it is the same source
              the count line one node above already uses.

              The null branch is UNCONDITIONAL, and that is a declared unreachable state rather than
              an oversight: it would over-claim only for a register holding fewer companies than the
              cap, and the register holds 743 654. There is deliberately no exact signal to gate on
              there — a browse-all is precisely the case the backend refuses to count — so if the
              register ever shrank below 2 000 this line would need the same treatment the `>` above
              just got, and nothing here would notice.

              A second declared limit, in the other direction: the comparison is exact only while
              this surface's cap stays below the magnitude's own ceiling. `PAGE_SIZE` is a module
              constant of 20, so the cap is 2 000 against a Ceiling of 10 000. A caller sending
              pageSize 100 — which the backend's MaxPageSize permits — would make the cap 10 000
              too, and a saturated magnitude would then compare 10 000 > 10 000 = false and hide
              this line while matches past row 10 000 really are being lost. Unreachable from here;
              it becomes reachable the moment PAGE_SIZE stops being a constant. */}
          {(magnitude === null
            || magnitude.magnitude > MAX_PAGE * companies.pageSize) && (
            <p className="mt-1 text-body-sm text-text-primary">
              {t("browseCeiling", { count: MAX_PAGE * companies.pageSize })}
            </p>
          )}

          {/* Mandatory säteskommun explainer + inline help (the kommun is the registered seat, not
              necessarily where the company operates). */}
          <p className="mt-4 flex items-center gap-1 text-body-sm text-text-primary">
            {t("seatExplainer")}
            <InfoDialog
              title={t("seatHelpTitle")}
              paragraphs={[t("seatHelpBody1"), t("seatHelpBody2")]}
              ariaLabel={t("seatHelpAria")}
            />
          </p>

          <div className="mt-6 flex flex-col gap-4">
            <CompanyBrowseList
              items={companies.items}
              reference={reference}
              followStateByOrgNr={followStateByOrgNr}
              // The shared table's DEFAULT accessible name says "matchar din bevakning" — false here.
              // This surface answers a search; the labels name what the table actually is. And on
              // a browse-all there is no search either, so the name branches on the same null: a
              // screen reader was otherwise told it was hearing search results on a view where
              // nothing had been searched for.
              labels={{
                tableAria: magnitude !== null ? t("tableAria") : t("tableAriaAll"),
                tableCaption: t("tableCaption"),
              }}
            />
            <JobAdPagination
              page={companies.page}
              pageSize={companies.pageSize}
              totalCount={companies.totalCount}
              // UNCONDITIONALLY false: `totalCount` saturates at MaxServableRows here, so "träffar
              // totalt" would state a ceiling as a total — measured 2000 against 743 654 active
              // companies. Not branched on the filter, because even a filtered search under the cap
              // must not put a second, differently-derived number beside the magnitude line.
              showTotalCount={false}
              buildHref={(targetPage) => buildPageHref(filterState, targetPage)}
            />
          </div>
        </>
      )}

      {/* Mandatory source attribution (DPIA C-D2/M-C4). */}
      <p className="mt-6 border-t border-border pt-4 text-body-sm text-text-primary">
        {t("source")}
      </p>
    </div>
  );
}

/**
 * The four reachable failure branches end the load too, so they close the announcement the skeleton
 * opened (#1092, `code-reviewer` Major 2). Without this the region keeps saying "Söker företag…"
 * after the search has finished failing — the state the surface rule calls worse than the silence
 * it replaced.
 *
 * `role="alert"` stays and is NOT the announcement path: it is mounted with its text already in
 * place, which is the ARIA22 shape this PR exists to stop relying on. It is kept because an alert
 * that a sighted user can read is not made wrong by being unreliable for AT, and removing it would
 * change the visible error's semantics for no gain. `Announce` is what actually reaches a screen
 * reader; the title carries the whole status in one sentence, so the body is not repeated into it.
 */
function ErrorShell({ title, body }: { title: string; body: string }) {
  return (
    <div
      role="alert"
      className="mt-8 rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700"
    >
      <Announce message={title} />
      <p className="text-body font-medium">{title}</p>
      <p className="mt-1 text-body-sm">{body}</p>
    </div>
  );
}
