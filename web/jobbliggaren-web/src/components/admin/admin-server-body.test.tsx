import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { serverRegion, toServerReading } from "@/lib/admin/host-observation";
import type { HostObservationDto } from "@/lib/dto/admin-host";
import { AdminServerBody } from "./admin-server-body";

const SAMPLED = "2026-10-10T12:00:00+00:00";
const T0 = Date.parse(SAMPLED);

// The numbers the production box showed on 2026-10-10 (runbook admin-host-observations.md).
const dto = (patch: Partial<HostObservationDto> = {}): HostObservationDto => ({
  readAt: "2026-10-10T12:00:10Z",
  staleAfterSeconds: 120,
  cpu: { state: "Available", sampledAt: SAMPLED, value: { percent: 5, windowSeconds: 30 } },
  memory: { state: "Available", sampledAt: SAMPLED, value: { percent: 29.4, usedBytes: 2_449_854_464, totalBytes: 8_331_255_808 } },
  disk: { state: "Available", sampledAt: SAMPLED, value: { percent: 6.1, freeBytes: 242_287_181_824, totalBytes: 258_154_033_152 } },
  ...patch,
});

const row = (label: string) => screen.getByText(label, { selector: "dt" }).nextElementSibling as HTMLElement;
const renderBody = (patch?: Partial<HostObservationDto>, now?: number) =>
  render(<AdminServerBody region={serverRegion(toServerReading(dto(patch)), now)} />);

describe("AdminServerBody with every reading available", () => {
  it("shows each reading as a percent with what it measures", () => {
    renderBody();

    expect(row("CPU")).toHaveTextContent("5,0 %Snitt över 30 s");
    expect(row("Minne")).toHaveTextContent("29,4 %2,3 av 7,8 GiB");
    expect(row("Disk")).toHaveTextContent("6,1 %225,6 GiB ledigt av 240,4 GiB");
  });

  it("fills each meter by the percent the backend sent, and hides the track from assistive technology", () => {
    renderBody();

    const fills = document.querySelectorAll<HTMLElement>(".jp-adminmeter__fill");
    expect([...fills].map((fill) => fill.style.inlineSize)).toEqual(["5%", "29.4%", "6.1%"]);
    document.querySelectorAll(".jp-adminmeter__track").forEach((track) => expect(track).toHaveAttribute("aria-hidden", "true"));
  });

  it("prints when the host was sampled, and says nothing about age while the readings are young", () => {
    const { container } = renderBody();

    expect(container).toHaveTextContent("Mätt 2026-10-10 14:00");
    expect(container).not.toHaveTextContent("äldre än");
    expect(container.querySelector("time")).toHaveAttribute("datetime", SAMPLED);
  });

  it("shows a real zero as a value and not as an unknown", () => {
    renderBody({ cpu: { state: "Available", sampledAt: SAMPLED, value: { percent: 0, windowSeconds: 30 } } });

    expect(row("CPU")).toHaveTextContent("0,0 %");
    expect(row("CPU")).not.toHaveTextContent("–");
  });

  it("shows a full meter at one hundred percent without overflowing the track", () => {
    renderBody({ cpu: { state: "Available", sampledAt: SAMPLED, value: { percent: 100, windowSeconds: 30 } } });

    expect(row("CPU")).toHaveTextContent("100,0 %");
    expect((row("CPU").querySelector(".jp-adminmeter__fill") as HTMLElement).style.inlineSize).toBe("100%");
  });
});

describe("AdminServerBody with readings that are not values", () => {
  it("shows the first CPU window as being measured while memory and disk already have values", () => {
    renderBody({ cpu: { state: "Collecting", sampledAt: null, value: null } });

    expect(row("CPU")).toHaveTextContent("–Uppgift saknasMäter…");
    expect(row("CPU").querySelector(".jp-adminmeter")).toBeNull();
    expect(row("Minne")).toHaveTextContent("29,4 %");
    expect(row("Disk")).toHaveTextContent("6,1 %");
  });

  it("says a reading is not measured here, in words that are not 'Kommer snart'", () => {
    const { container } = renderBody({
      cpu: { state: "NotObservable", sampledAt: null, value: null },
      memory: { state: "NotObservable", sampledAt: null, value: null },
    });

    expect(row("CPU")).toHaveTextContent("–Uppgift saknasMäts inte i den här miljön");
    expect(row("Minne")).toHaveTextContent("–Uppgift saknasMäts inte i den här miljön");
    expect(container).not.toHaveTextContent("Kommer snart");
  });

  it("shows one failed reading as failed and leaves the others standing", () => {
    renderBody({ disk: { state: "Failed", sampledAt: null, value: null } });

    expect(row("Disk")).toHaveTextContent("–Uppgift saknasMätningen misslyckades");
    expect(row("CPU")).toHaveTextContent("5,0 %");
    expect(row("Minne")).toHaveTextContent("29,4 %");
  });

  it("prints no sample time when no reading has a value", () => {
    const none = { sampledAt: null, value: null } as const;
    const { container } = renderBody({
      cpu: { state: "Collecting", ...none },
      memory: { state: "Failed", ...none },
      disk: { state: "NotObservable", ...none },
    });

    expect(container.querySelector("time")).toBeNull();
    expect(container).not.toHaveTextContent("Mätt");
  });
});

describe("AdminServerBody and age", () => {
  it("marks a reading the API itself calls stale, keeping its value and its original time", () => {
    const { container } = renderBody({ disk: { ...dto().disk, state: "Stale" } });

    expect(row("Disk")).toHaveTextContent("6,1 %");
    expect(container).toHaveTextContent("Mätningen är äldre än 2 min.");
    expect(container).toHaveTextContent("Mätt 2026-10-10 14:00");
  });

  it("marks a kept reading stale once the page's clock passes the limit the API applied", () => {
    const young = renderBody(undefined, T0 + 120_000);
    expect(young.container).not.toHaveTextContent("äldre än");
    young.unmount();

    const { container } = renderBody(undefined, T0 + 120_001);
    expect(container).toHaveTextContent("Mätningen är äldre än 2 min.");
    expect(row("CPU")).toHaveTextContent("5,0 %");
  });
});

describe("AdminServerBody in the region's other states", () => {
  it.each([
    [{ kind: "unavailable" } as const, "Kommer snart"],
    [{ kind: "failed" } as const, "Uppgifterna kunde inte hämtas"],
    [{ kind: "loading" } as const, "Hämtar uppgifter"],
  ])("keeps the three rows unknown and says %o", (region, text) => {
    const { container } = render(<AdminServerBody region={region} />);

    expect(container).toHaveTextContent(text);
    for (const label of ["CPU", "Minne", "Disk"]) {
      expect(row(label)).toHaveTextContent("–");
      expect(row(label).querySelector(".jp-adminmeter")).toBeNull();
    }
    expect(container.querySelector("time")).toBeNull();
  });
});
