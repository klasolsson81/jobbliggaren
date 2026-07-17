// "use client": filväljaren behöver lokal state (vald fil, namn, klient-validering,
// samtyckesdialogens state), en submit-handler som bygger FormData + fetch:ar BFF:en,
// och programmatisk navigation efter utfallet. Inget CV-PII passerar klienten — bara
// File-objektet (bytesen), det valda namnet (kontonamnet, användarens eget) och det
// returnerade, PII-fria utfallet (ids + count/kinds-fyndet, aldrig personnummer-värdet).
"use client";

import { useId, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { FileText, Upload } from "lucide-react";
import { BrandSpinner } from "@/components/brand/brand-spinner";
import { PersonnummerConsentDialog } from "@/components/resumes/personnummer-consent-dialog";

/** Paritet med BFF:ens + backendens golv (10 MiB). */
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

const ACCEPTED_EXTENSIONS = [".pdf", ".docx"] as const;
const ACCEPTED_MIME_TYPES = [
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
] as const;

/** `accept`-attributet: tillägg + MIME (filväljaren förfiltrerar; vi
 * validerar ändå själva eftersom `accept` inte är en garanti). */
const ACCEPT_ATTR = [...ACCEPTED_EXTENSIONS, ...ACCEPTED_MIME_TYPES].join(",");

/** Fel-tillstånd som en stabil nyckel — den svenska texten resolvas via next-intl
 * i komponenten (`resumes.upload.*`). Validerings-hjälparna nedan är rena (ingen
 * `t`-bindning), så de returnerar nyckeln, inte den färdiga strängen. */
type UploadErrorKey =
  | "errorWrongType"
  | "errorTooLarge"
  | "errorNoFile"
  | "errorGeneric"
  | "errorUnauthorized";

/**
 * Det sammansatta, PII-fria utfallet FE:t rutar på (speglar BFF:ens ImportOutcomeResponse —
 * CV-pivot 5c). `promoted` = CV:t skapades direkt (auto-promote). `pending` = parsen stannade
 * för granskning; `blockReason` bär VARFÖR (copy/telemetri, aldrig ruttning), och för
 * `PersonnummerPresent` bär `personnummerCount` samtyckesdialogens antal (aldrig ett råvärde).
 */
export type UploadOutcome =
  | { readonly kind: "promoted"; readonly resumeId: string; readonly parsedResumeId: string }
  | {
      readonly kind: "pending";
      readonly parsedResumeId: string;
      readonly blockReason: string;
      readonly personnummerCount: number;
    };

/** Smal läsning av BFF-svaret (`/api/cv/import`) via type-guards — vi litar
 * aldrig på okänd shape, men drar inte in zod i klientbunten. */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

/** Läser det sammansatta utfallet ur BFF-svaret. Returnerar null vid oväntad shape
 * (→ generisk fel-copy). Alla lästa fält är PII-fria. */
function readOutcome(body: unknown): UploadOutcome | null {
  if (!isRecord(body)) return null;
  const parsedResumeId =
    typeof body.parsedResumeId === "string" ? body.parsedResumeId : null;
  if (!parsedResumeId) return null;

  if (body.outcome === "Promoted" && typeof body.resumeId === "string") {
    return { kind: "promoted", resumeId: body.resumeId, parsedResumeId };
  }
  if (body.outcome === "LeftPending") {
    const pnr = isRecord(body.personnummer) ? body.personnummer : {};
    return {
      kind: "pending",
      parsedResumeId,
      blockReason: typeof body.blockReason === "string" ? body.blockReason : "",
      personnummerCount: typeof pnr.count === "number" ? pnr.count : 0,
    };
  }
  return null;
}

function readRetryAfter(body: unknown): number | null {
  if (
    isRecord(body) &&
    body.error === "rateLimited" &&
    typeof body.retryAfterSeconds === "number"
  ) {
    return body.retryAfterSeconds;
  }
  return null;
}

function readErrorMessage(body: unknown): string | null {
  if (
    isRecord(body) &&
    typeof body.error === "string" &&
    body.error !== "rateLimited" &&
    body.error !== "unauthorized" &&
    body.error !== "error"
  ) {
    return body.error;
  }
  return null;
}

function hasAcceptedExtension(fileName: string): boolean {
  const lower = fileName.toLowerCase();
  return ACCEPTED_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

function isAcceptedFile(file: File): boolean {
  // MIME kan vara tom eller fel beroende på OS/webbläsare → tillägg räcker
  // som komplement. Backend är den auktoritativa magic-byte-grinden.
  const mimeOk =
    file.type === "" ||
    (ACCEPTED_MIME_TYPES as readonly string[]).includes(file.type);
  return hasAcceptedExtension(file.name) && mimeOk;
}

/** Klient-validering före upload. Returnerar en fel-nyckel eller null (texten
 * resolvas i komponenten via next-intl). */
function validateFile(file: File): UploadErrorKey | null {
  if (!isAcceptedFile(file)) return "errorWrongType";
  if (file.size > MAX_UPLOAD_BYTES) return "errorTooLarge";
  return null;
}

interface CvUploadFormProps {
  /**
   * ADR 0077 STEG 5 — om satt anropas denna med det sammansatta {@link UploadOutcome}:t
   * (och det valda filnamnet) i STÄLLET för programmatisk navigation. Låter en host-modal
   * stå kvar och styra nästa steg (welcome-modalen, match-setup-railen). Utelämnat =
   * default-navigation: `promoted` → `/cv/{resumeId}/granska`, `pending` →
   * `/cv/granska/{parsedResumeId}` (5a CTO-bind §3). Samtyckesvalet (personnummer) resolvas
   * ALLTID i formuläret först — värden får det slutliga utfallet efter användarens val.
   */
  readonly onUploaded?: (outcome: UploadOutcome, fileName?: string) => void;
  /**
   * Epik #526 polish (Klas rendered-verify 2026-07-03): starta uppladdningen
   * direkt när en giltig fil valts — inget extra "Ladda upp"-klick, och
   * submit-raden döljs. I detta läge visas inget namnfält (match-setup-railen
   * behöver inget CV-namn — backend faller tillbaka på kontonamnet). Default false.
   */
  readonly autoUpload?: boolean;
  /**
   * Epik #526 polish: dölj formulärets egen hjälptext när VÄRDEN redan bär
   * instruktionen. Hjälptexten förblir describedby via sr-only. Default true.
   */
  readonly showHelp?: boolean;
  /**
   * CV-pivot 5c: förifyll namnfältet med kontonamnet (`JobSeeker.DisplayName`). Fältet
   * är redigerbart; skickas som `name`-formfältet och blir CV:ts namn. Tomt → backend
   * faller tillbaka på kontonamnet (5a CTO-bind R5). Endast synligt i tvåstegsläget.
   */
  readonly defaultName?: string;
}

export function CvUploadForm({
  onUploaded,
  autoUpload = false,
  showHelp = true,
  defaultName = "",
}: CvUploadFormProps = {}) {
  const t = useTranslations("resumes.upload");
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [name, setName] = useState(defaultName);
  const [error, setError] = useState<string | null>(null);

  // Samtyckesvalet (personnummer i filen): den fil + parse + antal som väntar på
  // användarens beslut om att LAGRA originalfilen (ADR 0114). `null` = ingen väntande fråga.
  const [consent, setConsent] = useState<{
    file: File;
    parsedResumeId: string;
    count: number;
  } | null>(null);
  const [consentSaving, setConsentSaving] = useState(false);

  const inputId = useId();
  const nameId = useId();
  const nameHintId = useId();
  const helpId = useId();
  const errorId = useId();

  const describedBy = [helpId, error ? errorId : null]
    .filter(Boolean)
    .join(" ");

  // Slutligt utfall: värden styr (callback) eller vi navigerar (default-flödet).
  function resolveOutcome(outcome: UploadOutcome, fileName?: string) {
    if (onUploaded) {
      onUploaded(outcome, fileName);
      return;
    }
    router.push(
      outcome.kind === "promoted"
        ? `/cv/${outcome.resumeId}/granska`
        : `/cv/granska/${outcome.parsedResumeId}`,
    );
  }

  async function postImport(
    file: File,
    acknowledged: boolean,
  ): Promise<{ status: number; body: unknown } | null> {
    const formData = new FormData();
    formData.append("file", file);
    const trimmedName = name.trim();
    if (trimmedName) formData.append("name", trimmedName);
    // Fail-closed: vi skickar flaggan ENDAST vid ett uttryckligt samtycke (aldrig som
    // default på varje uppladdning — ADR 0114 §D3).
    if (acknowledged) formData.append("personnummerAcknowledged", "true");

    let response: Response;
    try {
      response = await fetch("/api/cv/import", { method: "POST", body: formData });
    } catch {
      setError(t("errorGeneric"));
      return null;
    }

    let body: unknown = null;
    try {
      body = await response.json();
    } catch {
      body = null;
    }
    return { status: response.status, body };
  }

  /** Statusbaserad felhantering (401/429/övrigt). Returnerar true om ett fel hanterades. */
  function handleErrorStatus(status: number, body: unknown): boolean {
    if (status === 401) {
      setError(t("errorUnauthorized"));
      return true;
    }
    if (status === 429) {
      const retryAfter = readRetryAfter(body);
      setError(
        retryAfter !== null
          ? t("errorRateLimited", { seconds: retryAfter })
          : t("errorGeneric"),
      );
      return true;
    }
    if (status !== 200 && status !== 201) {
      setError(readErrorMessage(body) ?? t("errorGeneric"));
      return true;
    }
    return false;
  }

  async function uploadFile(file: File): Promise<void> {
    const result = await postImport(file, false);
    if (!result) return;
    if (handleErrorStatus(result.status, result.body)) return;

    const outcome = readOutcome(result.body);
    if (!outcome) {
      setError(t("errorGeneric"));
      return;
    }

    // Personnummer i filen + inget samtycke ännu → res samtyckesdialogen (ADR 0114): ska
    // originalfilen LAGRAS? Ruttningen väntar tills användaren valt. Andra pending-skäl
    // (preambel, ej pålitlig parse, ofullständigt) rutar direkt till granskningen.
    if (outcome.kind === "pending" && outcome.blockReason === "PersonnummerPresent") {
      setConsent({
        file,
        parsedResumeId: outcome.parsedResumeId,
        count: outcome.personnummerCount,
      });
      return;
    }

    resolveOutcome(outcome, file.name);
  }

  // Samtycke JA: lagra originalfilen (re-POST med flaggan, ADR 0114 §D3 — samma bytes,
  // serverns egen scan binder samtycket till fyndet), rutta sedan till granskningen med
  // den NYA parsen (re-POST:en skapar en ny parse; den första blir en föräldralös utkast
  // som TD-111-svepet städar — accepterad ADR 0114-vårta).
  function onConsentConfirm() {
    if (!consent) return;
    const file = consent.file;
    setConsentSaving(true);
    startTransition(async () => {
      const result = await postImport(file, true);
      setConsentSaving(false);
      setConsent(null);
      if (!result) return;
      if (handleErrorStatus(result.status, result.body)) return;
      const outcome = readOutcome(result.body);
      if (!outcome) {
        setError(t("errorGeneric"));
        return;
      }
      resolveOutcome(outcome, file.name);
    });
  }

  // Samtycke NEJ: lagra INTE originalfilen. Gå ändå vidare till granskningen med den
  // (redan skapade) pending-parsen så att användaren kan ta bort personnumret där.
  function onConsentDecline() {
    if (!consent) return;
    const { parsedResumeId, count } = consent;
    setConsent(null);
    resolveOutcome(
      { kind: "pending", parsedResumeId, blockReason: "PersonnummerPresent", personnummerCount: count },
      undefined,
    );
  }

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;
    if (!file) {
      setSelectedFile(null);
      setError(null);
      return;
    }
    const validationError = validateFile(file);
    setError(validationError ? t(validationError) : null);
    setSelectedFile(validationError ? null : file);

    // Nollställ input-värdet så att SAMMA fil kan väljas om (change fyrar inte
    // på oförändrat värde) — utan detta blir en same-file-retry efter t.ex. 429
    // tyst död i auto-läget. UI:t läser `selectedFile`-staten, aldrig input-värdet.
    event.target.value = "";

    // Epik #526 polish: auto-upload — giltig fil valdes → ladda upp direkt.
    if (autoUpload && !validationError && !isPending) {
      startTransition(async () => {
        await uploadFile(file);
      });
    }
  }

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isPending) return;

    if (!selectedFile) {
      setError(t("errorNoFile"));
      return;
    }
    const validationError = validateFile(selectedFile);
    if (validationError) {
      setError(t(validationError));
      return;
    }

    setError(null);
    startTransition(async () => {
      await uploadFile(selectedFile);
    });
  }

  // Spinnern tar huvudytan under laddning MEN inte medan samtyckesdialogen väntar på
  // ett beslut (då bär dialogen statusen och formuläret ska stå kvar bakom scrimen).
  const showSpinner = isPending && consent === null;

  return (
    <>
      <form onSubmit={handleSubmit} className="jp-cvupload" noValidate>
        {showSpinner ? (
          <div className="jp-cvupload__pending" role="status" aria-live="polite">
            <BrandSpinner size={48} label={t("pendingLabel")} />
            <p className="jp-cvupload__pending-text">{t("pendingText")}</p>
          </div>
        ) : (
          <>
            {/* CV-namn (CV-pivot 5c) — endast i tvåstegsläget. Rent fält (ingen
                placeholder), förifyllt med kontonamnet, hint under fältet. */}
            {!autoUpload && (
              <div className="jp-cvupload__field">
                <label htmlFor={nameId} className="jp-label">
                  {t("nameLabel")}
                </label>
                <input
                  id={nameId}
                  name="name"
                  type="text"
                  className="jp-input"
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  aria-describedby={nameHintId}
                  autoComplete="name"
                  maxLength={200}
                />
                <p id={nameHintId} className="jp-cvupload__help">
                  {t("nameHint")}
                </p>
              </div>
            )}

            <div className="jp-cvupload__field">
              <label htmlFor={inputId} className="jp-cvupload__drop">
                <span className="jp-cvupload__drop-icon" aria-hidden="true">
                  {selectedFile ? <FileText size={22} /> : <Upload size={22} />}
                </span>
                <span className="jp-cvupload__drop-text">
                  {selectedFile ? selectedFile.name : t("dropText")}
                </span>
                <span className="jp-cvupload__drop-hint">{t("dropHint")}</span>
              </label>
              <input
                id={inputId}
                name="file"
                type="file"
                accept={ACCEPT_ATTR}
                className="jp-cvupload__input"
                onChange={handleFileChange}
                aria-describedby={describedBy || undefined}
                aria-invalid={error ? true : undefined}
              />
            </div>

            {/* showHelp=false → sr-only: describedby-kedjan består (SR hör
                instruktionen) men den synliga dubbletten mot värdens brödtext försvinner. */}
            <p id={helpId} className={showHelp ? "jp-cvupload__help" : "sr-only"}>
              {t("help")}
            </p>

            {error && (
              <p id={errorId} role="alert" className="jp-cvupload__error">
                {error}
              </p>
            )}

            {/* Auto-upload-läget har ingen submit-rad: uppladdningen startar vid filval. */}
            {!autoUpload && (
              <div className="jp-cvupload__actions">
                <button
                  type="submit"
                  className="jp-btn jp-btn--primary"
                  disabled={!selectedFile}
                >
                  {t("submit")}
                </button>
              </div>
            )}
          </>
        )}
      </form>

      <PersonnummerConsentDialog
        open={consent !== null}
        count={consent?.count ?? 0}
        saving={consentSaving}
        onConfirm={onConsentConfirm}
        onDecline={onConsentDecline}
      />
    </>
  );
}
