import { useTranslations } from "next-intl";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import { CvProposedChange } from "@/components/resumes/cv-proposed-change";
import { categoryLabel } from "@/lib/resumes/review-labels";
import type {
  CvImprovementDto,
  ProposedChangeDto,
  RenderProfile,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

/**
 * CV-förbättringspanel (F4-10, propose-and-approve). RSC, display-only. Surfacerar
 * de deterministiska förbättringsförslagen: profil-växel, sedan ett kort per
 * rubrik-kategori med förslagen grupperade. Ingen opak totalsumma/score (Goodhart
 * — DTO-kommentaren) — en ren per-kategori-räkning ("3 förslag") är skanninfo, aldrig
 * ett 0–100-betyg. Ingen tillämpa/godkänn/avvisa-knapp, ingen kryssruta, ingen
 * klient-ö som muterar (CLAUDE.md §5).
 *
 * När `improvements` är null (förslagen kunde inte laddas) degraderas vyn civilt —
 * sid-skalet står kvar, panelen ersätts av en lugn notis (sidan 404:ar aldrig på
 * ett förbättrings-fel; det är en sekundär, civilt degraderad hämtning).
 */

/** De kategorier som har minst ett förslag, i DTO:ns ordning (stabil, deterministisk). */
function categoriesWithChanges(
  changes: ReadonlyArray<ProposedChangeDto>,
): RubricCategory[] {
  const seen = new Set<RubricCategory>();
  const ordered: RubricCategory[] = [];
  for (const change of changes) {
    if (!seen.has(change.category)) {
      seen.add(change.category);
      ordered.push(change.category);
    }
  }
  return ordered;
}

export function CvImprovePanel({
  improvements,
  parsedId,
  profile,
}: {
  improvements: CvImprovementDto | null;
  parsedId: string;
  profile: RenderProfile;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const basePath = `/cv/granska/${parsedId}/forbattra`;

  if (improvements === null) {
    return (
      <section className="jp-cvreview" aria-labelledby="cvimprove-title">
        <h2 id="cvimprove-title" className="jp-cvreview__title">
          {t("improve.title")}
        </h2>
        <div className="jp-cvreview__profile">
          <CvProfileToggle
            parsedId={parsedId}
            profile={profile}
            basePath={basePath}
          />
        </div>
        <p className="jp-cvreview__unavailable" role="status">
          {t("improve.unavailable")}
        </p>
      </section>
    );
  }

  const orderedCategories = categoriesWithChanges(improvements.changes);

  return (
    <section className="jp-cvreview" aria-labelledby="cvimprove-title">
      <h2 id="cvimprove-title" className="jp-cvreview__title">
        {t("improve.title")}
      </h2>

      <div className="jp-cvreview__profile">
        <CvProfileToggle
          parsedId={parsedId}
          profile={profile}
          basePath={basePath}
        />
      </div>

      {improvements.changes.length === 0 ? (
        <p className="jp-improve__empty">{t("improve.empty")}</p>
      ) : (
        <div className="jp-cvreview__categories">
          {orderedCategories.map((category) => {
            const changes = improvements.changes.filter(
              (change) => change.category === category,
            );
            return (
              <Card key={category}>
                <CardHeader>
                  <CardTitle asChild>
                    <h3>{categoryLabel(tEnum, category)}</h3>
                  </CardTitle>
                  <p className="jp-improve__count">
                    {t("improve.count", { count: changes.length })}
                  </p>
                </CardHeader>
                <CardContent>
                  <div className="jp-improve__changes">
                    {changes.map((change, index) => (
                      <CvProposedChange
                        // targetId är en öppen sträng (ingen unikhetsgaranti) →
                        // komposit-nyckel med index hindrar React-varning vid
                        // ev. dubbletter.
                        key={`${change.targetId}-${index}`}
                        change={change}
                      />
                    ))}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}

      <p className="jp-improve__versions">
        <span>{t("improve.rubricVersion", { version: improvements.rubricVersion })}</span>
        <span>{t("improve.clicheVersion", { version: improvements.clicheListVersion })}</span>
        <span>{t("improve.verbVersion", { version: improvements.verbMappingVersion })}</span>
      </p>
    </section>
  );
}
