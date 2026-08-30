import type { ReactNode } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { formatDaysAgo } from "@/lib/i18n/relative-time";
import { formatNoticesStamp } from "@/lib/oversikt/aggregations";
import {
  countByStatus,
  totalCount,
} from "@/lib/applications/pipeline-counts";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ListCompanyWatchesResult } from "@/lib/dto/company-follows";
import {
  buildGuestPipeline,
  GUEST_COMPANY_WATCHES,
  GUEST_MOCK,
  GUEST_MOCK_REF_DATE,
  GUEST_NEW_FOLLOWED_COMPANY_AD_COUNT,
  OVERSIKT_MOCK,
} from "@/lib/guest/mock-data";
import {
  toCompanyWatches,
  toPipelineGroups,
} from "@/lib/guest/mock-adapters";
import { ApplicationSummary } from "@/components/oversikt/application-summary";
import { CompanySummary } from "@/components/oversikt/company-summary";
import { MarkAllReadRow } from "@/components/oversikt/mark-all-read-row";
import { InertNoticePrefsProvider } from "@/components/oversikt/notice-prefs-provider";
import { NoticeToolbar } from "@/components/oversikt/notice-toolbar";
import {
  NoticeSection,
  type SectionNoticeData,
} from "@/components/oversikt/notice-section";

// F-Pre Punkt 5 — Gäst-översikt-sida (CTO-dom 2026-05-24 Beslut 1).
//
// #1572 — omkomponerad på appens nuvarande Översikt (#726 → #1548 → #1556 →
// #1557 → #1558). Sidan var den sista konsumenten av den layout appen lämnade:
// `<TodayCard>` i hero-asiden, en platt `<NoticeList>` med "Kräver åtgärd"/
// "Information", och ett `.jp-summary`-rutnät av `<SummaryRow>`. Nu: hero utan
// aside, tre `<NoticeSection>` per källa med stående sammanfattningar, och
// `<MarkAllReadRow>` sist på sidan.
//
// Tre saker skiljer sig från appen, var och en av ett mätt skäl:
//
// 1. INGEN `<SetupCallout>`. Dess CTA går till `/oversikt?matchsetup=1`, och
//    demot har ingen profil att ställa in.
// 2. INGA NOTIS-PREFERENSER ÖVER HUVUD TAGET. Kugghjulet utelämnas (`prefTypes`
//    osatt) per Klas-direktiv 2026-08-29, och notis-regionen omsluts av
//    `<InertNoticePrefsProvider>` så även LÄSNINGEN slås av. Båda behövs: nyckeln
//    `jp-oversikt-notice-prefs` har formen `"<källa>:<typ>"` och DELAS med den
//    inloggade appen i samma webbläsare — till skillnad från notis-id:na, som är
//    disjunkta — så enbart dölja kugghjulet hade lämnat en publik yta filtrerad av
//    en preferens besökaren varken kan se eller ångra där (CTO-dom 2026-08-29).
// 3. INGA LÄNKAR IN I DEN SKYDDADE APPEN. `/ansokningar`, `/ny-ansokan`,
//    `/foretag/bevakade` och `/foretag/sok` ligger alla i `PROTECTED_PREFIXES`, så
//    proxyn hade skickat besökaren till `/logga-in`. Ansöknings-sammanfattningen och
//    de två ansökningsnotiserna pekar på gästens egen spegel;
//    företags-sammanfattningen renderar ingen länk alls, eftersom ETIKETTEN ("Visa
//    bevakade företag") inte har någon sann destination här.
//
//    Därför bär exakt EN notis "Skapa konto" — företagsnotisen, den enda sektion som
//    saknar gästdestination. Övriga notis-CTA:er är radens åtgärd på sitt objekt, som
//    på appytan; ett demo som skickar dig till registreringen slutar demonstrera
//    (`design-reviewer` Major 2, 2026-08-29).
//
// Copyn delas på namnrymd: `guest` bär det demo-röstade, `oversikt` det strukturella
// (sektionsrubriker, tomt-lägen). Det är delningen som hindrar "Mina ansökningar"
// från att drifta mellan ytorna.

