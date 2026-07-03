// "use client": filväljaren behöver lokal state (vald fil, klient-validering),
// en submit-handler som bygger FormData + fetch:ar BFF:en, och programmatisk
// navigation efter 201. Inget CV-PII passerar klienten — bara File-objektet
// (bytesen) och det returnerade `parsedResumeId`.
"use client";

import { useId, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { FileText, Upload } from "lucide-react";
import { BrandSpinner } from "@/components/brand/brand-spinner";

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

/** Smal läsning av BFF-svaret (`/api/cv/import`) via type-guards — vi litar
 * aldrig på okänd shape, men drar inte in zod i klientbunten. Guard:arna
 * narrowar `body`, så inga `as`-cast:ar behövs. */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function readParsedResumeId(body: unknown): string | null {
  if (isRecord(body) && typeof body.parsedResumeId === "string") {
    return body.parsedResumeId;
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
   * ADR 0077 STEG 5 — om satt anropas denna med `parsedResumeId` (och det valda
   * filnamnet) vid 201 i STÄLLET för `router.push('/cv/granska/${id}')`. Låter en
   * host-modal stå kvar och visa bekräftelse-steget i stället för att navigera
   * bort. `fileName` driver "CV inläst: {filnamn}"-plattan (epik #526); äldre
   * konsumenter som ignorerar andra-argumentet är opåverkade. Default-beteendet
   * (navigera till granska-vyn) är oförändrat när proppen utelämnas.
   */
  readonly onUploaded?: (parsedResumeId: string, fileName?: string) => void;
  /**
   * Epik #526 polish (Klas rendered-verify 2026-07-03): starta uppladdningen
   * direkt när en giltig fil valts — inget extra "Ladda upp"-klick, och
   * submit-raden döljs (spinnern + värdens bekräftelse bär statusen). Default
   * false = oförändrat tvåstegsflöde (/cv/importera).
   */
  readonly autoUpload?: boolean;
  /**
   * Epik #526 polish: dölj formulärets egen hjälptext när VÄRDEN redan bär
   * instruktionen (rail-modalens Start-steg har en egen upload-rubrik + brödtext
   * — dubbel text var Klas-fynd 1). Hjälptexten förblir describedby via sr-only
   * (SR-användare behåller den). Default true = oförändrat.
   */
  readonly showHelp?: boolean;
}

export function CvUploadForm({
  onUploaded,
  autoUpload = false,
  showHelp = true,
}: CvUploadFormProps = {}) {
  const t = useTranslations("resumes.upload");
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  const inputId = useId();
  const helpId = useId();
  const errorId = useId();

  const describedBy = [helpId, error ? errorId : null]
    .filter(Boolean)
    .join(" ");

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
    // tyst död i auto-läget (code-reviewer Minor, 2026-07-03). UI:t läser
    // `selectedFile`-staten, aldrig input-värdet, så resetten är sidoeffektfri.
    event.target.value = "";

    // Epik #526 polish: auto-upload — giltig fil valdes → ladda upp direkt
    // (inget extra klick). Fel-vägen är oförändrad: väljs en ny (eller samma)
    // fil efter t.ex. 429 körs ett nytt försök automatiskt.
    if (autoUpload && !validationError && !isPending) {
      startTransition(async () => {
        await uploadFile(file);
      });
    }
  }

  async function uploadFile(file: File): Promise<void> {
    const formData = new FormData();
    formData.append("file", file);

    let response: Response;
    try {
      response = await fetch("/api/cv/import", {
        method: "POST",
        body: formData,
      });
    } catch {
      setError(t("errorGeneric"));
      return;
    }

    let body: unknown = null;
    try {
      body = await response.json();
    } catch {
      body = null;
    }

    if (response.status === 201) {
      const parsedResumeId = readParsedResumeId(body);
      if (!parsedResumeId) {
        setError(t("errorGeneric"));
        return;
      }
      // ADR 0077 STEG 5: om värden tillhandahållit en callback (welcome-modalen),
      // låt den styra nästa steg (bekräftelse i modalen) i stället för att
      // navigera bort. Default = oförändrad navigation till granska-vyn.
      if (onUploaded) {
        onUploaded(parsedResumeId, file.name);
        return;
      }
      router.push(`/cv/granska/${parsedResumeId}`);
      return;
    }

    if (response.status === 401) {
      setError(t("errorUnauthorized"));
      return;
    }

    if (response.status === 429) {
      const retryAfter = readRetryAfter(body);
      setError(
        retryAfter !== null
          ? t("errorRateLimited", { seconds: retryAfter })
          : t("errorGeneric"),
      );
      return;
    }

    setError(readErrorMessage(body) ?? t("errorGeneric"));
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
      // navigation (router.push) markerar transitionen som pågående tills den
      // nya routen är redo → spinnern står kvar över parse-väntan + navigeringen.
      await uploadFile(selectedFile);
    });
  }

  return (
    <form onSubmit={handleSubmit} className="jp-cvupload" noValidate>
      {isPending ? (
        // Spinner-doktrin (Klas 2026-06-20): under laddning visas ENBART spinnern
        // + en text som beskriver vad som görs — aldrig formuläret runt omkring
        // (undviker plottrig modal). Spinnern tar huvudytan. Minne:
        // project_spinner_usage_doctrine.
        <div className="jp-cvupload__pending" role="status" aria-live="polite">
          <BrandSpinner size={48} label={t("pendingLabel")} />
          <p className="jp-cvupload__pending-text">{t("pendingText")}</p>
        </div>
      ) : (
        <>
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
              instruktionen) men den synliga dubbletten mot värdens brödtext
              försvinner (Klas-fynd 1). */}
          <p
            id={helpId}
            className={showHelp ? "jp-cvupload__help" : "sr-only"}
          >
            {t("help")}
          </p>

          {error && (
            <p id={errorId} role="alert" className="jp-cvupload__error">
              {error}
            </p>
          )}

          {/* Auto-upload-läget har ingen submit-rad: uppladdningen startar vid
              filval och spinnern + värdens bekräftelse bär statusen. */}
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
  );
}
