/**
 * OAuth-leverantörsmonogram (Google/LinkedIn/Microsoft) för AuthCard.
 *
 * SVG-paths verbatim från `src-v3/landing.jsx` OAuthMark (prototyp-källa
 * är kontrakt per Klas pre-F6 Prompt 1 förkrav). 14×14, `currentColor` så
 * de matchar omkringliggande knapp-text. `aria-hidden` eftersom etikett
 * redan finns i knapp-texten ("Google" / "LinkedIn" / "Microsoft").
 *
 * Civic-utility-stil: enkla strokes/blocks, ingen färg-fyll, inga
 * varumärkes-logotyper i full färg (skulle bryta dov palett-disciplin).
 */

export type OAuthProvider = "google" | "linkedin" | "microsoft";

interface OAuthMarkProps {
  provider: OAuthProvider;
}

export function OAuthMark({ provider }: OAuthMarkProps) {
  if (provider === "google") {
    return (
      <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        aria-hidden="true"
      >
        <path d="M12 4a8 8 0 1 0 7.6 10.5" />
        <path d="M12 11h8" />
      </svg>
    );
  }
  if (provider === "linkedin") {
    return (
      <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <rect x="3" y="9" width="3" height="11" />
        <circle cx="4.5" cy="5" r="1.4" fill="currentColor" stroke="none" />
        <path d="M10 9v11M10 13a3.5 3.5 0 0 1 7 0v7" />
      </svg>
    );
  }
  // microsoft
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="8" height="8" />
      <rect x="13" y="3" width="8" height="8" />
      <rect x="3" y="13" width="8" height="8" />
      <rect x="13" y="13" width="8" height="8" />
    </svg>
  );
}
