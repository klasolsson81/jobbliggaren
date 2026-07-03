import { ImageResponse } from "next/og";
import { BrandMarkSvg } from "@/components/brand/brand-mark-svg";
import {
  OG_MARK_ACCENT,
  OG_MARK_PAPER,
  OG_MARK_PRIMARY,
  OG_SURFACE,
  OG_TAGLINE_STYLE,
  OG_TITLE_STYLE,
} from "@/lib/og-tokens";

// Next.js 16 file convention: Twitter card-image (summary_large_image).
// Återanvänder OG-kompositionen — paritet med opengraph-image.
// Geometri från BrandMarkSvg SSOT (CTO M1-triage 2026-05-25 Variant B).

export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = "Jobbliggaren: Den svenska jobbansökningshanteraren";
export const runtime = "edge";

export default function TwitterImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: OG_SURFACE,
          padding: "80px",
          gap: "64px",
        }}
      >
        <BrandMarkSvg
          width={240}
          height={240}
          primaryFill={OG_MARK_PRIMARY}
          accentFill={OG_MARK_ACCENT}
          paperFill={OG_MARK_PAPER}
        />
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "flex-start",
            gap: "12px",
          }}
        >
          <div style={OG_TITLE_STYLE}>
            Jobbliggaren
          </div>
          <div style={OG_TAGLINE_STYLE}>
            Den svenska jobbansökningshanteraren
          </div>
        </div>
      </div>
    ),
    { ...size }
  );
}
