// "use client": filväljaren behöver lokal state (vald fil, klient-validering),
// en submit-handler som bygger FormData + fetch:ar BFF:en, och programmatisk
// navigation efter 201. Inget CV-PII passerar klienten — bara File-objektet
// (bytesen) och det returnerade `parsedResumeId`.
"use client";

import { useId, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
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

const ERROR_WRONG_TYPE = "Välj en PDF- eller Word-fil (DOCX).";
const ERROR_TOO_LARGE = "Filen är för stor. Maxstorlek är 10 MB.";
const ERROR_NO_FILE = "Välj en fil att importera.";
const ERROR_GENERIC =
  "Något gick fel vid importen. Försök igen om en stund.";
const ERROR_UNAUTHORIZED = "Du är inte inloggad.";

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

/** Klient-validering före upload. Returnerar svenskt felmeddelande eller null. */
function validateFile(file: File): string | null {
  if (!isAcceptedFile(file)) return ERROR_WRONG_TYPE;
  if (file.size > MAX_UPLOAD_BYTES) return ERROR_TOO_LARGE;
  return null;
}

interface CvUploadFormProps {
  /**
   * ADR 0077 STEG 5 — om satt anropas denna med `parsedResumeId` vid 201 i
   * STÄLLET för `router.push('/cv/granska/${id}')`. Låter welcome-modalen stå
   * kvar och visa bekräftelse-steget i stället för att navigera bort.
   * Default-beteendet (navigera till granska-vyn) är oförändrat när proppen
   * utelämnas.
   */
  readonly onUploaded?: (parsedResumeId: string) => void;
}

export function CvUploadForm({ onUploaded }: CvUploadFormProps = {}) {
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
    setError(validationError);
    setSelectedFile(validationError ? null : file);
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
      setError(ERROR_GENERIC);
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
        setError(ERROR_GENERIC);
        return;
      }
      // ADR 0077 STEG 5: om värden tillhandahållit en callback (welcome-modalen),
      // låt den styra nästa steg (bekräftelse i modalen) i stället för att
      // navigera bort. Default = oförändrad navigation till granska-vyn.
      if (onUploaded) {
        onUploaded(parsedResumeId);
        return;
      }
      router.push(`/cv/granska/${parsedResumeId}`);
      return;
    }

    if (response.status === 401) {
      setError(ERROR_UNAUTHORIZED);
      return;
    }

    if (response.status === 429) {
      const retryAfter = readRetryAfter(body);
      setError(
        retryAfter !== null
          ? `För många uppladdningar. Vänta ${retryAfter} sekunder och försök igen.`
          : ERROR_GENERIC,
      );
      return;
    }

    setError(readErrorMessage(body) ?? ERROR_GENERIC);
  }

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isPending) return;

    if (!selectedFile) {
      setError(ERROR_NO_FILE);
      return;
    }
    const validationError = validateFile(selectedFile);
    if (validationError) {
      setError(validationError);
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
          <BrandSpinner size={48} label="Laddar upp och granskar ditt CV" />
          <p className="jp-cvupload__pending-text">
            Laddar upp och granskar ditt CV. Det kan ta en liten stund.
          </p>
        </div>
      ) : (
        <>
          <div className="jp-cvupload__field">
            <label htmlFor={inputId} className="jp-cvupload__drop">
              <span className="jp-cvupload__drop-icon" aria-hidden="true">
                {selectedFile ? <FileText size={22} /> : <Upload size={22} />}
              </span>
              <span className="jp-cvupload__drop-text">
                {selectedFile ? selectedFile.name : "Välj en fil att ladda upp"}
              </span>
              <span className="jp-cvupload__drop-hint">PDF eller Word (DOCX)</span>
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

          <p id={helpId} className="jp-cvupload__help">
            Ladda upp en PDF- eller Word-fil (DOCX) på högst 10 MB. Vi tolkar
            innehållet och visar en granskning. Ditt CV ändras inte.
          </p>

          {error && (
            <p id={errorId} role="alert" className="jp-cvupload__error">
              {error}
            </p>
          )}

          <div className="jp-cvupload__actions">
            <button
              type="submit"
              className="jp-btn jp-btn--primary"
              disabled={!selectedFile}
            >
              Ladda upp och granska CV
            </button>
          </div>
        </>
      )}
    </form>
  );
}
