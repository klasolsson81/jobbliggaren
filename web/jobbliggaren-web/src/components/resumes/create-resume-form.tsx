// "use client": useActionState (React 19 form-state-hook) kräver en klient-ö.
// Formuläret är medvetet rubrik-löst — fullsidan (`/cv/ny`) och modalen
// (@modal/(.)cv/ny) renderar var sin egen titel (page-h1 resp. shell-header),
// så samma form återanvänds i båda utan dubbel rubrik (DRY, ADR 0053).
"use client";

import Link from "next/link";
import { useActionState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { createResumeAction } from "@/lib/actions/resumes";

/**
 * CreateResumeForm — fälten för att skapa ett nytt CV. Server-actionen
 * `createResumeAction` redirectar till /cv/{id} vid 201 — en full navigation
 * som ersätter modalen med det nya CV:t (fungerar identiskt från fullsida och
 * modal). Inga placeholder-exempel i fälten (Klas hård regel) — hjälptexten
 * under labeln bär instruktionen.
 */
export function CreateResumeForm() {
  const t = useTranslations("resumes");
  const [state, formAction, isPending] = useActionState(
    createResumeAction,
    null
  );

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cv-name">{t("createForm.nameLabel")}</Label>
        <p id="cv-name-help" className="text-body-sm text-text-secondary">
          {t("createForm.nameHelp")}
        </p>
        <Input
          id="cv-name"
          name="name"
          required
          maxLength={200}
          disabled={isPending}
          aria-describedby="cv-name-help"
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cv-fullname">{t("createForm.fullNameLabel")}</Label>
        <p id="cv-fullname-help" className="text-body-sm text-text-secondary">
          {t("createForm.fullNameHelp")}
        </p>
        <Input
          id="cv-fullname"
          name="fullName"
          required
          maxLength={200}
          disabled={isPending}
          aria-describedby="cv-fullname-help"
        />
      </div>

      {state && !state.success && (
        <p role="alert" className="text-body-sm text-danger-700">
          {state.error}
        </p>
      )}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={isPending}>
          {isPending ? t("createForm.submitPending") : t("createForm.submit")}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/cv">{t("createForm.cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}
