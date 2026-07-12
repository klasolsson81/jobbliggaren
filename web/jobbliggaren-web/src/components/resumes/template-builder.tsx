"use client";

// "use client": interaktiv mallbyggare (Fas 4b PR-8b 8b.3). Kräver browser-API:er
// (fetch av binär PDF-blob, URL.createObjectURL/revokeObjectURL, AbortController),
// lokal val-state (useState) och useTransition för Server Action-skrivningen — inget
// av detta kan köras i en Server Component. Optionerna (mall/accent/täthet) och de
// två BE-auktoritativa fakta (per-mall atsSafe, accent-hex) kommer från katalogen
// som sidan (RSC) redan hämtat; ön härleder aldrig ATS-regeln eller hex själv (P5).

import {
  type CSSProperties,
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  useTransition,
} from "react";
import { useTranslations } from "next-intl";
import { RadioGroup as RadioGroupPrimitive } from "radix-ui";
import { Check, Info } from "lucide-react";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { Segment } from "@/components/ui/segment";
import { StatusPill } from "@/components/ui/status-pill";
import { TemplateSchematic } from "@/components/resumes/template-schematic";
import { updateTemplateOptionsAction } from "@/lib/actions/resumes";
import type {
  CvTemplateOptionsDto,
  TemplateCatalogDto,
} from "@/lib/dto/resumes";

interface TemplateBuilderProps {
  resumeId: string;
  /** De persisterade optionerna (styr initialt val + första ATS-etikett + första paint). */
  initialOptions: CvTemplateOptionsDto;
  /** Den slutna, BE-sourcade optionskatalogen (namn + per-mall atsSafe + accent-hex). */
  catalog: TemplateCatalogDto;
}

type PreviewStatus =
  | "loading"
  | "ready"
  | "error"
  | "rateLimited"
  | "notFound";

/** Default rate-limit-retry-fönster (sekunder) om 429-svarets body saknar ett
 *  parsbart värde — speglar backend-policyns fönster (paritet med `cv-preview.tsx`). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

/** Nyckel för att jämföra ett render:at PDF mot det nuvarande valet (font utelämnas:
 *  den bärs oförändrad och Visual-profilen renderar templaten, inte fonten). */
function selectionKey(template: string, accentColor: string, density: string): string {
  return `${template}|${accentColor}|${density}`;
}

