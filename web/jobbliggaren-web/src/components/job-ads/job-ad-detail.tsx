import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import { jobAdStatusLabel } from "@/lib/job-ads/status";
import { formatDate } from "@/lib/i18n/format";
import type { JobAdDto, JobAdStatus } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import type { CompanyFollowState } from "@/lib/dto/company-follows";
import type { OrtGranularity } from "@/lib/job-ads/ort-granularity";
import { SaveJobAdToggle } from "@/components/saved-job-ads/save-job-ad-toggle";
import { HarAnsoktButton } from "@/components/applications/har-ansokt-button";
import { FollowCompanyToggle } from "@/components/company-follows/follow-company-toggle";
import { JobAdMatchSection } from "./job-ad-match-section";
import { formatAdDescription } from "./format-ad-description";

/**
 * JobAdDetail — ren presentational Server Component (ingen "use client",
 * noll interaktivitet). Delas av både fullsida (`/jobb/[id]`) och
 * jobbmodalen (`@modal/(.)jobb/[id]`) per ADR 0053 (en presentations-
 * komponent, två kontexter — DRY-positiv konsekvens).
 *
 * Fält-set är ADR 0053 amendment 2026-05-19 (Fas-3-gated): ENDAST real
 * JobAdDto. Match-score / requirements / occupation / location / "Spara
 * annons" / "Har ansökt" var v3-prototyp-mock och saknas i domänen — de
 * renderas EJ (frånvaro, inte mock; HANDOVER §0.5-veto uppfylls genom
 * frånvaro, ingen disabled-knapp-teater).
 */

interface JobAdDetailProps {
  jobAd: JobAdDto;
  /**
   * När true renderas titel/företag i modal-headern av anroparen
   * (JobAdModalShell), så detaljen utelämnar sin egen rubrik-header.
   * Fullsidan sätter false och äger rubriken själv.
   */
  headless?: boolean;
  /**
   * F6 P5 Punkt 2 — initial-state för Spara/Har-ansökt-knappar i modal-footer.
   * `undefined` (default) = anonym/system-vy → knappar döljs helt
   * (civic-utility — ingen disabled-knapp-teater).
   * När definerade: båda måste vara satta tillsammans (PR5 — server-fetchar
   * isJobAdSaved + hasAppliedJobAd parallellt i page-handler).
   */
  initialSaved?: boolean;
  initialApplied?: boolean;
  /**
   * #311 #455 (ADR 0087 D8(c)) — server-fetched follow-state for this ad's employer. `undefined` =
   * anonymous/guest → the follow toggle is not rendered. When present, the toggle renders only if
   * `followable` (the ad carries an employer org.nr — B2-null ads have no dead affordance); a non-null
   * `companyWatchId` means the user already follows it. Server-fetched in the page handler alongside
   * `initialSaved`/`initialApplied` (the raw org.nr never reaches the client).
   */
  followState?: CompanyFollowState;
  /**
   * F4-16 (ADR 0076, CTO D3) — matchnings-detalj mot användarens profil.
   * `undefined`/`null` = ingen sektion renderas (anonym / ingen träffdata /
   * gäst — frånvaro, ej teater, ADR 0053). Server-fetchad parallellt i
   * page-handlern (parity initialSaved/initialApplied).
   */
  match?: JobAdMatchDetail | null;
  /**
   * Spår 3 PR-D — label → ort-granularitet (kommun/län) för match-sektionens
   * RegionFit-bevis. Härleds FE-side ur taxonomin i page-handlern (architect
   * NOTE-2) och vidarebefordras till JobAdMatchSection. Utelämnad → generisk
   * bevisform (degraderad taxonomi).
   */
  ortGranularityByLabel?: Record<string, OrtGranularity>;
  /**
   * #593 (#446-uppföljning, #311) — antalet av den inloggade användarens EGNA tidigare (inskickade)
   * ansökningar till annonsens arbetsgivare (samma org.nr), server-resolverat via
   * `getEmployerApplicationCounts` (#446). `undefined`/0 → renderas EJ (anonym/gäst, eller inga tidigare
   * ansökningar — POSITIVE-ONLY, paritet #446-kort-badgen). Till skillnad från /jobb-list-kortet (ETT
   * ytter-`<Link>` → nästlad länk ogiltig, B1/Fork 3C) har detaljvyn inget ytter-ankare, så den renderas
   * som en `<Link>` till `/foretag#ansokningshistorik`. Rent heltal; INGET org.nr i text/attribut/URL
   * (CLAUDE.md §5 — enskild firma = personnummer).
   *
   * <para>#824 PR 4 — antalet är ett GOLV. Predikatet faller på FRÅNVARO av arbetsgivar-identitet
   * (`.Where(r => r.OrgNr != null)`), tre vägar: ingen annons alls (manuell ansökan, `JobAdId == null`),
   * en annons som aldrig bar org.nr, eller ett org.nr som purgats med `raw_payload` (#824-mekanismen).
   * Copyn bär därför båda halvorna av hedgen på den här ytan (golv-markör på siffran +
   * ofullständigheten hos sammanställningen länken leder till) — ett oreserverat faktapåstående till den
   * registrerade om hennes egna uppgifter är en Art. 5(1)(a)/(d)-defekt, inte en formuleringsfråga.</para>
   */
  previousApplicationCount?: number;
}

// Active/Archived → .jp-pill-variant. Speglar
// JOB_AD_STATUS_BADGE_VARIANT-semantiken (Active=success, Archived=neutral)
// men mot v3 .jp-pill-systemet (HANDOVER §5.7). Expired retirerades i #886.
const STATUS_PILL_CLASS: Record<JobAdStatus, string> = {
  Active: "jp-pill jp-pill--success",
  Archived: "jp-pill jp-pill--neutral",
};

