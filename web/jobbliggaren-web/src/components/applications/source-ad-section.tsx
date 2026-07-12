import { ExternalLink } from "lucide-react";
import { PreservedAdPanel } from "@/components/applications/preserved-ad-panel";
import { useTranslations } from "next-intl";
import { applicationSourceLabel } from "@/lib/applications/status";
import type { AdSnapshotDto, JobAdSummaryDto } from "@/lib/types/applications";

interface SourceAdSectionProps {
  jobAd: JobAdSummaryDto | null;
  preservedAd: AdSnapshotDto | null;
}

/**
 * "Om annonsen" — #805-3 (Klas Beslut B). ETT ställe som avgör vad ansökan får
 * säga om KÄLLANS annons, delat av fullsidan (`ApplicationDetail`) och modal-
 * kroppen (`ApplicationDrawerBody`). Guarden bor här och ingen annanstans (SPOT):
 * att den tidigare låg duplicerad i båda ytorna är hur den kunde vara fel i båda
 * samtidigt.
 *
 * ## Fyra tillstånd, alla sanna
 *
 * | Tillstånd | Villkor | Rendering |
 * |---|---|---|
 * | **Live** | JobAd-rad + `status === "Active"` | "Visa annonsen" → källans URL, ny flik |
 * | **Borta** | JobAd-rad + status ≠ Active (`Archived`/`Expired`/okänt) | INGEN länk. Bevarad kopia (ADR 0086) om den finns, annars en lugn not |
 * | **Manuell** | Ingen JobAd-rad (`jobAdId == null`) | Länk om användaren sparade en URL. Ingen livs-utsaga åt något håll |
 * | **Ingen annons** | `jobAd == null` | Inget alls (ansökan är enbart personligt brev) |
 *
 * ## Varför `status`, inte `jobAd == null`
 *
 * Läsvägen kodade tidigare "annonsen är borta" som `jobAd == null` och delegerade
 * den nullen till soft-delete-axeln `JobAd.DeletedAt` — som saknar writer (#821).
 * Följd: `jobAd` blev ALDRIG null för en JobAd-länkad ansökan, `PreservedAdPanel`
 * renderades aldrig i produktion, och produktens enda "Visa annonsen"-utlänk bodde
 * inuti den. Systemets verkliga livscykel-axel är `JobAd.Status` (Active →
 * Archived, skriven av snapshot-miss-retention + expiry-jobbet), som sedan #805-3
 * bärs av `JobAdSummaryDto.Status`.
 *
 * ## Default-deny på liveness
 *
 * Live hävdas ENDAST på positivt `"Active"`. Domänen har tre statusvärden
 * (`Active` | `Expired` | `Archived`), så den naiva inversen (`!== "Archived"` ⇒
 * live) skulle skeppa en död länk för `Expired` — precis vad Beslut B förbjuder.
 * Ett okänt framtida värde faller också ut som "inte live". Vi hävdar aldrig att
 * en annons lever utan bevis.
 *
 * Manuell posting särskiljs på `jobAdId == null` (strukturell sanning) snarare än
 * på `status == null`, så en deploy-skewad respons utan `status`-fältet degraderar
 * till "vet inte" (ingen länk) i stället för att missläsas som manuell.
 */
export function SourceAdSection({ jobAd, preservedAd }: SourceAdSectionProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  // Ingen annonsrad alls (enbart personligt brev) — inget att säga om någon annons.
  if (jobAd == null) return null;

  const isManual = jobAd.jobAdId == null;
  const isLive = !isManual && jobAd.status === "Active";
  const isGone = !isManual && jobAd.status != null && jobAd.status !== "Active";

  const sourceLabel = applicationSourceLabel(t, jobAd.source);

  // Borta: annonsen är inte längre aktiv hos källan. Ingen utlänk — vi kan inte
  // hävda att URL:en fortfarande svarar, och en död länk är sämre än ingen
  // (Beslut B). Den bevarade kopian är ADR 0086:s löfte och bär hela budskapet.
  if (isGone) {
    if (preservedAd != null) return <PreservedAdPanel preservedAd={preservedAd} />;

    // Ingen bevarad kopia (ansökan skapad före #315) — en lugn not, inget mer.
    return (
      <section aria-labelledby="jp-source-ad-title">
        <div className="jp-section-label" id="jp-source-ad-title">
          {tUi("jobInfo.title")}
        </div>
        <div className="jp-modal__match">
          <div className="jp-source-ad__note">
            {tUi("jobInfo.noLongerActive", { source: sourceLabel })}
          </div>
        </div>
      </section>
    );
  }

  // Live eller manuell utan sparad URL → inget att länka till.
  if (!(isLive || isManual) || jobAd.url == null) return null;

  return (
    <section aria-labelledby="jp-source-ad-title">
      <div className="jp-section-label" id="jp-source-ad-title">
        {tUi("jobInfo.title")}
      </div>

      {/* Säker utlänk — samma behandling som den bevarade panelen bar (F3-mönstret
          från JobAdDetail): sekundär knapp, ExternalLink-ikon, ny flik,
          noopener/noreferrer. aria-label bär källan när vi känner den; för en
          manuell ansökan utelämnas källan helt (annars renderas "…hos Manuellt"). */}
      <p className="jp-source-ad__action">
        <a
          href={jobAd.url}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={
            isManual
              ? tUi("jobInfo.viewAdAriaLabelManual")
              : tUi("jobInfo.viewAdAriaLabel", { source: sourceLabel })
          }
          className="jp-btn jp-btn--secondary"
        >
          <ExternalLink size={14} aria-hidden="true" /> {tUi("jobInfo.viewAd")}
        </a>
      </p>
    </section>
  );
}
