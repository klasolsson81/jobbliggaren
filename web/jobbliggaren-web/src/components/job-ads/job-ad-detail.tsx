import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import { jobAdStatusLabel } from "@/lib/job-ads/status";
import { formatDate } from "@/lib/i18n/format";
import type { AdContactDto, JobAdDetailDto } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import type { CompanyFollowState } from "@/lib/dto/company-follows";
import type { OrtGranularity } from "@/lib/job-ads/ort-granularity";
import { SaveJobAdToggle } from "@/components/saved-job-ads/save-job-ad-toggle";
import { HarAnsoktButton } from "@/components/applications/har-ansokt-button";
import { FollowCompanyToggle } from "@/components/company-follows/follow-company-toggle";
import { JobAdMatchSection } from "./job-ad-match-section";
import { RecruiterContactBlock } from "./recruiter-contact-block";
import { formatAdDescription } from "./format-ad-description";

/**
 * JobAdDetail — ren presentational Server Component (ingen "use client",
 * noll interaktivitet). Delas av både fullsida (`/jobb/[id]`) och
 * jobbmodalen (`@modal/(.)jobb/[id]`) per ADR 0053 (en presentations-
 * komponent, två kontexter — DRY-positiv konsekvens).
 *
 * Fältsetet är komponentens eget under DESIGN.md §8 (ADR 0053 Amendment
 * 2026-09-26, #1828).
 */

interface JobAdDetailProps {
  /**
   * #745 — the DETAIL projection minus its `contacts` block (contacts arrive via the
   * dedicated `contacts` prop below, so they are `Omit`-ted here to avoid a redundant
   * second source). Typed against `JobAdDetailDto` — NOT the LIST type `JobAdDto`, which
   * since #745 no longer carries `description` (the list wire dropped the ad body). The
   * detail pages already hold a `JobAdDetailDto` from `getJobAd`; it is assignable here.
   */
  jobAd: Omit<JobAdDetailDto, "contacts">;
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
   * Spår 3 PR-D — conceptId → ort-granularitet (kommun/län) för match-sektionens
   * RegionFit-bevis. Härleds FE-side ur taxonomin i page-handlern (architect
   * NOTE-2) och vidarebefordras till JobAdMatchSection. Utelämnad → generisk
   * bevisform.
   */
  ortGranularityByConceptId?: Record<string, OrtGranularity>;
  /**
   * #593 (#446-uppföljning, #311) — antalet av den inloggade användarens EGNA tidigare (inskickade)
   * ansökningar till annonsens arbetsgivare (samma org.nr), server-resolverat via
   * `getEmployerApplicationCounts` (#446). `undefined`/0 → renderas EJ (anonym/gäst, eller inga tidigare
   * ansökningar — POSITIVE-ONLY, paritet #446-kortet). Raden följs av en länk till `/foretag/historik`.
   * Rent heltal; INGET org.nr i text/attribut/URL (CLAUDE.md §5 — enskild firma = personnummer).
   *
   * <para>#824 PR 4 — antalet är ett GOLV. Predikatet faller på FRÅNVARO av arbetsgivar-identitet
   * (`.Where(r => r.OrgNr != null)`), tre vägar: ingen annons alls (manuell ansökan, `JobAdId == null`),
   * en annons som aldrig bar org.nr, eller ett org.nr som purgats med `raw_payload` (#824-mekanismen).
   * "Minst" styr därför talet (ADR 0144 D4 rad 9).</para>
   */
  previousApplicationCount?: number;
  /**
   * #842 PR4 — the ad's recruiter contacts (detail-only wire field). Optional
   * additive prop, same pattern as `initialSaved`/`match`/`previousApplicationCount`
   * above: the real detail pages pass `jobAd.contacts` (from the JobAdDetailDto
   * getJobAd now returns), the guest demo omits it (a sample ad never fabricates a
   * recruiter). Defaults to [] → RecruiterContactBlock self-hides. A derived
   * entry is labelled as coming from the ad text; declared entries are not (R1(b)).
   */
  contacts?: readonly AdContactDto[];
}