export function JobAdDetail({
  jobAd,
  headless = false,
  initialSaved,
  initialApplied,
  followState,
  match,
  ortGranularityByLabel,
  previousApplicationCount,
}: JobAdDetailProps) {
  // Synchronous next-intl translators — keep JobAdDetail a non-async RSC (it is
  // shared by the full page and the @modal serialized slot, with sync tests).
  const t = useTranslations("jobads.enums");
  const tUi = useTranslations("jobads.ui");
  const format = useFormatter();
  // Typ-narrowing-pattern: bind till en `userActions`-konst som är non-null
  // när BÅDA props är definierade. Eliminerar `!`-suppressions i JSX nedan
  // (code-reviewer Minor 6).
  const userActions =
    initialSaved !== undefined && initialApplied !== undefined
      ? { saved: initialSaved, applied: initialApplied }
      : null;
  const publishedAt = formatDate(format, jobAd.publishedAt) ?? "";
  const expiresAt = formatDate(format, jobAd.expiresAt);

  return (
    <>
      {!headless && (
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h1 className="jp-modal__title">{jobAd.title}</h1>
            <p className="jp-modal__company">{jobAd.companyName}</p>
          </div>
          <span className={STATUS_PILL_CLASS[jobAd.status]}>
            <span className="jp-pill__dot" aria-hidden="true" />
            {jobAdStatusLabel(t, jobAd.status)}
          </span>
        </header>
      )}

      <div className="jp-modal__body">
        {headless && (
          <span className={STATUS_PILL_CLASS[jobAd.status]} style={{ alignSelf: "flex-start" }}>
            <span className="jp-pill__dot" aria-hidden="true" />
            {jobAdStatusLabel(t, jobAd.status)}
          </span>
        )}

        <dl className="jp-modal__metarow">
          <div className="jp-modal__metaitem">
            <dt>{tUi("detail.published")}</dt>
            <dd>{publishedAt}</dd>
          </div>
          {expiresAt && (
            <div className="jp-modal__metaitem">
              <dt>{tUi("detail.lastApplicationDay")}</dt>
              <dd>{expiresAt}</dd>
            </div>
          )}
          <div className="jp-modal__metaitem">
            <dt>{tUi("detail.adId")}</dt>
            <dd>{jobAd.id}</dd>
          </div>
        </dl>

        {/* #593 (#446-uppföljning) — "Du har MINST X tidigare ansökningar till detta företag.
            Sammanställningen kan vara ofullständig." som LÄNK till ansökningshistoriken. POSITIVE-ONLY
            (bara > 0). Giltig länk här (ingen ytter-`<a>` på detaljvyn, till skillnad från list-kortet —
            B1/Fork 3C). Rent heltal, inget org.nr. #824 PR 4: golv-markör + ofullständighet, båda
            halvorna — se prop-doccen ovan. */}
        {previousApplicationCount != null && previousApplicationCount > 0 && (
          <p className="text-body-sm">
            {tUi("detail.previousApplications", { count: previousApplicationCount })}{" "}
            {/* Understrykning i vilo-läge (design-reviewer, WCAG 1.4.1/F73): en in-prose-länk får inte
                skiljas från brödtexten enbart med färg (<3:1 mot body-ink i båda teman). Basankaret
                (globals.css a:not(.jp-btn)) sätter bara color; text-body-sm ärver ingen understrykning
                (till skillnad från .jp-muted a). Tailwind-utility, ingen globals.css-touch (hotspot). */}
            <Link
              href="/foretag#ansokningshistorik"
              className="underline underline-offset-2"
            >
              {tUi("detail.previousApplicationsLink")}
            </Link>
          </p>
        )}

        {/* F4-16 — matchnings-sektionen ovanför Annonsbeskrivning (design §2.A:
            "passar jobbet mig" är frågan modalen öppnas för → före annons-prosan).
            Renderas bara när matchdata finns (anonym/gäst → match=undefined → null). */}
        {match != null && (
          <JobAdMatchSection
            match={match}
            ortGranularityByLabel={ortGranularityByLabel}
          />
        )}

        <div>
          <div className="jp-eyebrow mb-2">
            {tUi("detail.description")}
          </div>
          <div id="jp-modal-desc" className="jp-modal__description">
            {formatAdDescription(jobAd.description)}
          </div>
        </div>
      </div>

      <div className="jp-modal__foot">
        <span className="jp-modal__foot__spacer" />
        {userActions && (
          <>
            <SaveJobAdToggle jobAdId={jobAd.id} initialSaved={userActions.saved} />
            <HarAnsoktButton jobAdId={jobAd.id} initialApplied={userActions.applied} />
            {/* #455 — follow the employer. Rendered only when the ad carries an org.nr (followable);
                a B2-null ad has no dead affordance (CTO deldom 5, civic-utility). */}
            {followState?.followable && (
              <FollowCompanyToggle
                jobAdId={jobAd.id}
                initialCompanyWatchId={followState.companyWatchId}
              />
            )}
          </>
        )}
        {jobAd.url && (
          <a
            href={jobAd.url}
            target="_blank"
            rel="noopener noreferrer"
            className="jp-btn jp-btn--secondary"
          >
            <ExternalLink size={14} aria-hidden="true" /> {tUi("detail.openAd")}
          </a>
        )}
      </div>
      {userActions?.applied && (
        <p className="jp-muted">
          {tUi("detail.appliedNotice")}{" "}
          <Link href="/ansokningar">
            {tUi("detail.appliedNoticeLink")}
          </Link>
          .
        </p>
      )}
    </>
  );
}
