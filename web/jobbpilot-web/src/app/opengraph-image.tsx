import { ImageResponse } from "next/og";
import { BrandMarkSvg } from "@/components/brand/brand-mark-svg";

// Next.js 16 file convention: site-wide OG-image, visas i Slack/LinkedIn/Twitter/Facebook
// link-previews. Storlek per Open Graph spec: 1200×630.
// Klas-STOPP A val H2 (2026-05-25): tagline "Den svenska jobbansökningshanteraren".
// Geometri från BrandMarkSvg SSOT (CTO M1-triage 2026-05-25 Variant B).

export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = "JobbPilot — Den svenska jobbansökningshanteraren";
export const runtime = "edge";

export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#FFFFFF",
          padding: "80px",
          gap: "64px",
        }}
      >
        <BrandMarkSvg width={240} height={240} primaryFill="#0A2647" accentFill="#FFCD00" />
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "flex-start",
            gap: "12px",
          }}
        >
          <div
            style={{
              fontSize: "112px",
              fontWeight: 700,
              color: "#0A2647",
              letterSpacing: "-0.025em",
              lineHeight: 1,
            }}
          >
            JobbPilot
          </div>
          <div
            style={{
              fontSize: "32px",
              fontWeight: 500,
              color: "#133F73",
              lineHeight: 1.3,
            }}
          >
            Den svenska jobbansökningshanteraren
          </div>
        </div>
      </div>
    ),
    { ...size }
  );
}
