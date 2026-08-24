"use client";

import Link from "next/link";
import { useActionState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { createApplicationAction } from "@/lib/actions/applications";

/**
 * The manual create-application form, extracted from `/ny-ansokan/page.tsx`.
 *
 * It is `"use client"` because `useActionState` is, and that is exactly why it is a
 * component rather than the page: Next refuses `generateMetadata` from a client
 * module, so a page that IS this form cannot carry a document title (WCAG 2.4.2).
 * Next's own error says the same thing — keep the page a Server Component and move
 * the Client Component logic to a separate file. The heading stays on the page,
 * which now renders it server-side; only the form ships as client JS.
 *
 * Sibling precedent for the shape: `add-note-form.tsx`, `add-follow-up-form.tsx`.
 */
export function NewApplicationForm() {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState(
    createApplicationAction,
    null
  );

  return (
    <form action={formAction} className="flex max-w-lg flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="title">
          {t("ansokningar.new.titleLabel")}{" "}
          <span aria-hidden="true" className="text-danger-600">
            *
          </span>
        </Label>
        <Input
          id="title"
          name="title"
          required
          aria-required="true"
          disabled={isPending}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="company">
          {t("ansokningar.new.companyLabel")}{" "}
          <span aria-hidden="true" className="text-danger-600">
            *
          </span>
        </Label>
        <Input
          id="company"
          name="company"
          required
          aria-required="true"
          disabled={isPending}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="url">{t("ansokningar.new.urlLabel")}</Label>
        <Input
          id="url"
          name="url"
          type="url"
          inputMode="url"
          aria-describedby="url-hint"
          disabled={isPending}
        />
        <p id="url-hint" className="text-body-sm text-text-primary">
          {t("ansokningar.new.urlHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="expiresAt">{t("ansokningar.new.expiresAtLabel")}</Label>
        <Input
          id="expiresAt"
          name="expiresAt"
          type="date"
          aria-describedby="expires-hint"
          disabled={isPending}
        />
        <p id="expires-hint" className="text-body-sm text-text-primary">
          {t("ansokningar.new.expiresAtHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cover-letter">
          {t("ansokningar.new.coverLetterLabel")}
        </Label>
        <Textarea
          id="cover-letter"
          name="coverLetter"
          rows={8}
          aria-describedby="cover-letter-hint"
          disabled={isPending}
        />
        <p
          id="cover-letter-hint"
          className="text-body-sm text-text-primary"
        >
          {t("ansokningar.new.coverLetterHint")}
        </p>
      </div>

      {state && !state.success && (
        <p role="alert" className="text-body-sm text-danger-700">
          {state.error}
        </p>
      )}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={isPending}>
          {isPending
            ? t("ansokningar.new.submitting")
            : t("ansokningar.new.submit")}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/ansokningar">{t("ansokningar.new.cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}
