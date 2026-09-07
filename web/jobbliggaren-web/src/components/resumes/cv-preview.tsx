"use client";

// "use client": klient-ö för visning och nedladdning av den uppladdade
// originalfilen (Fas 4 STEG B-2). Kräver browser-API:er (fetch av binär
// blob, URL.createObjectURL, AbortController), modal-state och tangentbords-/
// fokus-hantering — inget av detta kan göras i en Server Component.

import { useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { Eye, X } from "lucide-react";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { atsTextResponseSchema, type AtsTextResponse } from "@/lib/dto/resumes";

/**
 * CvPreview — trigger-knapp + klient-state-modal som ger ANVÄNDAREN HENNES EGEN
 * uppladdade fil (Klas-direktiv 2026-09-06). Hämtar filen från en binär BFF-route
 * (server-only egress, ägar-scopad via session→Bearer) och gör en object-URL som
 * en pdf VISAS på och som nedladdningen pekar på. Källan är generisk via
 * `originalUrl`: `/api/cv/parsed/{parsedId}/original` (importstaging) ELLER
 * `/api/cv/{id}/original` (befordrad, kanonisk Resume). Komponenten äger ingen
 * id-form.
 *
 * Fram till 2026-09-06 visade den i stället vår EGEN genererade PDF
 * (`/preview?profile=Ats|Visual`) — alltså inte filen användaren laddade upp. Med
 * originalet som källa har profilaxeln ingen mening: en uppladdad fil har ingen
 * ATS-variant och ingen visuell variant, den är precis en fil ("filen är helig",
 * ADR 0093 §D5). Profilflikarna är därför borta. `?profile=` lever vidare på
 * granskningssidan, där den styr SJÄLVA GRANSKNINGEN och inte den här vyn.
 *
 * **Pdf:en VISAS här, och det är en omprövad grind — inte en smaksak som gled
 * tillbaka.** DPIA #659:s R-F6/M-F2 föreskrev download-only. De är omprövade för
 * PDF-ARMEN och ingenting annat: ADR 0101 `Amendment 2026-09-06` + DPIA #659 §11,
 * beslutade av controllern och SIGNERADE av `security-auditor` 2026-09-06. Läs den
 * signaturen och dess sex lapse-triggers innan den här grenen rörs — den är en
 * daterad mätning, inte en egenskap som ärvs framåt.
 *
 * **Vilken mekanism som bär renderingen är ett val de två dokumenten kräver att en PR
 * gör, och det här är valet: `fetch → blob → <iframe src={blobUrl}>`.** Att i stället
 * rikta en iframe direkt mot BFF-routen kan inte fungera här — `frame-ancestors 'none'`
 * och `X-Frame-Options: DENY` serveras på `/(.*)` (next.config.ts), route handlers
 * inräknade, och de nekar även SAMMA origin. Att lätta på någondera är DPIA #659 §11:s
 * lapse-trigger 4.
 *
 * ⚠ Följden av det valet: BFF:ens `Content-Disposition` är INERT för den här vyn.
 * `fetch()` läser aldrig `Content-Disposition` (security-auditor, PR #1684), så att
 * flippa den headern varken tänder eller släcker renderingen. Den står kvar på
 * `attachment` av ett annat skäl — se `original-file-proxy.ts`.
 *
 * ⚠ **DOCX visas inte.** En iframe ger en tyst blank ruta för Word-filer, och att
 * rendera om dem vore "vår rendering av din fil" — precis det direktivet finns för att
 * få bort. Signaturen gäller uttryckligen pdf-armen och vidgas inte. Docx laddas ner.
 *
 * Filen kan dessutom SAKNAS: ett CV skapat i tjänsten har ingen uppladdad fil alls,
 * och inte heller importer som föregår filarkivet. På STAGING-ytan tillkommer en
 * import vars personnummer-scan föll och där användaren avböjde lagring — den orsaken
 * kan inte nå den kanoniska ytan, eftersom `ParsedResume.Promote` vägrar en flaggad
 * parse. 404 är alltså ett VANLIGT svar och renderas som tomt tillstånd.
 *
 * `Content-Type` (som BFF:en snävar mot en allowlist) avgör BÅDE vy-grenen (pdf
 * visas, docx laddas ner) och nedladdningens filändelse — aldrig filnamnets ändelse.
 *
 * Textversion för ATS (Fas 4b PR-8.3): när `atsTextUrl` ges läggs en andra flik
 * till som hämtar den linjäriserade, redan pnr-redigerade CV-texten (JSON) och
 * visar den i en `<pre>`. Den är kvar just för att den är KÄLLDISKRIMINERAD — den
 * säger uttryckligen "så här läser en maskin det vi genererar", vilket är något
 * annat än originalfilen och aldrig kan förväxlas med den (ADR 0093 §D8).
 * `atsTextUrl` utelämnas för parsade CV → ingen flikrad alls där.
 *
 * Modal-mekaniken (scrim / role=dialog / aria-modal / focus-trap / focus-return /
 * body-scroll-lock / Esc) speglar `JobAdModalShell`. Skillnad: detta är en
 * KLIENT-STATE-modal (ingen route/searchParam-navigering), så fokus-retur till
 * trigger-knappen görs explicit i `close()` (JobAdModalShell förlitar sig på
 * `router.back()`).
 *
 * Spinner-doktrin: filhämtningen är en känd-långsam, formlös väntan (den
 * dekrypterar ett Form C-kuvert server-side) → öppna modalen direkt +
 * BrandSpinner + "läses in"-text (samma mönster som ModalLoadingShell).
 * ATS-textens JSON-hämtning är snabb → en enkel status-rad (role=status), ingen
 * spinner. Object-URL:er revokeras vid stängning, unmount och vy-byte (ingen
 * blob-läcka).
 */

interface CvPreviewProps {
  /**
   * Binär BFF-route för originalfilen UTAN query, t.ex.
   * `/api/cv/parsed/{parsedId}/original` eller `/api/cv/{id}/original`.
   */
  originalUrl: string;
  /**
   * Same-origin BFF-route för den kanoniska ATS-textvyn (`/api/cv/{id}/ats-text`),
   * som returnerar `{ source, text }` JSON. Ges bara för befordrade Resume (den
   * har en kanonisk id); utelämnas för parsade CV → ingen ATS-textflik.
   */
  atsTextUrl?: string;
  /**
   * Klassnamn för trigger-knappen, så ytan kan matcha sina grann-knappar (t.ex.
   * `--sm` på ResumeCard intill Redigera-knappen). Default = full-storlek secondary.
   */
  triggerClassName?: string;
  /**
   * Ikonstorlek (px) i trigger-knappen, så den matchar grann-knappens ikon (t.ex.
   * 14 på ResumeCard:s `--sm`-rad intill Granska CV-knappens ikon). Default = 16
   * (full-storlek).
   */
  triggerIconSize?: number;
  /**
   * Tillgängligt namn på triggern, när ytan renderar flera. `/cv` ger ett kort per
   * CV, så utan detta blir N identiska "Öppna CV-filen" i en skärmläsares
   * knapp-rotor (#1373). Utelämnad => knappens egen text bär namnet.
   *
   * ⚠ Ändras trigger-copyn måste den här strängen följa med: WCAG 2.1 SC 2.5.3
   * (Label in Name) kräver att det tillgängliga namnet innehåller den synliga texten.
   */
  triggerAriaLabel?: string;
  /**
   * Ett läsbart namn på filen, som nedladdningen döps efter. Utan det heter varje
   * hämtad fil `original.pdf`, och två CV blir `original.pdf` + `original (1).pdf` i
   * nedladdningsmappen — produktens enda levererade artefakt, oidentifierbar efter
   * klicket. Ytan äger namnet: kortet har `resume.name`, stagingvyn
   * `parsed.sourceFileName`. Saneras här, aldrig av BFF:en (som medvetet skickar ett
   * syntetiskt namn i sin header — se `original-file-proxy.ts`).
   */
  fileName?: string;
}

/** Den aktiva fliken: originalfilen eller ATS-textvyn. */
type ViewTab = "original" | "atsText";

type OriginalStatus =
  | "loading"
  | "ready"
  | "error"
  | "rateLimited"
  | "noOriginal";

/** ATS-textflikens hämtnings-tillstånd. 429 viks in i `error` (civil "försök
 *  igen om en stund") — fliken har ingen egen rate-limit-copy. */
type AtsTextStatus = "loading" | "ready" | "notFound" | "error";

/** De två filformer `CvFileSignature` kan lösa. Formen är BÅDE vy-gren (bara pdf
 *  renderas) och nedladdningens filändelse. */
type OriginalKind = "pdf" | "docx";

/** Den hämtade filen: en object-URL plus vilken form den har. Formen avgör om
 *  URL:en målas i en iframe eller bara laddas ned. */
interface LoadedOriginal {
  url: string;
  kind: OriginalKind;
}

/** Content-Type → filform. Speglar BFF-routens allowlist; allt annat är ett fel. */
const KIND_BY_CONTENT_TYPE = new Map<string, OriginalKind>([
  ["application/pdf", "pdf"],
  [
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "docx",
  ],
]);

/** Default rate-limit-retry-fönster (sekunder) om 429-svarets body saknar ett
 *  parsbart värde — speglar backend-policyns fönster (paritet med
 *  `parseRetryAfter` i `_helpers`). */
const DEFAULT_RETRY_AFTER_SECONDS = 60;

/**
 * Nedladdningens filnamn. Ytans namn om det finns ett, annars `original.<kind>`.
 * Sökvägs- och kontrolltecken strippas: värdet blir ett filnamn på användarens disk,
 * så en separator där kunde peka utanför nedladdningsmappen. BÅDA separatorerna räknas
 * — `\\` är Windows egen, och den överlevde i en tidigare version av den här klassen
 * medan kommentaren påstod motsatsen. Ändelsen kommer alltid från det SERVERHÄRLEDDA
 * `kind`, aldrig från namnet.
 */
function downloadFileName(fileName: string | undefined, kind: OriginalKind): string {
  const cleaned = (fileName ?? "")
    .replace(/[\\\/:*?"<>|\x00-\x1f]/g, "")
    .trim();
  if (cleaned === "") return `original.${kind}`;
  return cleaned.toLowerCase().endsWith(`.${kind}`) ? cleaned : `${cleaned}.${kind}`;
}

export function CvPreview({
  originalUrl,
  atsTextUrl,
  triggerClassName = "jp-btn jp-btn--secondary",
  triggerIconSize = 16,
  triggerAriaLabel,
  fileName,
}: CvPreviewProps) {
  const t = useTranslations("resumes.preview");
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<ViewTab>("original");
  const [status, setStatus] = useState<OriginalStatus>("loading");
  const [original, setOriginal] = useState<LoadedOriginal | null>(null);
  const [retryAfterSeconds, setRetryAfterSeconds] = useState(
    DEFAULT_RETRY_AFTER_SECONDS
  );
  const [atsStatus, setAtsStatus] = useState<AtsTextStatus>("loading");
  const [atsText, setAtsText] = useState<AtsTextResponse | null>(null);
  // Bumpas av "Försök igen" och sitter i hämtningens deps — felgrenen får en
  // åtgärd i stället för bara en uppmaning att vänta.
  const [reloadToken, setReloadToken] = useState(0);

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
    setOriginal((prev) => {
      if (prev) URL.revokeObjectURL(prev.url);
      return null;
    });
    setStatus("loading");
    setView("original");
    triggerRef.current?.focus();
  };

  // Hämta originalfilen vid öppning. ATS-textfliken kör INTE denna hämtning (den
  // har sin egen effekt nedan). AbortController städar in-flight-fetchen vid
  // stängning/unmount/vy-byte. Object-URL:er revokeras när de ersätts eller vid
  // avmontering (ingen blob-läcka).
  useEffect(() => {
    if (!open) return;
    if (view === "atsText") return;

    const controller = new AbortController();
    let createdUrl: string | null = null;

    const run = async () => {
      // Återställ till loading + rensa ev. inaktuell blob vid starten av varje
      // hämtning. Görs inuti den async-funktionen (inte synkront i effekt-kroppen)
      // — undviker kaskad-renders.
      setStatus("loading");
      setOriginal((prev) => {
        if (prev) URL.revokeObjectURL(prev.url);
        return null;
      });
      try {
        const res = await fetch(originalUrl, {
          signal: controller.signal,
          cache: "no-store",
        });

        if (res.ok) {
          // Formen avgörs på det SERVERHÄRLEDDA svaret, aldrig på filnamnet.
          const contentType =
            res.headers.get("Content-Type")?.split(";")[0]?.trim() ?? "";
          const kind = KIND_BY_CONTENT_TYPE.get(contentType);
          if (kind === undefined) {
            setStatus("error");
            return;
          }

          const blob = await res.blob();
          createdUrl = URL.createObjectURL(blob);
          setOriginal({ url: createdUrl, kind });
          setStatus("ready");
          return;
        }

        if (res.status === 429) {
          const seconds = await readRetryAfterSeconds(res);
          setRetryAfterSeconds(seconds);
          setStatus("rateLimited");
          return;
        }

        // 404 är inte ett fel här: CV skapade i tjänsten, och importer där filen
        // aldrig sparades, har helt enkelt ingen originalfil.
        if (res.status === 404) {
          setStatus("noOriginal");
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
  }, [open, view, originalUrl, reloadToken]);

  // Hämta ATS-textvyn (JSON) vid aktivering av textfliken. Egen effekt så fil-
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
        aria-label={triggerAriaLabel}
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
              {/* Flikraden finns bara när det GÅR att välja: utan ATS-textvyn är
                  originalfilen den enda vyn, och en ensam flik är en kontroll som
                  inte kontrollerar något. */}
              {atsTextUrl && (
                <div
                  role="group"
                  aria-label={t("viewGroupLabel")}
                  className="jp-segment"
                >
                  <button
                    type="button"
                    className="jp-segment__opt"
                    data-active={!isAtsText}
                    aria-current={!isAtsText ? "true" : undefined}
                    onClick={() => setView("original")}
                  >
                    <span>{t("original")}</span>
                  </button>
                  <button
                    type="button"
                    className="jp-segment__opt"
                    data-active={isAtsText}
                    aria-current={isAtsText ? "true" : undefined}
                    onClick={() => setView("atsText")}
                  >
                    <span>{t("atsText")}</span>
                  </button>
                </div>
              )}

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

                  {/* PERMANENT live-region (WCAG 2.1 SC 4.1.3). Utfallet av hämtningen
                      annonseras bara om regionen fanns FÖRE texten: en artig region som
                      skapas i samma render som sitt innehåll missas av flera
                      skärmläsare. Spinnerns egen `role="status"` sitter inuti
                      loading-blocket och avmonteras i exakt den render där utfallet dyker
                      upp, så den kan inte bära det här. Formen speglar den levererade i
                      `template-builder.tsx` — behållaren alltid monterad, innehållet
                      byts. Fokus ligger kvar på Stäng-knappen hela tiden. */}
                  <div
                    role="status"
                    aria-live="polite"
                    aria-label={t("statusRegionLabel")}
                  >
                    {status === "ready" && original && (
                      <p className="jp-lede">
                        {original.kind === "pdf"
                          ? t("readyBodyPdf")
                          : t("readyBodyDocx")}
                      </p>
                    )}
                    {status === "noOriginal" && (
                      <p className="jp-lede">{t("noOriginal")}</p>
                    )}
                    {status === "rateLimited" && (
                      <p className="jp-lede">
                        {t("rateLimited", { seconds: retryAfterSeconds })}
                      </p>
                    )}
                  </div>

                  {/* Pdf:en målas; docx får ingen ram alls. En iframe mot en Word-fil
                      ger en tyst blank ruta, och att rendera om den vore "vår rendering
                      av din fil". Grenen är signerad för pdf-armen och ingenting annat
                      (ADR 0101 `Amendment 2026-09-06`, DPIA #659 §11).

                      Ramen står UTANFÖR live-regionen: den är ett dokument, inte ett
                      utfallsmeddelande, och en region som byter både text och inbäddat
                      innehåll annonseras ojämnt. Utfallet annonseras av `readyBodyPdf`
                      ovan, som ligger kvar i regionen. */}
                  {status === "ready" && original?.kind === "pdf" && (
                    <iframe
                      src={original.url}
                      title={t("frameTitle")}
                      className="jp-pdf-frame"
                    />
                  )}

                  {/* Kontrollerna står UTANFÖR live-regionen: en region som byter både
                      text och interaktiva element annonseras ojämnt, och knappen är
                      redan nåbar via fokusordningen. Nedladdningen finns för BÅDA
                      formerna — den är docx enda väg, och pdf:ens väg att spara. */}
                  {status === "ready" && original && (
                    <p>
                      <a
                        className="jp-btn jp-btn--secondary"
                        href={original.url}
                        download={downloadFileName(fileName, original.kind)}
                      >
                        {t("download")}
                      </a>
                    </p>
                  )}

                  {/* Tomt tillstånd får en KONTROLL, inte en instruktion: utan den måste
                      användaren stänga modalen och själv hitta importvägen. */}
                  {status === "noOriginal" && (
                    <p>
                      <Link
                        href="/cv/importera"
                        className="jp-btn jp-btn--secondary"
                        onClick={close}
                      >
                        {t("importCta")}
                      </Link>
                    </p>
                  )}

                  {/* Felet är `role="alert"` och inte en artig region — det avbryter, och
                      det är repots doktrin (`cv-finding-status-control.tsx`). */}
                  {status === "error" && (
                    <>
                      <p className="jp-lede" role="alert">
                        {t("error")}
                      </p>
                      <p>
                        <button
                          type="button"
                          className="jp-btn jp-btn--secondary"
                          onClick={() => {
                            setReloadToken((n) => n + 1);
                            // Denna knapp avmonterar SIG SJÄLV: omhämtningen sätter status
                            // till loading, och utan detta hamnar fokus på <body>. Fällan
                            // nedan jämför activeElement mot first/last, matchar ingendera,
                            // och nästa Tab lämnar en dialog som är aria-modal="true" och
                            // därmed dold för skärmläsaren. Stäng-knappen är dialogens
                            // stabila element och är där fokus ligger vid öppning.
                            closeRef.current?.focus();
                          }}
                        >
                          {t("retry")}
                        </button>
                      </p>
                    </>
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
