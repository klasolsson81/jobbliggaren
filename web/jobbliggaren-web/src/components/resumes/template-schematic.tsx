// Mini-schematik för mallkorten i mallbyggaren (#820). Ren presentation — ingen
// klient-state, inga browser-API:er (ingen "use client" behövs; ön importerar den).
//
// ÄRLIGHETSKONTRAKT (pinnat i template-schematic.test.tsx): schematiken får bara
// påstå det renderaren faktiskt ritar. Geometrin är en transkription av
// CvDocumentComposer (ComposeSingleColumn / ComposeMorkPanel), vilket gör
// "stämmer schematiken?" till en diffbar fråga i stället för en smaksak.
//
//   1. INGET foto. Renderaren emitterar ingen fotoyta (DPIA-grindad till PR-10) —
//      en avatarcirkel i Mörk panel vore den enklaste lögnen att råka berätta.
//   2. INGA bokstavsformer. Bara abstrakta staplar. Modern och Klassisk löser båda
//      ut till den paketerade Lato idag, och TYPSNITT saknar kontroll (deferrad) —
//      en serif/sans-mock skulle lova ett typsnittsval som inte finns.
//   3. Tätheten ritas INTE. En 12-raders miniatyr kan inte ärligt bära 0,85/1,0/1,2
//      i radavstånd; den riktiga förhandsvisningen gör det. Hjälptexten bär det.
//   4. Accentfärgen är det ENDA färgpåståendet, och den kommer ur katalogens hex
//      (AccentOptionDto.Hex → --jp-mallcard-accent, satt av kortgruppen).
//   5. Mörk panels panel ÄR accentfärgad (Background(accent)) — inte grå/svart.
//      Namnet säger "mörk", renderingen säger "accent"; schematiken följer renderingen.
//   6. Okänd mall → noll accent-element (samma fail-safe-hållning som AtsSafe => false):
//      en oklassificerad mall får inte göra något färgpåstående alls.

interface TemplateSchematicProps {
  /** Katalogens mallnamn (öppen sträng — en framtida BE-mall får fallback-skiss). */
  template: string;
}

/** Gemensam A4-proportion (120 x 170 ≈ 1:1.414) — samma som kortets figur-yta. */
const VIEW_BOX = "0 0 120 170";

export function TemplateSchematic({ template }: TemplateSchematicProps) {
  switch (template) {
    case "Klar":
      return <KlarSchematic />;
    case "Accentlinje":
      return <AccentlinjeSchematic />;
    case "MorkPanel":
      return <MorkPanelSchematic />;
    default:
      return <FallbackSchematic />;
  }
}

/**
 * Klar — en spalt. Namn, tunn accentlinje under huvudet, versala rubriker i accent
 * med accentfärgad understrykning över spaltbredden.
 */
function KlarSchematic() {
  return (
    <svg
      viewBox={VIEW_BOX}
      className="jp-schem"
      aria-hidden="true"
      focusable="false"
    >
      <rect className="jp-schem__paper" x="0" y="0" width="120" height="170" />
      <rect className="jp-schem__ink" x="14" y="16" width="46" height="6" rx="1" />
      <rect className="jp-schem__line" x="14" y="26" width="62" height="2.5" rx="1" />
      {/* Tunn accentlinje under huvudet (LineHorizontal(1) i accent). */}
      <rect className="jp-schem__accent" x="14" y="33" width="92" height="1.5" />
      {/* Rubrik: versal accenttext + understrykning (LineHorizontal(0.75)). */}
      <rect className="jp-schem__accent" x="14" y="42" width="26" height="4" rx="0.5" />
      <rect className="jp-schem__accent" x="14" y="48" width="92" height="1" />
      <rect className="jp-schem__line" x="14" y="54" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="60" width="84" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="66" width="66" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="14" y="78" width="30" height="4" rx="0.5" />
      <rect className="jp-schem__accent" x="14" y="84" width="92" height="1" />
      <rect className="jp-schem__line" x="14" y="90" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="96" width="78" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="102" width="88" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="14" y="114" width="22" height="4" rx="0.5" />
      <rect className="jp-schem__accent" x="14" y="120" width="92" height="1" />
      <rect className="jp-schem__line" x="14" y="126" width="70" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="132" width="86" height="2.5" rx="1" />
    </svg>
  );
}

/**
 * Accentlinje — en spalt. Accentfärgat streck FÖRE varje rubrik (ConstantItem(4)
 * .Background(accent)), rubriktext i accent utan understrykning, och accentfärgade
 * gruppetiketter i kompetenssektionen (groupNameColor = accent).
 */
