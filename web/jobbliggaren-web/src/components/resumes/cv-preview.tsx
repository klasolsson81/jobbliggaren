"use client";

// "use client": klient-ö för PDF-förhandsgranskning (Fas 4 STEG B-2,
// "Förhandsgranska CV"). Kräver browser-API:er (fetch av binär blob,
// URL.createObjectURL, AbortController), modal-state och tangentbords-/
// fokus-hantering — inget av detta kan göras i en Server Component.

import { useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Eye, X } from "lucide-react";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import type { RenderProfile } from "@/lib/dto/parsed-resume";
import { atsTextResponseSchema, type AtsTextResponse } from "@/lib/dto/resumes";

/**
 * CvPreview — trigger-knapp + klient-state-modal med en PDF-iframe (deterministisk
 * förhandsgranskning, INGEN AI — ADR 0071/0074). Hämtar PDF:en från en binär
 * BFF-route (server-only egress, ägar-scopad via session→Bearer), gör en
 * object-URL och visar den i en iframe. Källan är generisk via `previewUrl`:
 * `/api/cv/parsed/{parsedId}/preview` (parsad staging-artefakt) ELLER
 * `/api/cv/{id}/preview` (befordrad, kanonisk Resume — TD-112 / #202). Komponenten
 * äger ingen id-form; den lägger bara på `?profile=` på den givna routen.
 *
 * Textversion för ATS (Fas 4b PR-8.3): när `atsTextUrl` ges läggs en tredje flik
 * till som hämtar den linjäriserade, redan pnr-redigerade CV-texten (JSON) och
 * visar den i en `<pre>`. Den aktiva fliken modelleras som `RenderProfile |
 * "atsText"` — vi vidgar ALDRIG `RenderProfile` (backend-validatorn känner bara
 * `Ats`/`Visual`). ATS-textfliken kör inte PDF-blob-hämtningen; ett byte tillbaka
 * till en PDF-profil återställer iframe-beteendet. `atsTextUrl` utelämnas för
 * parsade CV (ingen kanonisk id / ingen ats-text-endpoint) → fliken visas ej där.
 *
 * Modal-mekaniken (scrim / role=dialog / aria-modal / focus-trap / focus-return /
 * body-scroll-lock / Esc) speglar `JobAdModalShell`. Skillnad: detta är en
 * KLIENT-STATE-modal (ingen route/searchParam-navigering), så fokus-retur till
 * trigger-knappen görs explicit i `close()` (JobAdModalShell förlitar sig på
 * `router.back()`). Profil-växeln speglar `.jp-segment`-utseendet men använder
 * `<button>` som byter vy-state utan att navigera.
 *
 * Spinner-doktrin: PDF-renderingen är en känd-långsam, formlös väntan → öppna
 * modalen direkt + BrandSpinner + "läses in"-text (samma mönster som
 * ModalLoadingShell). ATS-textens JSON-hämtning är snabb → en enkel status-rad
 * (role=status), ingen spinner. Object-URL:er revokeras vid stängning, unmount
 * och profil-byte (ingen blob-läcka).
 */

interface CvPreviewProps {
  /**
   * Binär BFF-preview-route UTAN query, t.ex. `/api/cv/parsed/{parsedId}/preview`
   * eller `/api/cv/{id}/preview`. `?profile=` läggs på av komponenten.
   */
  previewUrl: string;
  /**
   * Same-origin BFF-route för den kanoniska ATS-textvyn (`/api/cv/{id}/ats-text`),
   * som returnerar `{ source, text }` JSON. Ges bara för befordrade Resume (den
   * har en kanonisk id); utelämnas för parsade CV → ingen ATS-textflik.
   */
  atsTextUrl?: string;
  /** Initial profil (sidans `?profile=`-default — "Ats"). */
  initialProfile: RenderProfile;
  /**
   * Klassnamn för trigger-knappen, så ytan kan matcha sina grann-knappar (t.ex.
   * `--sm` på ResumeCard intill Redigera-knappen). Default = full-storlek secondary.
   */
  triggerClassName?: string;
  /**
   * Ikonstorlek (px) i trigger-knappen, så den matchar grann-knappens ikon (t.ex.
   * 14 på ResumeCard:s `--sm`-rad intill Redigera-ikonen). Default = 16 (full-storlek).
   */
  triggerIconSize?: number;
}

/** Den aktiva fliken: en PDF-profil eller ATS-textvyn. `RenderProfile` vidgas
 * ALDRIG (backend känner bara `Ats`/`Visual`) — `"atsText"` är ren klient-vy-state. */
type ViewTab = RenderProfile | "atsText";

type PreviewStatus =
  | "loading"
  | "ready"
  | "error"
  | "rateLimited"
  | "notFound";

/** ATS-textflikens hämtnings-tillstånd. 429 viks in i `error` (civil "försök
 * igen om en stund") — fliken har ingen egen rate-limit-copy. */
