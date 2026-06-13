import { permanentRedirect } from "next/navigation";

/**
 * Closed-beta-disciplin (ADR 0005 Amendment 2026-05-12): publik registrering
 * är stängd. `/registrera` 308-permanent-redirectar till `/vantelista` där
 * intresseanmälningar samlas. RegisterForm-komponenten är bevarad i
 * `@/components/forms/RegisterForm` för post-launch invite-flow.
 */
export default function RegistreraPage(): never {
  permanentRedirect("/vantelista");
}
