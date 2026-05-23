"use server";

import { revalidatePath } from "next/cache";
import { saveJobAd, unsaveJobAd } from "@/lib/api/saved-job-ads";

export type SaveJobAdResult =
  | { success: true }
  | { success: false; error: string };

/**
 * F6 P5 Punkt 2 Del A — server-action för att bokmärka en annons.
 * Idempotent (backend hanterar redan-sparad). Revalidate:ar `/sparade`
 * + `/jobb`-listan så list-vyer reflekterar nya bokmärket.
 */
export async function saveJobAdAction(jobAdId: string): Promise<SaveJobAdResult> {
  const result = await saveJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sparade");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: "Du är inte inloggad." };
    case "notFound":
      return {
        success: false,
        error: "Annonsen kunde inte hittas. Den kan ha tagits bort.",
      };
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: "Kunde inte spara annonsen. Försök igen." };
  }
}

/**
 * F6 P5 Punkt 2 Del A — server-action för att ta bort ett bokmärke.
 * Idempotent. Revalidate:ar `/sparade` + `/jobb`-listan.
 */
export async function unsaveJobAdAction(jobAdId: string): Promise<SaveJobAdResult> {
  const result = await unsaveJobAd(jobAdId);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sparade");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: "Du är inte inloggad." };
    case "notFound":
    case "forbidden":
    case "rateLimited":
    case "error":
      return { success: false, error: "Kunde inte ta bort bokmärket. Försök igen." };
  }
}
