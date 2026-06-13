import { type NextRequest, NextResponse } from "next/server";

// Defense-in-depth (ADR 0017): middleware blockerar oautentiserad noise innan
// request når BE; layout/page re-verifierar via getServerSession().
// security-auditor M-2 (2026-05-24): listan ska spegla `(app)`-route-listan.
const PROTECTED_PREFIXES = [
  "/installningar",
  "/mig",
  "/ansokningar",
  "/cv",
  "/oversikt",
  "/jobb",
  "/sokningar",
  "/sparade",
];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  const isProtected = PROTECTED_PREFIXES.some((prefix) =>
    pathname.startsWith(prefix)
  );

  if (isProtected) {
    // Cheap cookie presence check — actual session validation happens in the Server Component.
    // Per ADR 0017 §defense-in-depth: middleware blocks unauthenticated noise; layout re-verifies.
    const hasSession = request.cookies.has("__Host-jobbliggaren_session");
    if (!hasSession) {
      const loginUrl = new URL("/logga-in", request.url);
      loginUrl.searchParams.set("next", pathname);
      return NextResponse.redirect(loginUrl);
    }
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/((?!_next/static|_next/image|favicon.ico|api/).*)",
  ],
};
