import Link from "next/link";
import { Filter } from "lucide-react";
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
 * inget företagsnamn renderas. Följden är att `companyName`, `organizationNumber` och
 * `isProtectedIdentity` aldrig når Översikt (ADR 0087 D8(c)).
 *
 * Talen är olänkade. Ankarradens länk går till /foretag/bevakade och tomt-lägets till
 * /foretag/sok; ingen `?employer=<orgnr>`-axel emitteras här — den frågan är #1547:s.
 */
export function CompanySummary({ watches }: CompanySummaryProps) {
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

  return (
    <div className="jp-appsummary">
      <p className="jp-appsummary__anchor">
        <span className="jp-appsummary__totals tabular-nums">
          {t("anchor", { count: items.length, active: activeAds })}
        </span>
        <Link className="jp-appsummary__link" href="/foretag/bevakade">
          {t("link")}
        </Link>
      </p>

      {/* Ej bedömd matchning tiger helt: ingen nolla (dto:ns null är "inte bedömd", och
          en 0 vore falsk), och ingen nudge — den grenen sammanfaller med
          `!hasStatedDesiredOccupation`, där SetupCallout redan står med samma mål. En
          BEDÖMD nolla skrivs däremot alltid ut; att tysta ett mätt tal är issuets egen
          felklass. */}
      {matchingAds !== null && (
        <p className="jp-matchline tabular-nums">
          {t("matching", { count: matchingAds })}
        </p>
      )}

      {/* Utan den här raden går ett per-bevakningsfilter som tystar allt inte att skilja
          från "inget publicerat" — och sammanfattningen ställer nu volym intill just den
          tystnaden. */}
      {filteredWatches > 0 && (
        <p className="jp-transparency-note">
          <Filter size={16} aria-hidden="true" />
          <span>{t("filter", { count: filteredWatches })}</span>
        </p>
      )}
    </div>
  );
}