export function TemplateBuilder({
  resumeId,
  initialOptions,
  catalog,
}: TemplateBuilderProps) {
  const t = useTranslations("pages.cv.mall");

  // Valbara optioner. TYPSNITT-kontrollen är deferrad (Klas 2026-07-12) → fontPair
  // hålls konstant på det persisterade värdet och skickas oförändrat vid Spara.
  const [template, setTemplate] = useState(initialOptions.template);
  const [accentColor, setAccentColor] = useState(initialOptions.accentColor);
  const [density, setDensity] = useState(initialOptions.density);
  const fontPair = initialOptions.fontPair;

  const initialKey = selectionKey(
    initialOptions.template,
    initialOptions.accentColor,
    initialOptions.density
  );
  const currentKey = selectionKey(template, accentColor, density);

  // Preview-state.
  const [previewStatus, setPreviewStatus] = useState<PreviewStatus>("loading");
  const [blobUrl, setBlobUrl] = useState<string | null>(null);
  const [previewSourceUrl, setPreviewSourceUrl] = useState<string | null>(null);
  const [retryAfterSeconds, setRetryAfterSeconds] = useState(
    DEFAULT_RETRY_AFTER_SECONDS
  );
  // Nyckeln som iframen just nu speglar. null tills första paint klar → "inte stale"
  // under initial laddning. Blir currentKey efter varje lyckad render.
  const [renderedKey, setRenderedKey] = useState<string | null>(null);
  const isStale = renderedKey !== null && renderedKey !== currentKey;

  // Spara-state (Server Action via useTransition).
  const [isSaving, startSaving] = useTransition();
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const abortRef = useRef<AbortController | null>(null);
  const blobUrlRef = useRef<string | null>(null);
  // Speglar det nu valda alternativet så en Server Action som är i luften kan upptäcka
  // att användaren hunnit byta val sedan klicket (se handleSave). Skrivs enbart ur
  // event-handlers (aldrig under render).
  const selectionRef = useRef(initialKey);

  const templateHeadingId = useId();
  const accentHeadingId = useId();
  const densityHeadingId = useId();
  const previewHeadingId = useId();
  // Bas för mallkortens etikett- och beskrivnings-id:n (aria-labelledby/-describedby).
  const cardIdBase = useId();

  // Applicerar en PDF-hämtning (persisterad eller efemär) → object-URL → iframe. Första
  // satsen är ett await → INGEN synkron setState (all setState ligger i await-fortsätt-
  // ningarna), så effekten kan anropa detta utan kaskad-render (react-hooks/set-state-
  // in-effect). Den synkrona "loading"-återställningen görs i event-handlern (tillåtet
  // där). Object-URL:er revokeras när de ersätts (ingen blob-läcka).
  const applyFetch = useCallback(
    async (url: string, keyToMark: string, signal: AbortSignal) => {
      try {
        const res = await fetch(url, { signal, cache: "no-store" });

        if (res.ok) {
          const blob = await res.blob();
          if (signal.aborted) return;
          if (blobUrlRef.current) URL.revokeObjectURL(blobUrlRef.current);
          const objectUrl = URL.createObjectURL(blob);
          blobUrlRef.current = objectUrl;
          setBlobUrl(objectUrl);
          setPreviewSourceUrl(url);
          setPreviewStatus("ready");
          setRenderedKey(keyToMark);
          return;
        }

        if (res.status === 429) {
          setRetryAfterSeconds(await readRetryAfterSeconds(res));
          setPreviewStatus("rateLimited");
          return;
        }
        if (res.status === 404) {
          setPreviewStatus("notFound");
          return;
        }
        setPreviewStatus("error");
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setPreviewStatus("error");
      }
    },
    []
  );

  // Startar en hämtning: avbryter ev. in-flight-fetch + ny controller. INGEN setState
  // här (synkront anropad ur effekten) — all setState ligger i applyFetch (async).
  const startFetch = useCallback(
    (url: string, keyToMark: string) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      void applyFetch(url, keyToMark, controller.signal);
    },
    [applyFetch]
  );

  // Första paint: den PERSISTERADE Visual-renderingen (befintlig endpoint) så
  // användaren ser sitt CV direkt utan att spendera en efemär render vid load.
  // Hämtningen schemaläggs på en macrotask (setTimeout 0) så den synkrona effekt-
  // kroppen aldrig setState:ar (react-hooks/set-state-in-effect; mönster från
  // job-ad-typeahead.tsx). previewStatus initieras redan till "loading".
  useEffect(() => {
    const id = setTimeout(() => {
      startFetch(
        `/api/cv/${encodeURIComponent(resumeId)}/preview?profile=Visual`,
        initialKey
      );
    }, 0);
    return () => {
      clearTimeout(id);
      abortRef.current?.abort();
      if (blobUrlRef.current) {
        URL.revokeObjectURL(blobUrlRef.current);
        blobUrlRef.current = null;
      }
    };
    // initialKey härleds ur initialOptions (stabil per mount); startFetch är stabil.
  }, [resumeId, startFetch, initialKey]);

  // "Uppdatera förhandsvisning": efemär render av de OSPARADE valen (font oförändrad).
  function handleUpdatePreview() {
    const params = new URLSearchParams({
      template,
      accent: accentColor,
      font: fontPair,
      density,
    });
    // Event-handler → synkron setState tillåten: visa spinner + rensa den inaktuella
    // iframe:n direkt medan den efemära renderingen hämtas.
    setPreviewStatus("loading");
    if (blobUrlRef.current) {
      URL.revokeObjectURL(blobUrlRef.current);
      blobUrlRef.current = null;
      setBlobUrl(null);
    }
    startFetch(
      `/api/cv/${encodeURIComponent(resumeId)}/render/preview?${params.toString()}`,
      currentKey
    );
  }

  // Vid varje valändring nollställs spara-kvittot (det gäller inte längre det nya valet)
  // och selectionRef följer med, så en pågående skrivning vet att valet flyttat sig.
  function selectTemplate(value: string) {
    setTemplate(value);
    selectionRef.current = selectionKey(value, accentColor, density);
    resetSaveFeedback();
  }
  function selectAccent(value: string) {
    setAccentColor(value);
    selectionRef.current = selectionKey(template, value, density);
    resetSaveFeedback();
  }
  function selectDensity(value: string) {
    setDensity(value);
    selectionRef.current = selectionKey(template, accentColor, value);
    resetSaveFeedback();
  }
  function resetSaveFeedback() {
    setSaved(false);
    setSaveError(null);
  }

  function handleSave() {
    resetSaveFeedback();
    // Valet som FAKTISKT skrivs. Server Action-anropet stänger över det här värdet,
    // så kvittot får bara gälla exakt det.
    const savedKey = currentKey;

    startSaving(async () => {
      const result = await updateTemplateOptionsAction(resumeId, {
        template,
        accentColor,
        fontPair,
        density,
      });

      // useTransition blockerar INTE inmatning: användaren kan hinna byta mall medan
      // skrivningen är i luften. Valbytet nollställer kvittot (resetSaveFeedback), men
      // utan den här grinden skulle transitionens sena setSaved(true) skriva tillbaka
      // det — och "Mallen sparad." skulle stå bredvid ett val som aldrig sparades. Ett
      // kvitto som ljuger är värre än inget kvitto. selectionRef läses (inte `currentKey`)
      // eftersom closuren bär värdet från klickögonblicket.
      if (selectionRef.current !== savedKey) return;

      if (!result.success) {
        setSaveError(result.error);
        return;
      }
      setSaved(true);
    });
  }

  // Etikett-resolver: katalogens namn → svensk i18n-etikett; okänt namn (framtida
  // katalogpost utan etikett ännu) faller civilt till råvärdet (paritet ResumeCard,
  // som narrowar via en allowlist). Katalog-namnen är öppna strängar medan next-intl
  // typar nyckeln som en literal-union, så nyckeln castas — `t.has` gate:ar mot ett
  // saknat värde så casten aldrig ger en MISSING_MESSAGE-krasch.
  function optionLabel(
    group: "templates" | "accents" | "densities",
    name: string
  ): string {
    const key = `${group}.${name}` as Parameters<typeof t>[0];
    return t.has(key) ? t(key) : name;
  }

  const selectedTemplate = catalog.templates.find((tpl) => tpl.name === template);
  // ATS-etiketten läser katalogens atsSafe för den VALDA mallen — ren konsumtion av
  // domänregeln, ALDRIG en FE-härledning (P5: den persisterade DTO:n och den här
  // per-render-etiketten delar en enda källa och kan aldrig motsäga varandra).
  const atsSafe = selectedTemplate?.atsSafe ?? false;

  // Den valda accentens hex ur KATALOGEN (den WCAG-gardade CvPalette). Aldrig
  // FE-härledd: en swatch eller schematik får aldrig visa en färg PDF:en inte har.
  // Okänt namn → katalogens första post; saknas katalogen helt bär CSS-varianten
  // sin egen fallback (--jp-ink-1), så resultatet blir bläck — aldrig fel färg.
  const accentHex =
    catalog.accents.find((a) => a.name === accentColor)?.hex ??
    catalog.accents[0]?.hex;

  return (
    <div className="jp-mallbuilder">
      {/* Vänster: de tre optionsgrupperna + ATS-utfallet + Spara. */}
      <div className="jp-mallbuilder__options">
        <section aria-labelledby={templateHeadingId} className="jp-mallsec">
          <h2 id={templateHeadingId} className="jp-mallsec__title">
            {t("templateGroupLabel")}
          </h2>

          {/* Radix-primitiven (samma som ui/radio-group.tsx bygger på) ger
              role=radiogroup/radio, aria-checked, roving tabindex och piltangenter.
              Vi använder primitiven direkt eftersom ui/radio-group.tsx hårdkodar en
              label + prick-layout som ett kort inte kan bära — den lämnas orörd så
              inga andra konsumenter regrerar.
              Katalogens hex sätts EN gång på gruppen som datakanal; varje schematik
              läser den via fill: var(--jp-mallcard-accent) → swatch och schematik
              kan aldrig visa olika färg. */}
          <RadioGroupPrimitive.Root
            value={template}
            onValueChange={selectTemplate}
            aria-labelledby={templateHeadingId}
            className="jp-mallgrid"
            style={
              accentHex
                ? ({ "--jp-mallcard-accent": accentHex } as CSSProperties)
                : undefined
            }
          >
            {catalog.templates.map((tpl) => {
              const labelId = `${cardIdBase}-${tpl.name}-label`;
              const descId = `${cardIdBase}-${tpl.name}-desc`;
              const descKey =
                `templateDescriptions.${tpl.name}` as Parameters<typeof t>[0];
              // Okänd (framtida) katalogmall får ingen påhittad beskrivning.
              const hasDesc = t.has(descKey);

              return (
                <RadioGroupPrimitive.Item
                  key={tpl.name}
                  id={`mall-template-${tpl.name}`}
                  value={tpl.name}
                  className="jp-mallcard"
                  // Namnet = ENBART etiketten (aria-labelledby vinner över innehållet),
                  // beskrivningen är en description — annars skulle den svälla det
                  // tillgängliga namnet och göra radion svår att adressera.
                  aria-labelledby={labelId}
                  aria-describedby={hasDesc ? descId : undefined}
                >
                  <span className="jp-mallcard__figure">
                    <TemplateSchematic template={tpl.name} />
                  </span>
                  <span className="jp-mallcard__labelrow">
                    <span className="jp-mallcard__ind" aria-hidden="true">
                      <RadioGroupPrimitive.Indicator>
                        <Check size={12} strokeWidth={3.2} />
                      </RadioGroupPrimitive.Indicator>
                    </span>
                    <span id={labelId} className="jp-mallcard__label">
                      {optionLabel("templates", tpl.name)}
                    </span>
                  </span>
                  {hasDesc && (
                    <span id={descId} className="jp-mallcard__desc">
                      {t(descKey)}
                    </span>
                  )}
                </RadioGroupPrimitive.Item>
              );
            })}
          </RadioGroupPrimitive.Root>

          {/* Anti-överdrifts-raden: en schematik är ett strukturdiagram, inte en preview. */}
          <p className="jp-mallsec__hint">{t("schematicNote")}</p>

          {/* ATS-utfallet hör till den VALDA mallen → det bor i mall-sektionen.
              Live-region: Radix auto-checkar vid piltangent-fokus, så en tangentbords-
              användare byter mall utan att lämna gruppen. Utan aria-live hör hen bara
              "Mörk panel, alternativknapp, markerad" — medan sidans enda förtroende-
              bärande påstående tyst vänder från "Klarar ATS-granskning" till "Utformad
              för läsning". Det utfallet måste annonseras, inte bara visas. */}
          <div className="jp-mallats" role="status" aria-live="polite">
            <StatusPill
              tone={atsSafe ? "success" : "neutral"}
              className="jp-mallats__pill"
            >
              {atsSafe ? t("atsSafeLabel") : t("atsUnsafeLabel")}
            </StatusPill>
            <p className="jp-mallats__hint">
              {atsSafe ? t("atsSafeHint") : t("atsUnsafeHint")}
            </p>
          </div>
        </section>

        <section aria-labelledby={accentHeadingId} className="jp-mallsec">
          <h2 id={accentHeadingId} className="jp-mallsec__title">
            {t("accentGroupLabel")}
          </h2>
          <RadioGroupPrimitive.Root
            value={accentColor}
            onValueChange={selectAccent}
            aria-labelledby={accentHeadingId}
            className="jp-swatchrow"
          >
            {catalog.accents.map((accent) => (
              <RadioGroupPrimitive.Item
                key={accent.name}
                id={`mall-accent-${accent.name}`}
                value={accent.name}
                className="jp-swatch"
              >
                {/* Färgen kommer ur katalogens hex (WCAG-gardad palett). Pricken är
                    dekor (aria-hidden) — etiketten bär betydelsen för skärmläsaren. */}
                <span
                  className="jp-swatch__dot"
                  aria-hidden="true"
                  style={{ backgroundColor: accent.hex }}
                >
                  <RadioGroupPrimitive.Indicator className="jp-swatch__check">
                    <Check size={14} strokeWidth={3.2} />
                  </RadioGroupPrimitive.Indicator>
                </span>
                <span className="jp-swatch__label">
                  {optionLabel("accents", accent.name)}
                </span>
              </RadioGroupPrimitive.Item>
            ))}
          </RadioGroupPrimitive.Root>
          <p className="jp-mallsec__hint">{t("accentHint")}</p>
        </section>

        <section aria-labelledby={densityHeadingId} className="jp-mallsec">
          <h2 id={densityHeadingId} className="jp-mallsec__title">
            {t("densityGroupLabel")}
          </h2>
          {/* Segment bär redan hela W3C-radiogroup-mönstret (roving tabindex,
              piltangenter, aria-checked) — ingen ny a11y-maskin uppfinns här. */}
          <Segment<string>
            value={density}
            onChange={selectDensity}
            aria-label={t("densityGroupLabel")}
            options={catalog.densities.map((d) => ({
              value: d.name,
              label: optionLabel("densities", d.name),
            }))}
          />
          <p className="jp-mallsec__hint">{t("densityHint")}</p>
        </section>

        <div className="jp-mallsave">
          <button
            type="button"
            className="jp-btn jp-btn--primary"
            onClick={handleSave}
            disabled={isSaving}
          >
            {isSaving ? t("savePending") : t("save")}
          </button>
          {/* Live-regionen är PERMANENT monterad och får sitt innehåll senare. En artig
              region som skapas samtidigt som sin text missas av flera skärmläsare — de
              annonserar bara ändringar i en region som redan fanns. (role="alert" klarar
              sig vid insättning, men vi håller båda i samma alltid-närvarande behållare
              så kvittot och felet aldrig kan tappas bort.) */}
          <div role="status" aria-live="polite">
            {saved && !saveError && <p className="jp-mallsave__ok">{t("saved")}</p>}
          </div>
          {saveError && (
            <p className="jp-mallsave__err" role="alert">
              {saveError}
            </p>
          )}
        </div>
      </div>

      {/* Höger: förhandsvisningen i ett inramat kort (huvud / stale / kropp / fot). */}
      <aside className="jp-mallbuilder__preview" aria-labelledby={previewHeadingId}>
        <div className="jp-mallpreview">
          <div className="jp-mallpreview__head">
            <h2 id={previewHeadingId} className="jp-mallpreview__title">
              {t("previewHeading")}
            </h2>
            <button
              type="button"
              className="jp-btn jp-btn--secondary jp-btn--sm"
              onClick={handleUpdatePreview}
              disabled={previewStatus === "loading"}
            >
              {t("updatePreview")}
            </button>
          </div>

          {/* Permanent monterad artig live-region (se spara-kvittot ovan). Stale-remsan
              visas BARA när förhandsvisningen faktiskt står och är inaktuell: vid 429/
              fel/saknad bär platshållaren redan ett meddelande, och två samtidiga texter
              där den ena säger "välj Uppdatera" och den andra "du har gjort för många
              förfrågningar" motsäger varandra. */}
          <div role="status" aria-live="polite">
            {isStale && previewStatus === "ready" && (
              <p className="jp-mallpreview__stale">
                <Info size={15} aria-hidden="true" />
                <span>{t("previewStale")}</span>
              </p>
            )}
          </div>

          <div className="jp-mallpreview__body">
            {previewStatus === "ready" && blobUrl ? (
              <iframe
                src={blobUrl}
                title={t("iframeTitle")}
                className="jp-mallframe"
              />
            ) : (
              <div className="jp-mallpreview__placeholder">
                {previewStatus === "loading" && (
                  <>
                    <BrandSpinner size={48} label={t("previewLoadingLabel")} />
                    <p className="text-body-sm" aria-hidden="true">
                      {t("previewLoading")}
                    </p>
                  </>
                )}
                {/* Live-regioner (WCAG 4.1.3): en tangentbords-/skärmläsaranvändare måste
                    få veta att en "Uppdatera"-omtrigga misslyckades. Fel = assertiv
                    (role=alert, annonseras pålitligt vid insättning); rate-limit/saknad =
                    artig och ligger därför i en permanent monterad region nedan. */}
                {previewStatus === "error" && (
                  <p className="max-w-[68ch] text-body" role="alert">
                    {t("previewError")}
                  </p>
                )}
                <div role="status" aria-live="polite">
                  {previewStatus === "rateLimited" && (
                    <p className="max-w-[68ch] text-body">
                      {t("previewRateLimited", { seconds: retryAfterSeconds })}
                    </p>
                  )}
                  {previewStatus === "notFound" && (
                    <p className="max-w-[68ch] text-body">
                      {t("previewNotFound")}
                    </p>
                  )}
                </div>
              </div>
            )}
          </div>

          {previewStatus === "ready" && previewSourceUrl && (
            // iOS Safari renderar inte alltid PDF i iframe — den här länken är den
            // riktiga mobil-vägen, inte en dekoration.
            <p className="jp-mallpreview__foot">
              <a href={previewSourceUrl} target="_blank" rel="noopener noreferrer">
                {t("openInNewTab")}
              </a>
            </p>
          )}
        </div>
      </aside>
    </div>
  );
}

/**
 * Läser `retryAfterSeconds` ur 429-svarets JSON-body (BFF:n speglar render-routens
 * form). Faller till 60s om body saknas/inte är parsbar — samma default-fönster som
 * backendens rate-limit-policy (paritet med `cv-preview.tsx`).
 */
async function readRetryAfterSeconds(res: Response): Promise<number> {
  try {
    const data: unknown = await res.json();
    if (
      typeof data === "object" &&
      data !== null &&
      "retryAfterSeconds" in data &&
      typeof (data as { retryAfterSeconds: unknown }).retryAfterSeconds ===
        "number"
    ) {
      return (data as { retryAfterSeconds: number }).retryAfterSeconds;
    }
  } catch {
    // Ignorera parse-fel — faller till default.
  }
  return DEFAULT_RETRY_AFTER_SECONDS;
}
