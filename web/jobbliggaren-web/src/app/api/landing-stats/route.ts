import { NextResponse } from "next/server";
import { fetchLandingStats } from "@/lib/api/landing";

// Defensiv Next-disciplin (code-reviewer Mi3 2026-05-24): force-dynamic
// säkrar att route-handlern inte static-renderas vid Next-version-uppgrad
// — backend-state är inherent dynamisk även om handlern saknar request-input.
export const dynamic = "force-dynamic";

/**
 * Klient-side polling-proxy för `GET /api/v1/landing/stats` (ADR 0064).
 * `<HeaderStats />`-komponenten (client) pollar denna route var 10:e minut
 * för att hålla siffrorna live i app-headern utan att kräva page-refresh.
 *
 * <p>
 * Proxy:n låter klient-koden använda relativ URL utan att exponera
 * `BACKEND_URL` till bundlen. `fetchLandingStats` (server-only) återanvänds
 * — vi gör inte parallell-implementation av backend-anropet.
 * </p>
 * <p>
 * Vid backend-fail (null från `fetchLandingStats`) returneras 503 så
 * klienten kan behålla sitt nuvarande värde utan att skriva över klientens senast MÄTTA värde (ifall
 * Floor redan visas vill vi inte trampa över med samma värde + ny render).
 * </p>
 */
export async function GET() {
  const stats = await fetchLandingStats();
  if (!stats) {
    return NextResponse.json(
      { error: "Landing-stats kunde inte hämtas." },
      { status: 503 },
    );
  }
  // Klient-cache: matchar backendens Cache-Control: public, max-age=30 men
  // utan public-keywordet eftersom denna proxy går genom Next-server och
  // CDN/proxy-absorption inte är meningsfullt här (varje klient pollar
  // egen 10-min-loop).
  return NextResponse.json(stats, {
    headers: { "Cache-Control": "private, max-age=30" },
  });
}
