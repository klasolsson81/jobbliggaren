"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { z } from "zod";
import {
  createCriterion,
  updateCriterion,
  deleteCriterion,
  type CriterionWriteResult,
} from "@/lib/api/company-criteria";

/**
 * #560 PR-3 — server actions for the criteria-based company watches. Mirrors
 * `lib/actions/company-follows.ts`: Zod-validate the input, delegate to the server fetcher, map the
 * result to a discriminated `{ success }` union with Swedish error strings, and `revalidatePath`.
 *
 * The dialog closes BEFORE the revalidate lands (the caller's job, the #141 trap), so a successful
 * save never unmounts the open dialog mid-flow.
 */

export type CriterionActionResult =
  | { success: true }
  | { success: false; error: string };

/**
 * The write input from the picker. Both axes require at least one code (the backend requires it too —
 * "minst en bransch" / "minst en kommun"); the dialog disables save until both are chosen, so this
 * min(1) is a backstop. `label` is normalised to a TRIMMED string (possibly empty); the actions then
 * apply the create-vs-update label semantics (below).
 */
const criterionFormInputSchema = z.object({
  sniCodes: z.array(z.string().min(1)).min(1),
  municipalityCodes: z.array(z.string().min(1)).min(1),
  label: z
    .string()
    .max(120)
    .nullish()
    .transform((value) => (value ?? "").trim()),
});

export type CriterionFormInput = z.input<typeof criterionFormInputSchema>;

export async function createCriterionAction(
  input: CriterionFormInput,
): Promise<CriterionActionResult> {
  const t = await getTranslations("pages.foretag.criteria.errors");

  const parsed = criterionFormInputSchema.safeParse(input);
  if (!parsed.success) return { success: false, error: t("invalidInput") };

  // Create: an empty label means "no name" (the aggregate normalises blank → null anyway); send null.
  const result = await createCriterion(
    { sniCodes: parsed.data.sniCodes, municipalityCodes: parsed.data.municipalityCodes },
    parsed.data.label.length > 0 ? parsed.data.label : null,
  );

  if (result.kind === "ok") {
    revalidatePath("/foretag/smarta-bevakningar");
    return { success: true };
  }
  return { success: false, error: writeError(result, t, "createFailed") };
}

export async function updateCriterionAction(
  criterionId: string,
  input: CriterionFormInput,
): Promise<CriterionActionResult> {
  const t = await getTranslations("pages.foretag.criteria.errors");

  const parsed = criterionFormInputSchema.safeParse(input);
  if (!parsed.success) return { success: false, error: t("invalidInput") };

  // The edit dialog always carries the whole predicate + the label field, so both members are sent:
  // `criteria` replaces the full predicate. `label` is sent as the TRIMMED string, INCLUDING "" — the
  // backend's PATCH semantics treat a present blank label as CLEAR and an ABSENT (null) label as
  // untouched, so an emptied field must travel as "" (never null) to actually clear the name.
  const result = await updateCriterion(criterionId, {
    label: parsed.data.label,
    criteria: {
      sniCodes: parsed.data.sniCodes,
      municipalityCodes: parsed.data.municipalityCodes,
    },
  });

  if (result.kind === "ok") {
    revalidatePath("/foretag/smarta-bevakningar");
    return { success: true };
  }
  return { success: false, error: writeError(result, t, "updateFailed") };
}

export async function deleteCriterionAction(
  criterionId: string,
): Promise<CriterionActionResult> {
  const t = await getTranslations("pages.foretag.criteria.errors");

  const result = await deleteCriterion(criterionId);
  switch (result.kind) {
    case "ok":
    // A repeat delete is 404 (the row is already gone) — the same UX outcome as a fresh delete, so it
    // is treated as success-equivalent rather than surfacing an error for an already-completed intent.
    case "notFound":
      revalidatePath("/foretag/smarta-bevakningar");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "rateLimited":
      return {
        success: false,
        error: t("rateLimited", { seconds: result.retryAfterSeconds }),
      };
    case "forbidden":
    case "error":
      return { success: false, error: t("deleteFailed") };
  }
}

type CriterionErrorsTranslator = Awaited<
  ReturnType<typeof getTranslations<"pages.foretag.criteria.errors">>
>;

/**
 * Map a write failure to a Swedish message. The two user-relevant backend messages (400 unknown-codes
 * detail, 409 max-per-user) are surfaced verbatim when present; every other outcome uses a generic
 * i18n string keyed off the operation (`createFailed`/`updateFailed`).
 */
function writeError(
  result: Exclude<CriterionWriteResult<unknown>, { kind: "ok" }>,
  t: CriterionErrorsTranslator,
  genericKey: "createFailed" | "updateFailed",
): string {
  switch (result.kind) {
    case "unauthorized":
      return t("notLoggedIn");
    case "validation":
      return result.message ?? t("validationFailed");
    case "conflict":
      return result.message ?? t("maxPerUser");
    case "notFound":
      return t("notFound");
    case "rateLimited":
      return t("rateLimited", { seconds: result.retryAfterSeconds });
    case "error":
      return t(genericKey);
  }
}