export function GuestOversiktPage() {
  // Synchronous next-intl translators — keeps this a non-async RSC.
  const t = useTranslations("guest");
  const tOversikt = useTranslations("oversikt");
  const tRelativeTime = useTranslations("guest.relativeTime");
  const format = useFormatter();

  const { applications } = GUEST_MOCK;
  const latestOffer = applications.find((a) => a.status === "Offer");
  const latestInterview = applications.find((a) => a.status === "Interview");

  // Stämpeln är RENDER-tiden, inte mockens frysdatum — sidan är `force-dynamic`, så
  // uppdatera-kontrollen ger en ny render och därmed en ny stämpel.
  //
  // Men den stämplar SIDLADDNINGEN, inte datafärskhet (`contentCanChange={false}`):
  // innehållet här kan inte skilja sig mellan två renderingar, så "Senast uppdaterad"
  // hade varit ett påstående ingen render kan leverera — och den gamla gäststämpeln bar
  // kvalifikationen i sig själv ("exempeldata · {datum}")
  // (`design-reviewer` Major 1, 2026-08-29).
  const now = new Date();

  // Sammanfattningarna kan inte degradera här: demot gör ingen hämtning, så det
  // finns ingen läsning som kan falla. `ok` är alltså inte en optimism utan den
  // enda nåbara grenen.
  const pipeline: ApiResult<PipelineGroupDto[]> = {
    kind: "ok",
    data: toPipelineGroups(buildGuestPipeline()),
  };
  const companyWatches: ApiResult<ListCompanyWatchesResult> = {
    kind: "ok",
    data: toCompanyWatches(GUEST_COMPANY_WATCHES),
  };

  // Härledda, inte hårdkodade: skulle mocken någon gång tömmas ska sektionen
  // sluta räkna olästa i stället för att påstå ett tillstånd sammanfattningen
  // motsäger. Samma form som appen (`oversikt-page.tsx:123-141`).
  const summaryOwns =
    totalCount(countByStatus(pipeline.data)) === 0 ? ("empty" as const) : undefined;
  const companySummaryOwns =
    companyWatches.data.length === 0 ? ("empty" as const) : undefined;

  const bold = (chunks: ReactNode) => <b>{chunks}</b>;

  // ── Mina ansökningar ──────────────────────────────────────────────────────
  //
  // Utkasts-notisen är BORTTAGEN. Ingen typ i `NOTICE_TYPES.applications`
  // beskriver ett osänt utkast — `followup` betyder uteblivet svar på något som
  // ÄR skickat — och faktumet gick inte förlorat: `<ApplicationSummary>` renderar
  // "Utkast 2" som ett eget steg, vilket är där stående tillstånd bor sedan
  // #1548, och dess ankarlänk bär samma väg till `/gast/ansokningar` som notisen
  // gjorde.
  const applicationNotices: SectionNoticeData[] = [];
  if (latestOffer) {
    applicationNotices.push({
      id: "guest-n-offer",
      source: "applications",
      type: "offers",
      kind: "success",
      label: t("oversikt.noticeOfferLabel"),
      text: t.rich("oversikt.noticeOfferText", {
        company: latestOffer.company,
        role: latestOffer.role,
        b: bold,
      }),
      // Radens åtgärd på sitt eget objekt, med appens egen etikett — inte en global
      // konverteringsknapp. Demot har en egen ansökningsvy att peka in i.
      cta: tOversikt("notices.offerCta"),
      href: `/gast/ansokningar/${latestOffer.id}`,
      // Härledd ur mocken, som appen gör (`oversikt-page.tsx:194`), inte ett valt
      // ord: så bär demot samma relativtids-form som produkten (#1516).
      time: formatDaysAgo(
        tRelativeTime,
        latestOffer.updatedAtIso,
        GUEST_MOCK_REF_DATE,
      ),
    });
  }
  if (latestInterview) {
    applicationNotices.push({
      id: "guest-n-interview",
      source: "applications",
      type: "interviews",
      kind: "brand",
      label: t("oversikt.noticeInterviewLabel"),
      text: t.rich("oversikt.noticeInterviewText", {
        company: latestInterview.company,
        b: bold,
      }),
      cta: tOversikt("notices.interviewCta"),
      href: `/gast/ansokningar/${latestInterview.id}`,
      time: formatDaysAgo(
        tRelativeTime,
        latestInterview.updatedAtIso,
        GUEST_MOCK_REF_DATE,
      ),
    });
  }

  // ── Jobbannonser ──────────────────────────────────────────────────────────
  const jobAdNotices: SectionNoticeData[] = [
    {
      id: "guest-n-match",
      source: "jobads",
      type: "matches",
      kind: "info",
      label: t("oversikt.noticeMatchLabel"),
      text: t.rich("oversikt.noticeMatchText", {
        count: OVERSIKT_MOCK.matchCountThisWeek,
        segment: OVERSIKT_MOCK.matchSegmentLabel,
        b: bold,
        em: (chunks) => <em>{chunks}</em>,
      }),
      cta: t("oversikt.noticeMatchCta"),
      href: "/gast/jobb",
      // Valt ord, inte härlett: bakom talet finns ingen tidsstämpel att räkna
      // från — samma MOCK-not som appens matchnings-notis bär.
      time: t("oversikt.timeToday"),
    },
  ];

  // ── Företagsbevakning ─────────────────────────────────────────────────────
  const companyNotices: SectionNoticeData[] = [
    {
      id: "guest-n-followed-ads",
      source: "companies",
      type: "followedads",
      kind: "info",
      label: t("oversikt.noticeCompaniesLabel"),
      text: t.rich("oversikt.noticeCompaniesText", {
        count: GUEST_NEW_FOLLOWED_COMPANY_AD_COUNT,
        b: bold,
      }),
      cta: t("oversikt.noticeCompaniesCta"),
      href: "/registrera",
      time: t("oversikt.timeToday"),
    },
  ];

  const allNotices: SectionNoticeData[] = [
    ...applicationNotices,
    ...jobAdNotices,
    ...companyNotices,
  ];

  return (
    <>
      {/* Page-hero utan aside — paritet med appens Översikt, där I dag-kortet
          togs bort i #726. */}
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">{t("oversikt.kicker")}</div>
            <h1 className="jp-pagehero__title">{t("oversikt.title")}</h1>
            <p className="jp-pagehero__lede">{t("oversikt.lede")}</p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <NoticeToolbar
          lastUpdated={formatNoticesStamp(format, now)}
          lastUpdatedIso={now.toISOString()}
          contentCanChange={false}
        />

        <InertNoticePrefsProvider>
          <NoticeSection
            source="applications"
            titleId="gast-oversikt-applications"
            title={tOversikt("notices.sectionApplications")}
            notices={applicationNotices}
            emptyBody={tOversikt("notices.emptyApplications")}
            summary={
              <ApplicationSummary
                pipeline={pipeline}
                linkHref="/gast/ansokningar"
              />
            }
            summaryOwns={summaryOwns}
          />
          <NoticeSection
            source="jobads"
            titleId="gast-oversikt-jobads"
            title={tOversikt("notices.sectionJobAds")}
            notices={jobAdNotices}
            emptyBody={tOversikt("notices.emptyJobAds")}
          />
          <NoticeSection
            source="companies"
            titleId="gast-oversikt-companies"
            title={tOversikt("notices.sectionCompanies")}
            notices={companyNotices}
            emptyBody={tOversikt("notices.emptyCompanies")}
            summary={
              <CompanySummary watches={companyWatches} linkHref={null} />
            }
            summaryOwns={companySummaryOwns}
          />

          {/* Sist på sidan, efter det den verkar på (#1557). `noticeIdsRotate` är
              falskt här: gästens notis-id är statiska literaler utan datumdel, så
              appens "till i morgon" hade varit ett falskt påstående (#1572). */}
          <MarkAllReadRow notices={allNotices} noticeIdsRotate={false} />
        </InertNoticePrefsProvider>
      </div>
    </>
  );
}
