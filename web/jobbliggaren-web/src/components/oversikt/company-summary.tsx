import Link from "next/link";
import {
  buildCompanyJobsHref,
  isLinkableOrgNr,
} from "@/lib/job-ads/company-jobs-href";
import { EyeOff, Filter } from "lucide-react";
import { useTranslations } from "next-intl";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ListCompanyWatchesResult } from "@/lib/dto/company-follows";

interface CompanySummaryProps {
  /**
   * Bevakningarna som Result, inte som array — samma skäl som `ApplicationSummary`:
   * sammanfattningen MÅSTE kunna skilja "du bevakar inga företag" från "listan kunde
   * inte hämtas". Degraderas den till [] påstår ett tomt-läge noll när sanningen är omätt.
   */
  readonly watches: ApiResult<ListCompanyWatchesResult>;
  /**
   * Vart ankarradens länk pekar. `null` = rendera ingen länk alls.
   *
   * Obligatorisk och utan default, av samma skäl som `ApplicationSummary.linkHref`:
   * en utelämnad prop hade tyst löst till en route i `PROTECTED_PREFIXES`.
   *
   * Gäst-demon (#1572) skickar `null`, och det är etiketten som avgör det, inte
   * href:en: `companySummary.link` lyder "Visa bevakade företag", så en omdirigering
   * till `/registrera` hade gjort ETIKETTEN falsk i stället för att laga länken.
   * Demot har ingen `/gast/foretag` att peka på, och sektionens notis bär redan
   * "Skapa konto" som konverteringsväg.
   *
   * Ingen prop för tomt-lägets `/foretag/sok`: den grenen kräver noll bevakningar, och
   * `mock-adapters.test.ts` pinnar gästmockens bevakningsmängd som icke-tom.
   */
  readonly linkHref: string | null;
}

/**
 * Stående tillstånd över "Företagsbevakning" på Översikt (#1558).
 *
 * Sektionens enda källa var ett delta mot en watermark som besöket på /foretag/bevakade
 * självt avancerar, så efter det besöket läste sektionen som tom för ett konto som
 * bevakar företag med aktiva annonser. Den här raden svarar på "vad bevakar jag, och
 * finns det något där" utan att flytta katalogen från /foretag/bevakade hit.
 *
 * Formen är EN ankarrad plus högst två villkorade rader — aldrig en post per företag.
 * En radlista här hade lagt en radlista ovanpå notislistans, på samma vänsterkant och
 * med samma grammatik men annan betydelse (stående tillstånd vs händelser). Formen
 * varierar därför inte med antalet: den är densamma vid 1 som vid 25 bevakningar, och
 * inget företagsnamn renderas.
 */