function AccentlinjeSchematic() {
  return (
    <svg
      viewBox={VIEW_BOX}
      className="jp-schem"
      aria-hidden="true"
      focusable="false"
    >
      <rect className="jp-schem__paper" x="0" y="0" width="120" height="170" />
      <rect className="jp-schem__ink" x="14" y="16" width="46" height="6" rx="1" />
      <rect className="jp-schem__line" x="14" y="26" width="62" height="2.5" rx="1" />
      {/* Rubrik = streck + accentfärgad rubriktext. Ingen understrykning (till skillnad från Klar). */}
      <rect className="jp-schem__accent" x="14" y="40" width="3" height="8" />
      <rect className="jp-schem__accent" x="21" y="42" width="32" height="4.5" rx="0.5" />
      <rect className="jp-schem__line" x="14" y="54" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="60" width="82" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="66" width="70" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="14" y="78" width="3" height="8" />
      <rect className="jp-schem__accent" x="21" y="80" width="26" height="4.5" rx="0.5" />
      <rect className="jp-schem__line" x="14" y="92" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="98" width="76" height="2.5" rx="1" />
      {/* Kompetenser: accentfärgade gruppetiketter + rader i bläck. */}
      <rect className="jp-schem__accent" x="14" y="110" width="3" height="8" />
      <rect className="jp-schem__accent" x="21" y="112" width="34" height="4.5" rx="0.5" />
      <rect className="jp-schem__accent" x="14" y="124" width="20" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="38" y="124" width="68" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="14" y="131" width="16" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="34" y="131" width="72" height="2.5" rx="1" />
    </svg>
  );
}

/**
 * Mörk panel — två spalter. Sidopanelen går kant till kant i ACCENTFÄRGEN
 * (row.ConstantItem(188).Background(accent).ExtendVertical(); 188/595pt ≈ 32%)
 * med vit text; huvudspalten bär namn i bläck + accentfärgade rubriker.
 * INGEN fotocirkel — renderaren ritar inget foto (PR-10, DPIA-grind).
 */
function MorkPanelSchematic() {
  return (
    <svg
      viewBox={VIEW_BOX}
      className="jp-schem"
      aria-hidden="true"
      focusable="false"
    >
      <rect className="jp-schem__paper" x="0" y="0" width="120" height="170" />
      <rect className="jp-schem__accent" x="0" y="0" width="38" height="170" />
      {/* Sidopanel — vit text (KONTAKT / KOMPETENSER / SPRÅK). */}
      <rect className="jp-schem__panelink" x="6" y="18" width="20" height="3.5" rx="0.5" />
      <rect className="jp-schem__panelline" x="6" y="26" width="26" height="2" rx="1" />
      <rect className="jp-schem__panelline" x="6" y="31" width="22" height="2" rx="1" />
      <rect className="jp-schem__panelline" x="6" y="36" width="24" height="2" rx="1" />
      <rect className="jp-schem__panelink" x="6" y="50" width="24" height="3.5" rx="0.5" />
      <rect className="jp-schem__panelline" x="6" y="58" width="26" height="2" rx="1" />
      <rect className="jp-schem__panelline" x="6" y="63" width="20" height="2" rx="1" />
      <rect className="jp-schem__panelline" x="6" y="68" width="24" height="2" rx="1" />
      <rect className="jp-schem__panelink" x="6" y="82" width="16" height="3.5" rx="0.5" />
      <rect className="jp-schem__panelline" x="6" y="90" width="22" height="2" rx="1" />
      <rect className="jp-schem__panelline" x="6" y="95" width="18" height="2" rx="1" />
      {/* Huvudspalt. */}
      <rect className="jp-schem__ink" x="48" y="18" width="44" height="7" rx="1" />
      <rect className="jp-schem__accent" x="48" y="34" width="26" height="4" rx="0.5" />
      <rect className="jp-schem__line" x="48" y="42" width="58" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="48" y="48" width="52" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="48" y="60" width="30" height="4" rx="0.5" />
      <rect className="jp-schem__line" x="48" y="68" width="58" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="48" y="74" width="46" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="48" y="80" width="54" height="2.5" rx="1" />
      <rect className="jp-schem__accent" x="48" y="92" width="22" height="4" rx="0.5" />
      <rect className="jp-schem__line" x="48" y="100" width="58" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="48" y="106" width="44" height="2.5" rx="1" />
    </svg>
  );
}

/**
 * Fallback — en katalogmall vi inte känner igen (framtida BE-tillägg). Neutral
 * struktur utan ETT ENDA accent-element: vi vet inte hur den renderas, så vi gör
 * inget färgpåstående (paritet med den civila etikett-fallbacken i byggaren).
 */
function FallbackSchematic() {
  return (
    <svg
      viewBox={VIEW_BOX}
      className="jp-schem"
      aria-hidden="true"
      focusable="false"
    >
      <rect className="jp-schem__paper" x="0" y="0" width="120" height="170" />
      <rect className="jp-schem__ink" x="14" y="16" width="46" height="6" rx="1" />
      <rect className="jp-schem__line" x="14" y="26" width="62" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="42" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="48" width="84" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="54" width="66" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="66" width="92" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="72" width="78" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="78" width="88" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="90" width="70" height="2.5" rx="1" />
      <rect className="jp-schem__line" x="14" y="96" width="86" height="2.5" rx="1" />
    </svg>
  );
}
