"use server";

import { env } from "@/lib/env";
import { parseResponse } from "@/lib/dto/_helpers";
import { waitlistEntryResponseSchema } from "@/lib/dto/waitlist";

export type WaitlistActionState =
  | { status: "idle" }
  | { status: "success"; email: string }
  | { status: "error"; error: string };

export async function requestWaitlistAction(
  _prevState: WaitlistActionState,
  formData: FormData,
): Promise<WaitlistActionState> {
  const email = formData.get("email") as string | null;

  if (!email) {
    return { status: "error", error: "E-postadress krävs." };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/waitlist/`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
      cache: "no-store",
    });

    if (res.status === 503) {
      return {
        status: "error",
        error:
          "Registreringar är just nu stängda. Försök igen senare när vi öppnar nästa pulse.",
      };
    }

    if (res.status === 400) {
      return {
        status: "error",
        error: "E-postadressen har fel format.",
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