export function CompanySummary({
  watches,
  linkHref,
}: CompanySummaryProps) {
  const t = useTranslations("oversikt.companySummary");

  if (watches.kind !== "ok") {
    return (
      <p className="jp-appsummary jp-appsummary--unavailable">
        {t("unavailable")}
      </p>
    );
  }

  const items = watches.data;

  if (items.length === 0) {
    return (
      <div className="jp-appsummary jp-appsummary--empty">
        <p className="jp-appsummary__emptytitle">{t("emptyTitle")}</p>
        <p className="jp-appsummary__emptybody">{t("emptyBody")}</p>
        {/* Betonad men inte solid: en-primär-per-skärm är redan spenderad, och i
            det här läget kan setup-kortet stå högre upp på samma sida. */}
        <Link className="jp-btn jp-btn--emphasis" href="/foretag/sok">
          {t("emptyCta")}
        </Link>
      </div>
    );
  }

  // Summan är exakt för att employer-bevakningar är disjunkta PER KONSTRUKTION: det unika
  // indexet `ux_company_watches_user_orgnr_active` på (UserId, OrganizationNumber) ger en
  // rad per arbetsgivare och användare, och en annons har en arbetsgivare. Det villkor som
  // bryter invarianten är en `BrandGroup`-bevakning — dess rad summerar över sina medlemmar
  // och kan därför täcka en annan rads org.nr — och dto:n bär varken `targetType` eller
  // `brandGroupId`, så klienten kan inte upptäcka en sådan rad. Det arbetet bor i #1566.
  const activeAds = items.reduce((sum, w) => sum + w.activeAdCount, 0);

  // `some`, inte `every`. Backendens SSYK-gate sätts en gång per request, så alla
  // bevakningar är null eller ingen — men brister den gaten någon gång tystnar raden
  // hellre än summerar en delmängd och underskattar tyst. Förenkla inte till `every`.
  const matchingNotAssessed = items.some((w) => w.matchingAdCount === null);
  const matchingAds = matchingNotAssessed
    ? null
    : items.reduce((sum, w) => sum + (w.matchingAdCount ?? 0), 0);

  // En rad över alla bevakningar, aldrig en per bevakning — en per-bevakningsrad vore
  // katalogen från /foretag/bevakade, byggd genom bakdörren.
  const filteredWatches = items.filter((w) => w.filter !== null).length;

  // Klas-direktiv 2026-08-30: the sums link straight to the ads, so a user reaches them in one
  // click instead of going through /foretag/bevakade first.
  //
  // EVERY watch must be linkable or neither sum links. A masked sole-prop and a brand-group watch
  // both arrive with `organizationNumber: null`, and their ads would be missing from the
  // destination while the number beside the link still counted them -- the count/click divergence
  // this route exists to avoid. Partial is worse than plain text here.
  const linkableOrgNrs = items.flatMap((w) =>
    !w.isProtectedIdentity &&
    w.organizationNumber &&
    isLinkableOrgNr(w.organizationNumber)
      ? [w.organizationNumber]
      : []
  );
  const everyWatchLinkable = linkableOrgNrs.length === items.length;

  // A 0 is a negation, not a number, so it gets no link -- parity the watch row.
  const activeAdsHref =
    everyWatchLinkable && activeAds > 0
      ? buildCompanyJobsHref(linkableOrgNrs, "all")
      : null;
  const matchingAdsHref =
    everyWatchLinkable && matchingAds !== null && matchingAds > 0
      ? buildCompanyJobsHref(linkableOrgNrs, "matching")
      : null;

  // Shown only where a link would otherwise have rendered -- an account whose watches have no
  // ads at all is not missing anything, so it stays quiet. Parity with the watch row, which
  // explains the same absence per row.
  const notLinkableCount = items.length - linkableOrgNrs.length;
  const explainMissingLinks =
    notLinkableCount > 0 && (activeAds > 0 || (matchingAds ?? 0) > 0);

  return (
    <div className="jp-appsummary">
      <p className="jp-appsummary__anchor">
        <span className="jp-appsummary__totals tabular-nums">
          {t.rich("anchor", {
            count: items.length,
            active: activeAds,
            // The accessible name is the visible text -- 2.5.3 Label in Name holds by
            // construction, and the plural lives in ONE key rather than a visible copy and an
            // aria copy that can drift. There is exactly one such link on the page and its
            // enclosing paragraph is the programmatic context 2.4.4 asks for, so no suffix is
            // owed here. The watch card is the opposite case and does carry one.
            lnk: (chunks) =>
              activeAdsHref ? (
                <Link href={activeAdsHref} className="jp-countlink" prefetch={false}>
                  {chunks}
                </Link>
              ) : (
                <>{chunks}</>
              ),
          })}
        </span>
        {linkHref !== null && (
          <Link className="jp-appsummary__link" href={linkHref}>
            {t("link")}
          </Link>
        )}
      </p>

      {/* Ej bedömd matchning tiger helt: ingen nolla (dto:ns null är "inte bedömd", och
          en 0 vore falsk), och ingen nudge — den grenen sammanfaller med
          `!hasStatedDesiredOccupation`, där SetupCallout redan står med samma mål. En
          BEDÖMD nolla skrivs däremot alltid ut; att tysta ett mätt tal är issuets egen
          felklass. */}
      {matchingAds !== null && (
        <p className="jp-matchline tabular-nums">
          {t.rich("matching", {
            count: matchingAds,
            lnk: (chunks) =>
              matchingAdsHref ? (
                <Link href={matchingAdsHref} className="jp-countlink" prefetch={false}>
                  {chunks}
                </Link>
              ) : (
                <>{chunks}</>
              ),
          })}
        </p>
      )}

      {/* Utan den här raden går ett per-bevakningsfilter som tystar allt inte att skilja
          från "inget publicerat" — och sammanfattningen ställer nu volym intill just den
          tystnaden. */}
      {explainMissingLinks && (
        <p className="jp-transparency-note">
          <EyeOff size={16} aria-hidden="true" />
          <span>{t("notLinkable", { count: notLinkableCount })}</span>
        </p>
      )}

      {filteredWatches > 0 && (
        <p className="jp-transparency-note">
          <Filter size={16} aria-hidden="true" />
          <span>{t("filter", { count: filteredWatches })}</span>
        </p>
      )}
    </div>
  );
}
