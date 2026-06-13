"use client";

import { useEffect, useState, useTransition } from "react";
import { Controller, useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  updateMyProfileSchema,
  type UpdateMyProfileInput,
} from "@/lib/actions/me-schemas";
import { updateMyProfileAction } from "@/lib/actions/me";
import { pathToElementId } from "@/lib/forms/me-path-routing";
import type { JobSeekerProfileDto } from "@/lib/types/me";

interface MeProfileFormProps {
  initialProfile: JobSeekerProfileDto;
}

type FormValues = {
  displayName: string;
  language: "sv" | "en";
  emailNotifications: boolean;
  weeklySummary: boolean;
};

type FieldError = { path: string | null; message: string };

const ERROR_ID = "me-profile-form-error";

function normalizeLanguage(language: string): "sv" | "en" {
  return language === "en" ? "en" : "sv";
}

export function MeProfileForm({ initialProfile }: MeProfileFormProps) {
  const [isPending, startTransition] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [serverError, setServerError] = useState<FieldError | null>(null);

  const { register, handleSubmit, control } = useForm<FormValues>({
    defaultValues: {
      displayName: initialProfile.displayName,
      language: normalizeLanguage(initialProfile.language),
      emailNotifications: initialProfile.emailNotifications,
      weeklySummary: initialProfile.weeklySummary,
    },
    shouldUnregister: false,
  });

  function fieldA11y(path: string) {
    return serverError?.path === path
      ? ({ "aria-invalid": true, "aria-describedby": ERROR_ID } as const)
      : {};
  }

  useEffect(() => {
    if (!serverError?.path) return;
    const elementId = pathToElementId(serverError.path);
    if (elementId) {
      document.getElementById(elementId)?.focus();
    }
  }, [serverError]);

  function onSubmit(values: FormValues) {
    setServerError(null);
    setSavedAt(null);

    const parsed = updateMyProfileSchema.safeParse(values);
    if (!parsed.success) {
      const first = parsed.error.issues[0];
      if (first) {
        const path = first.path.join(".");
        setServerError({ path: path || null, message: first.message });
      } else {
        setServerError({ path: null, message: "Ogiltiga uppgifter." });
      }
      return;
    }

    startTransition(async () => {
      const result = await updateMyProfileAction(
        parsed.data as UpdateMyProfileInput
      );
      if (!result.success) {
        setServerError({ path: null, message: result.error });
        return;
      }
      setSavedAt(new Date());
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-6">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="me-displayName">Visningsnamn</Label>
        <Input
          id="me-displayName"
          {...register("displayName")}
          {...fieldA11y("displayName")}
          maxLength={200}
          required
          disabled={isPending}
        />
        <p className="text-body-sm text-text-secondary">
          Namnet som visas i appen och på dina ansökningar.
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="me-language">Språk</Label>
        <Controller
          control={control}
          name="language"
          render={({ field }) => (
            <Select
              value={field.value}
              onValueChange={field.onChange}
              disabled={isPending}
            >
              <SelectTrigger
                id="me-language"
                ref={field.ref}
                className="w-full"
                {...fieldA11y("language")}
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="sv">Svenska</SelectItem>
                <SelectItem value="en">Engelska</SelectItem>
              </SelectContent>
            </Select>
          )}
        />
      </div>

      <fieldset className="flex flex-col gap-3 rounded-md border border-border bg-card p-4">
        <legend className="px-1 text-label text-text-primary">
          Notifieringar
        </legend>
        <div className="flex items-start gap-3">
          <input
            id="me-emailNotifications"
            type="checkbox"
            {...register("emailNotifications")}
            {...fieldA11y("emailNotifications")}
            disabled={isPending}
            className="mt-1 size-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-50"
          />
          <div className="flex flex-col gap-0.5">
            <Label htmlFor="me-emailNotifications" className="cursor-pointer">
              E-postnotifieringar
            </Label>
            <p className="text-body-sm text-text-secondary">
              Få mejl vid viktiga händelser i ditt konto.
            </p>
          </div>
        </div>
        <div className="flex items-start gap-3">
          <input
            id="me-weeklySummary"
            type="checkbox"
            {...register("weeklySummary")}
            {...fieldA11y("weeklySummary")}
            disabled={isPending}
            className="mt-1 size-4 cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-50"
          />
          <div className="flex flex-col gap-0.5">
            <Label htmlFor="me-weeklySummary" className="cursor-pointer">
              Veckosammanfattning
            </Label>
            <p className="text-body-sm text-text-secondary">
              En veckovis översikt av dina aktiva ansökningar.
            </p>
          </div>
        </div>
      </fieldset>

      <div className="flex items-center gap-3 border-t border-border pt-6">
        <Button type="submit" disabled={isPending}>
          {isPending ? "Sparar…" : "Spara profil"}
        </Button>
        {savedAt && !serverError && (
          <p className="text-body-sm text-text-secondary" role="status">
            Sparat{" "}
            {savedAt.toLocaleTimeString("sv-SE", {
              hour: "2-digit",
              minute: "2-digit",
            })}
            .
          </p>
        )}
        {serverError && (
          <p
            id={ERROR_ID}
            className="text-body-sm text-danger-600"
            role="alert"
          >
            {serverError.message}
          </p>
        )}
      </div>
    </form>
  );
}