type AtsTextStatus = "loading" | "ready" | "notFound" | "error";

const PROFILE_OPTIONS: ReadonlyArray<{
  value: RenderProfile;
  labelKey: "ats" | "visual";
}> = [
  { value: "Ats", labelKey: "ats" },
  { value: "Visual", labelKey: "visual" },
];

/** Default rate-limit-retry-fönster (sekunder) om 429-svarets body saknar ett
 *  parsbart värde — speglar backend-policyns fönster (paritet med
 *  `parseRetryAfter` i `_helpers`). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

function iframeTitle(
  t: ReturnType<typeof useTranslations<"resumes.preview">>,
  profile: RenderProfile,
): string {
  return profile === "Ats"
    ? t("iframeTitleAts")
    : t("iframeTitleVisual");
}

export function CvPreview({
  previewUrl,
  atsTextUrl,
  initialProfile,
  triggerClassName = "jp-btn jp-btn--secondary",
  triggerIconSize = 16,
}: CvPreviewProps) {
  const t = useTranslations("resumes.preview");
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<ViewTab>(initialProfile);
  const [status, setStatus] = useState<PreviewStatus>("loading");
  const [blobUrl, setBlobUrl] = useState<string | null>(null);
  const [retryAfterSeconds, setRetryAfterSeconds] = useState(
    DEFAULT_RETRY_AFTER_SECONDS
  );
  const [atsStatus, setAtsStatus] = useState<AtsTextStatus>("loading");
  const [atsText, setAtsText] = useState<AtsTextResponse | null>(null);

  const isAtsText = view === "atsText";

  const triggerRef = useRef<HTMLButtonElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const labelId = useId();

  // Stäng: revoka blob, nollställ state och RETURNERA FOKUS till triggern
  // (explicit — klient-state-modal, ingen router.back()).
  const close = () => {
    setOpen(false);
    // Belt-and-braces: blob:en revokas även av fetch-effektens cleanup när
    // `open` faller — den extra revoken här är en spec-no-op (medvetet, så
    // ingen läcka kvarstår oavsett vilken stäng-väg som triggas).
    setBlobUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setStatus("loading");
    triggerRef.current?.focus();
  };

  // Hämta PDF:en vid öppning och vid profil-byte. ATS-textfliken kör INTE denna
  // hämtning (den har sin egen effekt nedan). AbortController städar in-flight-
  // fetchen vid stängning/unmount/vy-byte. Object-URL:er revokeras när de ersätts
  // eller vid avmontering (ingen blob-läcka).
  useEffect(() => {
    if (!open) return;
    if (view === "atsText") return;
    const activeProfile = view;

    const controller = new AbortController();
    let createdUrl: string | null = null;

    const run = async () => {
      // Återställ till loading + rensa ev. inaktuell blob vid starten av varje
      // hämtning (öppning eller profil-byte). Görs inuti den async-funktionen
      // (inte synkront i effekt-kroppen) — undviker kaskad-renders.
      setStatus("loading");
      setBlobUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return null;
      });
      try {
        const res = await fetch(
          `${previewUrl}?profile=${activeProfile}`,
          { signal: controller.signal, cache: "no-store" }
        );

        if (res.ok) {
          const blob = await res.blob();
          createdUrl = URL.createObjectURL(blob);
          setBlobUrl(createdUrl);
          setStatus("ready");
          return;
        }

        if (res.status === 429) {
          const seconds = await readRetryAfterSeconds(res);
          setRetryAfterSeconds(seconds);
          setStatus("rateLimited");
          return;
        }

        if (res.status === 404) {
          setStatus("notFound");
          return;
        }

        setStatus("error");
      } catch (err) {
        // Abort vid stängning/byte är inte ett fel — lämna state orört.
        if (err instanceof DOMException && err.name === "AbortError") return;
        setStatus("error");
      }
    };

    void run();

    return () => {
      controller.abort();
      if (createdUrl) URL.revokeObjectURL(createdUrl);
    };
  }, [open, view, previewUrl]);

  // Hämta ATS-textvyn (JSON) vid aktivering av textfliken. Egen effekt så PDF-
  // och text-hämtningarna aldrig korsar tillstånd. AbortController städar in-
  // flight-fetchen vid stängning/unmount/vy-byte. Backend-endpointen kan saknas
  // lokalt (PR-8.2-syskon) → 404 mappas civilt till notFound.
  useEffect(() => {
    if (!open) return;
    if (view !== "atsText") return;
    if (!atsTextUrl) return;

    const controller = new AbortController();

    const run = async () => {
      setAtsStatus("loading");
      setAtsText(null);
      try {
        const res = await fetch(atsTextUrl, {
          signal: controller.signal,
          cache: "no-store",
        });

        if (res.ok) {
          const json: unknown = await res.json();
          const parsed = atsTextResponseSchema.safeParse(json);
          if (parsed.success) {
            setAtsText(parsed.data);
            setAtsStatus("ready");
          } else {
            setAtsStatus("error");
          }
          return;
        }

        if (res.status === 404) {
          setAtsStatus("notFound");
          return;
        }

        setAtsStatus("error");
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setAtsStatus("error");
      }
    };

    void run();

    return () => controller.abort();
  }, [open, view, atsTextUrl]);

  // Fokus in i modalen vid öppning (close-knappen, som JobAdModalShell) +
  // body-scroll-lock under modalens livstid.
  useEffect(() => {
    if (!open) return;
    closeRef.current?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
    };
  }, [open]);

  // Esc stänger; focus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3) —
  // idiom speglat från JobAdModalShell.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        close();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      // Full focusable set — must stay identical across every focus-trap shell
      // (input/select/textarea included so a trap never leaks to the browser
      // chrome when the panel gains a form control). SPOT-centralisation: #575.
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
      );
      if (focusable.length === 0) return;
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
    // close läser bara refs/setters (stabila) — medvetet utelämnad ur deps.
  }, [open]);

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={triggerClassName}
        onClick={() => setOpen(true)}
      >
        <Eye size={triggerIconSize} aria-hidden="true" />
        <span>{t("trigger")}</span>
      </button>

      {open && (
        <div className="jp-modal-scrim" role="presentation" onClick={close}>
          <div
            ref={panelRef}
            className="jp-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby={labelId}
            onClick={(e) => e.stopPropagation()}
          >
            <header className="jp-modal__head">
              <h2 id={labelId} className="jp-modal__title">
                {t("title")}
              </h2>
              <button
                ref={closeRef}
                type="button"
                className="jp-icon-btn"
                aria-label={t("close")}
                onClick={close}
              >
                <X size={20} aria-hidden="true" />
              </button>
            </header>

            <div className="jp-modal__body">
              <div
                role="group"
                aria-label={t("profileGroupLabel")}
                className="jp-segment"
              >
                {PROFILE_OPTIONS.map((option) => {
                  const isActive = option.value === view;
                  return (
                    <button
                      key={option.value}
                      type="button"
                      className="jp-segment__opt"
                      data-active={isActive}
                      aria-current={isActive ? "true" : undefined}
                      onClick={() => setView(option.value)}
                    >
                      <span>{t(option.labelKey)}</span>
                    </button>
                  );
                })}
                {atsTextUrl && (
                  <button
                    type="button"
                    className="jp-segment__opt"
                    data-active={isAtsText}
                    aria-current={isAtsText ? "true" : undefined}
                    onClick={() => setView("atsText")}
                  >
                    <span>{t("atsText")}</span>
                  </button>
                )}
              </div>

              {!isAtsText && (
                <>
                  {status === "loading" && (
                    <div className="jp-modal-loading">
                      <BrandSpinner size={48} label={t("loadingLabel")} />
                      <p className="jp-modal-loading__text" aria-hidden="true">
                        {t("loadingText")}
                      </p>
                    </div>
                  )}

                  {status === "ready" && blobUrl && (
                    <>
                      <iframe
                        src={blobUrl}
                        title={iframeTitle(t, view)}
                        className="jp-pdf-frame"
                      />
                      <p className="jp-pdf-frame__fallback">
                        <a
                          href={`${previewUrl}?profile=${view}`}
                          target="_blank"
                          rel="noopener noreferrer"
                        >
                          {t("openInNewTab")}
                        </a>
                      </p>
                    </>
                  )}

                  {status === "rateLimited" && (
                    <p className="jp-lede">
                      {t("rateLimited", { seconds: retryAfterSeconds })}
                    </p>
                  )}

                  {status === "notFound" && (
                    <p className="jp-lede">{t("notFound")}</p>
                  )}

                  {status === "error" && (
                    <p className="jp-lede">{t("error")}</p>
                  )}
                </>
              )}

              {isAtsText && (
                <>
                  {atsStatus === "loading" && (
                    <p className="jp-lede" role="status">
                      {t("atsTextLoading")}
                    </p>
                  )}

                  {atsStatus === "ready" && atsText && (
                    <section
                      aria-label={t("atsTextRegionLabel")}
                      className="jp-atstext"
                    >
                      <p className="jp-atstext__banner">{t("atsTextBanner")}</p>
                      <pre className="jp-atstext__body">{atsText.text}</pre>
                    </section>
                  )}

                  {atsStatus === "notFound" && (
                    <p className="jp-lede">{t("atsTextNotFound")}</p>
                  )}

                  {atsStatus === "error" && (
                    <p className="jp-lede">{t("atsTextError")}</p>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
      )}
    </>
  );
}

/**
 * Läser `retryAfterSeconds` ur 429-svarets JSON-body (BFF:n speglar
 * import-routens form). Faller till 60s om body saknas/inte är parsbar — samma
 * default-fönster som backendens rate-limit-policy.
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