export function JobAdDetail({
  jobAd,
  headless = false,
  initialSaved,
  initialApplied,
  followState,
  match,
  ortGranularityByConceptId,
  previousApplicationCount,
  contacts = [],
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
            {/* #1000 (V1) — INGEN separat BEVAKAR-tagg i modal-headern. Den vore en
                load-time-snapshot (Server Component-prop) medan follow-knappen är ett
                live client-island som medvetet INTE revaliderar medan modalen är öppen
                (#993/#1004) → tagg och knapp skulle säga emot varandra efter klick
                (design-reviewer 2026-07-20, ADR 0047 status/handling). I modalen bär
                togglens label ("Bevakar företaget") + aria-pressed redan LIVE-tillståndet,
                så en tagg vore redundant + stale-benägen. BEVAKAR-taggen lever på
                list-KORTEN (alltid load-time-sann, inget per-kort-toggle). */}
          </div>
        </header>
      )}

      <div className="jp-modal__body">
        {/* The card's own meta form (`.jp-job__meta`, one rule for both surfaces). An
            active ad carries no pill: every ad /jobb lists is active; "Arkiverad" is
            information and leads the line. */}
        <div className="jp-job__meta">
          {jobAd.status === "Archived" && (
            <span className="jp-pill jp-pill--neutral">
              <span className="jp-pill__dot" aria-hidden="true" />
              {jobAdStatusLabel(t, jobAd.status)}
            </span>
          )}
          <span>
            {tUi("detail.published")} <b>{publishedAt}</b>
          </span>
          {expiresAt && (
            <span>
              {tUi("detail.lastApplicationDay")} <b>{expiresAt}</b>
            </span>
          )}
        </div>

        {/* #593 (#446-uppföljning) — räknaren + länk till ansökningshistoriken. POSITIVE-ONLY
            (bara > 0). Rent heltal, inget org.nr. */}
        {previousApplicationCount != null && previousApplicationCount > 0 && (
          <p className="text-body-sm">
            {tUi("detail.previousApplications", { count: previousApplicationCount })}{" "}
            {/* Understrykning i vilo-läge (design-reviewer, WCAG 1.4.1/F73): en in-prose-länk får inte
                skiljas från brödtexten enbart med färg (<3:1 mot body-ink i båda teman). Basankaret
                (globals.css a:not(.jp-btn)) sätter bara color; text-body-sm ärver ingen understrykning. */}
            <Link
              href="/foretag/historik"
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
            ortGranularityByConceptId={ortGranularityByConceptId}
          />
        )}

        <section aria-labelledby="jp-ad-description-title">
          <div id="jp-ad-description-title" className="jp-eyebrow mb-2">
            {tUi("detail.description")}
          </div>
          <div className="jp-modal__description">
            {formatAdDescription(jobAd.description)}
          </div>
        </section>

        {/* #842 PR4 — recruiter contact block. Self-hides when the ad carries no
            contacts; the guest demo omits the prop entirely. */}
        <RecruiterContactBlock contacts={contacts} />
        {/* #842 Tier A (ADR 0106) — Art. 14(5)(b): the public recruiter notice must be
            reachable from the ad detail, with or without contacts (ADR 0144 D4 row 10).
            A sibling after the block, never inside it: the block renders nothing for []. */}
        <p className="jp-recruiter-notice">
          <Link href="/kontaktperson-i-annons">
            {tUi("detail.recruiterNoticeLink")}
          </Link>
        </p>
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
        {userActions?.applied && (
          <p className="jp-modal__footnote">
            {tUi("detail.appliedNotice")}{" "}
            <Link href="/ansokningar">
              {tUi("detail.appliedNoticeLink")}
            </Link>
            .
          </p>
        )}
      </div>
    </>
  );
}
