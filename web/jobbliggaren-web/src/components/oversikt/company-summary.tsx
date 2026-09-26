import Link from "next/link";
import { InfoDialog } from "@/components/common/info-dialog";
import { summariseWatches } from "@/lib/company-watches/watch-summary";
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
   * till inloggningssidan hade gjort ETIKETTEN falsk i stället för att laga länken.
   * Demot har ingen `/gast/foretag` att peka på, och sektionens notis bär redan
   * "Skapa konto" som konverteringsväg.
   *
   * Ingen prop för tomt-lägets `/foretag/sok`: den grenen kräver noll bevakningar, och
   * `mock-adapters.test.ts` pinnar gästmockens bevakningsmängd som icke-tom.
   */
  readonly linkHref: string | null;
  /**
   * Blockets namn, eller `null` för att rendera inget namn alls (#1717).
   *
   * Obligatorisk och utan default, av samma skäl som `linkHref` ovan: en utelämnad prop hade
   * tyst gett varje anropsställe en rubrik, och den ENA yta som inte ska ha en — gäst-demon,
   * vars sektion har en enda innehållstyp — hade fått den utan att någon valde det.
   *
   * `null` är alltså ett val och inte en frånvaro. `design-reviewer` B1/B4 (2026-09-13): ett
   * block namnges av närmaste rubrik ovanför sig, så där sektionens h2 redan står ensam över
   * en enda innehållstyp vore en h3 en tautologi.
   */
  readonly heading: string | null;
}

/**
 * Stående tillstånd över "Företagsbevakning" på Översikt (#1558).
 *
 * Sektionens enda källa var ett delta mot en watermark som besöket på /foretag/bevakade/nya
 * avancerar, så efter det besöket läste sektionen som tom för ett konto som
 * bevakar företag med aktiva annonser. Den här raden svarar på "vad bevakar jag, och
 * finns det något där" utan att flytta katalogen från /foretag/bevakade hit.
 *
 * Formen är EN ankarrad plus högst två villkorade rader — aldrig en post per företag.
 * En radlista här hade lagt en radlista ovanpå notislistans, på samma vänsterkant och
 * med samma grammatik men annan betydelse (stående tillstånd vs händelser). Formen
 * varierar därför inte med antalet: den är densamma vid 1 som vid 25 bevakningar, och
 * inget företagsnamn renderas.
 *
 * Siffrorna och länkregeln bor i `summariseWatches` (ADR 0140): samma tal renderas som ett kort
 * på `/oversikt`, och en andra härledning hade varit en andra sanning.
 */
export function CompanySummary({
  watches,
  linkHref,
  heading,
}: CompanySummaryProps) {
  const t = useTranslations("oversikt.companySummary");
  // The matching rule is read from the keys the watch-filter dialog already owns, never copied:
  // one rule, one text. `jobads.companyWatches.filter` is a foreign namespace on purpose --
  // duplicating the two sentences here is how the two surfaces drift apart on the next edit.
  const tRule = useTranslations("jobads.companyWatches.filter");

  // Renderas i ALLA tre lägena, ovillkorligt (design-reviewer B1): namnet är blockets, och
  // tomt-läget är just när läsaren mest behöver veta VILKET block som är tomt.
  const headingNode =
    heading !== null ? (
      <h3 className="jp-appsummary__heading">{heading}</h3>
    ) : null;

  if (watches.kind !== "ok") {
    // Utan rubrik behålls den ensamma `<p>`-formen ORÖRD — det är den som gör gäst-ytan
    // byte-identisk (design-reviewer B4). Med rubrik måste grenen bära två barn, och klassen
    // stannar på rotelementet så `.jp-appsummary:has(+ .jp-appsummary)` fortsätter matcha.
    if (headingNode === null) {
      return (
        <p className="jp-appsummary jp-appsummary--unavailable">
          {t("unavailable")}
        </p>
      );
    }
    return (
      <div className="jp-appsummary jp-appsummary--unavailable">
        {headingNode}
        <p>{t("unavailable")}</p>
      </div>
    );
  }

  const items = watches.data;

  if (items.length === 0) {
    return (
      <div className="jp-appsummary jp-appsummary--empty">
        {headingNode}
        <p className="jp-appsummary__emptytitle">{t("emptyTitle")}</p>
        {/* Betonad men inte solid: en-primär-per-skärm är redan spenderad, och i
            det här läget kan setup-kortet stå högre upp på samma sida. */}
        <Link className="jp-btn jp-btn--emphasis" href="/foretag/sok">
          {t("emptyCta")}
        </Link>
      </div>
    );
  }

  // `linkHref === null` means this surface has no authenticated destination at all (#1572: the
  // guest demo). One prop, one meaning: it gates the anchor link, the ad links and the rule
  // dialog alike.
  const surfaceCanLink = linkHref !== null;
  const summary = summariseWatches(items, surfaceCanLink);

  return (
    <div className="jp-appsummary">
      {headingNode}
      <p className="jp-appsummary__anchor">
        <span className="jp-appsummary__totals tabular-nums">
          {t.rich("anchor", {
            count: summary.count,
            active: summary.activeAds,
            // The accessible name is the visible text -- 2.5.3 Label in Name holds by
            // construction, and the plural lives in ONE key rather than a visible copy and an
            // aria copy that can drift. There is exactly one such link on the page and its
            // enclosing paragraph is the programmatic context 2.4.4 asks for, so no suffix is
            // owed here. The watch card is the opposite case and does carry one.
            lnk: (chunks) =>
              summary.activeAdsHref ? (
                <Link href={summary.activeAdsHref} className="jp-countlink" prefetch={false}>
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
          `!hasStatedDesiredOccupation`. En
          BEDÖMD nolla skrivs däremot alltid ut; att tysta ett mätt tal är issuets egen
          felklass. */}
      {summary.matchingAds !== null && (
        <p className="jp-matchline tabular-nums">
          {t.rich("matching", {
            count: summary.matchingAds,
            lnk: (chunks) =>
              summary.matchingAdsHref ? (
                <Link href={summary.matchingAdsHref} className="jp-countlink" prefetch={false}>
                  {chunks}
                </Link>
              ) : (
                <>{chunks}</>
              ),
          })}
          {/* Gated on the SAME flag as the links: `linkHref === null` means this surface has no
              authenticated destination, and the rule's second sentence sends the reader to
              Matchning. The guest demo has no such page (measured: `(guest)/gast/` carries
              oversikt, jobb, ansokningar and cv, and nothing else), so the help would name a
              place the reader cannot reach. */}
          {surfaceCanLink && (
            <InfoDialog
              title={tRule("onlyMatchedHelpTitle")}
              paragraphs={[
                tRule("onlyMatchedHelpBody1"),
                tRule("onlyMatchedHelpBody2"),
              ]}
              ariaLabel={tRule("onlyMatchedHelpAria")}
            />
          )}
        </p>
      )}

      {summary.explainMissingLinks && (
        <p className="jp-transparency-note">
          <EyeOff size={16} aria-hidden="true" />
          <span>{t("notLinkable")}</span>
        </p>
      )}

      {/* Utan den här raden går ett per-bevakningsfilter som tystar allt inte att skilja
          från "inget publicerat" — och sammanfattningen ställer nu volym intill just den
          tystnaden. */}
      {summary.filteredWatches > 0 && (
        <p className="jp-transparency-note">
          <Filter size={16} aria-hidden="true" />
          <span>{t("filter", { count: summary.filteredWatches })}</span>
        </p>
      )}
    </div>
  );
}
