import Link from "next/link";
import { redirect } from "next/navigation";
import { ChevronLeft } from "lucide-react";
import { getServerSession } from "@/lib/auth/session";
import { CvUploadForm } from "@/components/resumes/cv-upload-form";

/**
 * /cv/importera — CV-import, steg 1 (Fas 4 STEG B, F1). RSC: auth-grind +
 * civic page-hero, sedan den interaktiva `<CvUploadForm />` (klient-ö som äger
 * filväljaren). Inget CV-PII rör servern här — bytesen strömmar via BFF:en
 * (`/api/cv/import`) direkt till backend.
 */
export default async function CvImportPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  return (
    <div className="flex flex-col gap-6">
      <Link
        href="/cv"
        className="inline-flex items-center gap-1 text-body-sm text-text-secondary hover:text-text-primary self-start"
      >
        <ChevronLeft size={16} aria-hidden="true" />
        <span>Tillbaka till CV</span>
      </Link>

      <header className="flex flex-col gap-2">
        <h1 className="jp-h1">Importera CV</h1>
        <p className="jp-lede">
          Ladda upp ditt befintliga CV som PDF eller Word-fil. Vi tolkar
          innehållet och visar en deterministisk granskning med citerad evidens.
          Du behåller kontrollen — inget ändras i ditt CV förrän du själv sparar.
        </p>
      </header>

      <CvUploadForm />
    </div>
  );
}
