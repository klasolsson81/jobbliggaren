/**
 * LandingFeatures — "Funktioner"-sektion i v3-stil (HANDOVER §7.1 punkt 3).
 *
 * Mono-key (left, 220px, uppercase navy-700) + brödtext (right). Ren RSC.
 *
 * FEATURES-array verbatim från `src-v3/landing.jsx` (prototyp-källan är
 * kontrakt enligt Klas pre-F6 Prompt 1 förkrav). Civic-utility-ton: ingen
 * ikon, ingen "Så funkar det"-numrerad cirkel, inga trust-pills.
 */

const FEATURES: ReadonlyArray<{ key: string; body: string }> = [
  {
    key: "Sökning",
    body: "Aktiva platsannonser från Platsbanken, fler källor planerade. Sortera efter CV-match, nyhet eller deadline.",
  },
  {
    key: "Pipeline",
    body: "Spåra varje ansökan från utkast till svar, genom intervjuer, erbjudanden och avslag. Inget tappas mellan stolarna.",
  },
  {
    key: "CV och brev",
    body: "Skapa och organisera flera CV-varianter. Generera personliga brev som matchar din ton. Du äger varje rad.",
  },
  {
    key: "Påminnelser",
    body: "Intervjuer och sista ansökningsdag fångas automatiskt från dina ansökningar, så du missar inga deadlines.",
  },
];

export function LandingFeatures() {
  return (
    <section className="jp-land-section jp-land-section--alt">
      <div className="jp-container">
        <div className="jp-land-section__head">
          <div className="jp-land-kicker">Funktioner</div>
          <h2 className="jp-land-section__title">
            Allt du behöver för att hålla ordning
          </h2>
        </div>
        <div className="jp-land-features">
          {FEATURES.map((f) => (
            <div key={f.key} className="jp-land-feature">
              <div className="jp-land-feature__key">{f.key}</div>
              <div className="jp-land-feature__val">{f.body}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
