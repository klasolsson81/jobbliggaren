import "server-only";
import { redirect } from "next/navigation";
import { adminPreviewEnabled } from "./gate.cjs";

/**
 * The preview's runtime lock (ADR 0150 D5 (b)). The preview layout AND every preview page call it
 * first, in a `force-dynamic` segment: a layout alone does not re-render on navigation and does not
 * stop a page segment from running, so a build made with the flag but started without it would
 * still serve a page that only its layout checked.
 */
export function requireAdminPreview(): void {
  if (!adminPreviewEnabled(process.env)) redirect("/admin");
}
