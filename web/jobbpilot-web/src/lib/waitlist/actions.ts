"use server";

import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import {
  waitlistEntryResponseSchema,
  waitlistFormSchema,
} from "@/lib/dto/waitlist";

export type WaitlistActionState =
  | { status: "idle" }
  | { status: "success"; email: string }
  | { status: "error"; error: string; fieldErrors?: Record<string, string> };

function coerceBool(value: FormDataEntryValue | null): boolean {
  return value === "on" || value === "true" || value === "1";
}

export async function requestWaitlistAction(
  _prevState: WaitlistActionState,
  formData: FormData,
): Promise<WaitlistActionState> {
  const parsed = waitlistFormSchema.safeParse({
    name: formData.get("name"),
    email: formData.get("email"),
    motivation: formData.get("motivation"),
    marketingEmailAccepted: coerceBool(formData.get("marketingEmailAccepted")),
  });

  if (!parsed.success) {
    const fieldErrors: Record<string, string> = {};
    for (const issue of parsed.error.issues) {
      const path = issue.path[0];
      if (typeof path === "string" && !fieldErrors[path]) {
        fieldErrors[path] = issue.message;
      }
    }
    return {
      status: "error",
      error: "Kontrollera fälten och försök igen.",
      fieldErrors,
    };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/waitlist/`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(parsed.data),
      cache: "no-store",
    });

    if (res.status === 503) {
      return {
        status: "error",
        error:
          "Anmälningar är just nu stängda. Försök igen senare när vi öppnar nästa pulse.",
      };
    }

    if (res.status === 400) {
      return {
        status: "error",
        error: "Något var fel med uppgifterna. Kontrollera och försök igen.",
      };
    }

    if (res.status === 429) {
      return {
        status: "error",
        error:
          "För många anmälningar från denna nätverksadress. Försök igen om en stund.",
      };
    }

    if (!res.ok) {
      return {
        status: "error",
        error: "Ett fel uppstod. Försök igen om en stund.",
      };
    }

    const data = await parseResponse(
      res,
      waitlistEntryResponseSchema,
      "POST /api/v1/waitlist",
    );

    return { status: "success", email: data.email };
  } catch {
    return {
      status: "error",
      error: "Kunde inte nå servern. Försök igen.",
    };
  }
}
