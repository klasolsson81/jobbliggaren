import { redirect } from "next/navigation";

/**
 * `/foretag` (S1 #996) — the hub root redirects to the default surface, Bevakade företag (Klas
 * 2026-07-21, "Bevakade först"). The six sections that used to live here are split into focused
 * sub-pages — bevakade / sok / smarta-bevakningar / historik — sharing a persistent sub-nav
 * (`ForetagSubnav`). The top-nav "Företag" item still lands here, and this redirect preserves the
 * follow-rail watermark-on-hub-visit semantic (the Bevakade surface advances it, #801). Auth is
 * enforced by the target surface.
 */
export default function ForetagPage() {
  redirect("/foretag/bevakade");
}
