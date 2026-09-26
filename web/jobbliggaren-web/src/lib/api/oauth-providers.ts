import "server-only";
import { z } from "zod";
import { toExternalProviderKey, type ExternalProviderKey } from "@/lib/auth/external-login";
import { env } from "@/lib/env";

/** MIRROR of the backend `ExternalLoginPolicy.ProvidersListMaxAge`, the list's `Cache-Control` max-age. */
const PROVIDERS_REVALIDATE_SECONDS = 300;

const providersSchema = z.array(z.string());

/**
 * The providers `/logga-in` may offer as live (#1744, ADR 0142 D8): the keys the api registered, in
 * the order it lists them, narrowed to the keys this build knows. Anonymous and cached, and it fails
 * closed: any failure is `[]`, which is the page's order without a provider. A key this build does
 * not know is dropped, so an api ahead of the web offers nothing the web cannot start.
 */
export async function getExternalLoginProviders(): Promise<ExternalProviderKey[]> {
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/oauth/providers`, {
      next: { revalidate: PROVIDERS_REVALIDATE_SECONDS },
    });
    if (!res.ok) return [];
    const parsed = providersSchema.safeParse(await res.json());
    if (!parsed.success) return [];
    return parsed.data.flatMap((raw) => {
      const key = toExternalProviderKey(raw);
      return key === null ? [] : [key];
    });
  } catch {
    return [];
  }
}
