"use client";

// "use client": interaktiv mallbyggare (Fas 4b PR-8b 8b.3). Kräver browser-API:er
// (fetch av binär PDF-blob, URL.createObjectURL/revokeObjectURL, AbortController),
// lokal val-state (useState) och useTransition för Server Action-skrivningen — inget
// av detta kan köras i en Server Component. Optionerna (mall/accent/täthet) och de
// två BE-auktoritativa fakta (per-mall atsSafe, accent-hex) kommer från katalogen
// som sidan (RSC) redan hämtat; ön härleder aldrig ATS-regeln eller hex själv (P5).

import {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  useTransition,
} from "react";
import { useTranslations } from "next-intl";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { StatusDot } from "@/components/ui/status-dot";
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

  const templateHeadingId = useId();
  const accentHeadingId = useId();
  const densityHeadingId = useId();

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

  // Vid varje valändring nollställs spara-kvittot (det gäller inte längre det nya valet).
  function selectTemplate(value: string) {
    setTemplate(value);
    resetSaveFeedback();
  }
  function selectAccent(value: string) {
    setAccentColor(value);
    resetSaveFeedback();
  }
  function selectDensity(value: string) {
    setDensity(value);
    resetSaveFeedback();
  }
  function resetSaveFeedback() {
    setSaved(false);
    setSaveError(null);
  }

  function handleSave() {
    resetSaveFeedback();
    startSaving(async () => {
      const result = await updateTemplateOptionsAction(resumeId, {
        template,
        accentColor,
        fontPair,
        density,
      });
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

  return (
    <div className="grid grid-cols-1 gap-8 lg:grid-cols-[minmax(0,360px)_minmax(0,1fr)]">
      {/* Vänster reglage: de tre optionsgrupperna + Spara. */}
      <div className="flex flex-col gap-8">
        <section
          aria-labelledby={templateHeadingId}
          className="flex flex-col gap-3"
        >
          <h2
            id={templateHeadingId}
            className="text-h3 font-medium text-text-primary"
          >
            {t("templateGroupLabel")}
          </h2>
          <RadioGroup
            value={template}
            onValueChange={selectTemplate}
            aria-labelledby={templateHeadingId}
          >
            {catalog.templates.map((tpl) => (
              <RadioGroupItem
                key={tpl.name}
                id={`mall-template-${tpl.name}`}
                value={tpl.name}
              >
                {optionLabel("templates", tpl.name)}
              </RadioGroupItem>
            ))}
          </RadioGroup>
        </section>

        <section
          aria-labelledby={accentHeadingId}
          className="flex flex-col gap-3"
        >
          <h2
            id={accentHeadingId}
            className="text-h3 font-medium text-text-primary"
          >
            {t("accentGroupLabel")}
          </h2>
          <RadioGroup
            value={accentColor}
            onValueChange={selectAccent}
            aria-labelledby={accentHeadingId}
          >
            {catalog.accents.map((accent) => (
              <RadioGroupItem
                key={accent.name}
                id={`mall-accent-${accent.name}`}
                value={accent.name}
              >
                <span className="inline-flex items-center gap-2">
                  {/* Swatch färgad från katalogens hex (den WCAG-vaktade paletten) —
                      dynamiskt data-värde, aria-hidden; namnet bär betydelsen. */}
                  <span
                    aria-hidden="true"
                    className="size-4 shrink-0 rounded-pill border border-border-default"
                    style={{ backgroundColor: accent.hex }}
                  />
                  {optionLabel("accents", accent.name)}
                </span>
              </RadioGroupItem>
            ))}
          </RadioGroup>
        </section>

        <section
          aria-labelledby={densityHeadingId}
          className="flex flex-col gap-3"
        >
          <h2
            id={densityHeadingId}
            className="text-h3 font-medium text-text-primary"
          >
            {t("densityGroupLabel")}
          </h2>
          <RadioGroup
            value={density}
            onValueChange={selectDensity}
            aria-labelledby={densityHeadingId}
          >
            {catalog.densities.map((d) => (
              <RadioGroupItem
                key={d.name}
                id={`mall-density-${d.name}`}
                value={d.name}
              >
                {optionLabel("densities", d.name)}
              </RadioGroupItem>
            ))}
          </RadioGroup>
        </section>

        <div className="flex flex-col gap-2 border-t border-border pt-6">
          <div>
            <Button type="button" onClick={handleSave} disabled={isSaving}>
              {isSaving ? t("savePending") : t("save")}
            </Button>
          </div>
          {saved && !saveError && (
            <p className="text-body-sm text-text-secondary" role="status">
              {t("saved")}
            </p>
          )}
          {saveError && (
            <p className="text-body-sm text-danger-700" role="alert">
              {saveError}
            </p>
          )}
        </div>
      </div>

      {/* Höger: ärlig ATS-etikett + förhandsvisning + Uppdatera-knapp. */}
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1">
          <StatusDot tone={atsSafe ? "success" : "neutral"}>
            {atsSafe ? t("atsSafeLabel") : t("atsUnsafeLabel")}
          </StatusDot>
          <p className="max-w-[68ch] text-body-sm text-text-secondary">
            {atsSafe ? t("atsSafeHint") : t("atsUnsafeHint")}
          </p>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-h3 font-medium text-text-primary">
            {t("previewHeading")}
          </h2>
          <Button
            type="button"
            variant="outline"
            onClick={handleUpdatePreview}
            disabled={previewStatus === "loading"}
          >
            {t("updatePreview")}
          </Button>
        </div>

        {isStale && previewStatus !== "loading" && (
          <p className="text-body-sm text-text-secondary" role="status">
            {t("previewStale")}
          </p>
        )}

        {previewStatus === "ready" && blobUrl ? (
          <>
            <iframe
              src={blobUrl}
              title={t("iframeTitle")}
              className="jp-pdf-frame"
            />
            {previewSourceUrl && (
              <p className="jp-pdf-frame__fallback">
                <a
                  href={previewSourceUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {t("openInNewTab")}
                </a>
              </p>
            )}
          </>
        ) : (
          <div className="flex min-h-[min(70vh,800px)] flex-col items-center justify-center gap-3 rounded-sm bg-surface-secondary p-6 text-center">
            {previewStatus === "loading" && (
              <>
                <BrandSpinner size={48} label={t("previewLoadingLabel")} />
                <p className="text-body-sm text-text-secondary" aria-hidden="true">
                  {t("previewLoading")}
                </p>
              </>
            )}
            {/* Live-regioner (WCAG 4.1.3): en tangentbords-/skärmläsaranvändare måste
                få veta att en "Uppdatera"-omtrigga misslyckades. Fel = assertiv
                (role=alert), rate-limit/saknad = artig (role=status). */}
            {previewStatus === "rateLimited" && (
              <p className="max-w-[68ch] text-body text-text-primary" role="status">
                {t("previewRateLimited", { seconds: retryAfterSeconds })}
              </p>
            )}
            {previewStatus === "notFound" && (
              <p className="max-w-[68ch] text-body text-text-primary" role="status">
                {t("previewNotFound")}
              </p>
            )}
            {previewStatus === "error" && (
              <p className="max-w-[68ch] text-body text-text-primary" role="alert">
                {t("previewError")}
              </p>
            )}
          </div>
        )}
      </div>
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
