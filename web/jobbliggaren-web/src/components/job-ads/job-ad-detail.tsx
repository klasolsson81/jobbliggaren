import Link from "next/link";
import { InformationLink } from "@/components/information/InformationLink";
import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import type { AdContactDto, JobAdDetailDto } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import type { CompanyFollowState } from "@/lib/dto/company-follows";
import type { OrtGranularity } from "@/lib/job-ads/ort-granularity";
import { SaveJobAdToggle } from "@/components/saved-job-ads/save-job-ad-toggle";
import { HarAnsoktButton } from "@/components/applications/har-ansokt-button";
import { FollowCompanyToggle } from "@/components/company-follows/follow-company-toggle";
import { JobAdMatchSection } from "./job-ad-match-section";
import { RecruiterContactBlock } from "./recruiter-contact-block";
import { AdDescriptionExcerpt } from "./ad-description-excerpt";
import { JobAdDetailMeta } from "./job-ad-detail-meta";
import { formatAdDescription } from "./format-ad-description";
import contactLinkStyles from "./recruiter-contact-link.module.css";

/**
 * JobAdDetail — ren presentational Server Component (ingen "use client"). Delas av både fullsida
 * (`/jobb/[id]`) och jobbmodalen (`@modal/(.)jobb/[id]`) per ADR 0053 (en presentations-komponent,
 * två kontexter — DRY-positiv konsekvens). Dess klientöar är utdraget och sidfotens knappar.
 *
 * Fältsetet är komponentens eget under DESIGN.md §8 (ADR 0053 Amendment 2026-09-26, #1828; formen
 * Amendment 2026-10-03, #1963).
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
   * När true renderas titel/företag/datum i modal-headern av anroparen
   * (JobAdModalShell), så detaljen utelämnar sin egen rubrik-header.
   * Fullsidan sätter false och äger rubriken själv.
   */
  headless?: boolean;
  /**
   * The intercepted modal, whose title is the dialog's h2: the body is a named region a keyboard can
   * reach and scroll, and the ad text's headings sit at h3. On a page the title is the h1 and the
   * headings sit at h2 (#1965, #1966).
   */
  inModal?: boolean;
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
   * Ort-rad. Härleds FE-side ur taxonomin i page-handlern (architect NOTE-2) och
   * vidarebefordras till JobAdMatchSection.
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
   * recruiter). Defaults to [] → no contact area and no notice (#1944). A derived
   * entry is labelled as coming from the ad text; declared entries are not (R1(b)).
   */
  contacts?: readonly AdContactDto[];
}

export function JobAdDetail({
  jobAd,
  headless = false,
  inModal = false,
  initialSaved,
  initialApplied,
  followState,
  match,
  ortGranularityByConceptId,
  previousApplicationCount,
  contacts = [],
}: JobAdDetailProps) {
  // Synchronous next-intl translator — keep JobAdDetail a non-async RSC (it is
  // shared by the full page and the @modal serialized slot, with sync tests).
  const tUi = useTranslations("jobads.ui");
  // Typ-narrowing-pattern: bind till en `userActions`-konst som är non-null
  // när BÅDA props är definierade. Eliminerar `!`-suppressions i JSX nedan
  // (code-reviewer Minor 6).
  const userActions =
    initialSaved !== undefined && initialApplied !== undefined
      ? { saved: initialSaved, applied: initialApplied }
      : null;

  return (
    <>
      {!headless && (
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h1 tabIndex={-1} className="jp-modal__title">{jobAd.title}</h1>
            <p className="jp-modal__company">{jobAd.companyName}</p>
            <JobAdDetailMeta jobAd={jobAd} />
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

      <div
        className="jp-modal__body"
        data-information-scroll={inModal ? "job-modal-body" : undefined}
        tabIndex={inModal ? 0 : undefined}
        role={inModal ? "region" : undefined}
        aria-label={inModal ? tUi("detail.bodyLabel") : undefined}
      >
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
          <AdDescriptionExcerpt
            snapshotId={jobAd.id}
            showFullLabel={tUi("detail.showFullAd")}
            showLessLabel={tUi("detail.showLess")}
          >
            {formatAdDescription(jobAd.description, inModal ? 3 : 2)}
          </AdDescriptionExcerpt>
        </section>

        {/* #842 PR4 + #1944 — the contact card and its notice exist only with a contact, and the
            notice sits directly after the card, never behind the excerpt (ADR 0144 row 10). */}
        {contacts.length > 0 && (
          <div className="flex flex-col gap-2">
            <RecruiterContactBlock contacts={contacts} variant="card" />
            <p className="jp-recruiter-notice">
              <InformationLink id={`information-job-${jobAd.id}`} href="/kontaktperson-i-annons" className={contactLinkStyles.link}>
                {tUi("detail.recruiterNoticeLink")}
              </InformationLink>
            </p>
          </div>
        )}
      </div>

      <div className="jp-modal__foot jp-modal__foot--split">
        {userActions && (
          <div className="jp-modal__footgroup">
            <SaveJobAdToggle jobAdId={jobAd.id} initialSaved={userActions.saved} />
            {/* #455 — follow the employer. Rendered only when the ad carries an org.nr (followable);
                a B2-null ad has no dead affordance (CTO deldom 5, civic-utility). */}
            {followState?.followable && (
              <FollowCompanyToggle
                jobAdId={jobAd.id}
                initialCompanyWatchId={followState.companyWatchId}
              />
            )}
          </div>
        )}
        <div className="jp-modal__footgroup jp-modal__footgroup--end">
          {userActions && (
            <HarAnsoktButton jobAdId={jobAd.id} initialApplied={userActions.applied} />
          )}
          {jobAd.url && (
            <a
              href={jobAd.url}
              target="_blank"
              rel="noopener noreferrer"
              className="jp-btn jp-btn--primary"
            >
              <ExternalLink size={14} aria-hidden="true" /> {tUi("detail.openAd")}
            </a>
          )}
        </div>
      </div>
    </>
  );
}
