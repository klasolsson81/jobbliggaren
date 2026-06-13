import { GUEST_MOCK } from "@/lib/guest/mock-data";

// F-Pre Punkt 5 — Gäst-CV-sida. Mockdata-grid, inga muterande knappar
// ("Nytt CV" hide:as per Klas-direktiv §F). Använder befintliga `.jp-card`
// + `.jp-tag`-klasser + Tailwind-tokens (paritet med /cv).
// design-reviewer M1 2026-05-24: inline-styles ersatta med utility-klasser
// + design-tokens.

export function GuestCvPage() {
  const { resumes } = GUEST_MOCK;

  return (
    <>
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">Demo-CV</div>
            <h1 className="jp-pagehero__title">CV</h1>
            <p className="jp-pagehero__lede">
              Exempel på CV-varianter. När du har konto kan du skapa egna,
              redigera fritt och ladda upp PDF.
            </p>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <div className="jp-cvgrid">
          {resumes.map((resume) => (
            <article
              key={resume.id}
              className="jp-card jp-guest-resume"
              aria-label={resume.title}
            >
              <header className="jp-guest-resume__head">
                <h2 className="jp-guest-resume__title">{resume.title}</h2>
                {resume.isPrimary && (
                  <span className="jp-tag jp-tag--brand">Primär</span>
                )}
              </header>

              <dl className="jp-guest-resume__meta">
                <dt>Språk</dt>
                <dd>{resume.language === "sv" ? "Svenska" : "Engelska"}</dd>
                {resume.latestRole && (
                  <>
                    <dt>Senaste roll</dt>
                    <dd>{resume.latestRole}</dd>
                  </>
                )}
                <dt>Sektioner</dt>
                <dd>{resume.sectionCount}</dd>
                <dt>Uppdaterad</dt>
                <dd>{resume.updatedAtLabel}</dd>
              </dl>

              <div className="jp-guest-resume__skills">
                {resume.topSkills.map((skill) => (
                  <span key={skill} className="jp-tag">
                    {skill}
                  </span>
                ))}
              </div>
            </article>
          ))}
        </div>
      </div>
    </>
  );
}
