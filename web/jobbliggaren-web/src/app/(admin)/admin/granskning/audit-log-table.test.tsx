import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { AuditLogTable } from "./audit-log-table";
import type { AuditLogEntryDto } from "@/lib/types/admin";

function entry(overrides: Partial<AuditLogEntryDto> = {}): AuditLogEntryDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    occurredAt: "2026-05-11T08:32:15.000Z",
    correlationId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    userId: "abc123ab-1111-2222-3333-444444444444",
    impersonatedBy: null,
    eventType: "Application.Created",
    aggregateType: "Application",
    aggregateId: "deadbeef-1111-2222-3333-444444444444",
    ipAddress: "192.0.2.1",
    userAgent: "Mozilla/5.0",
    ...overrides,
  };
}

describe("AuditLogTable", () => {
  it("renders empty state when no entries", () => {
    render(<AuditLogTable entries={[]} />);
    expect(screen.getByText("Inga poster matchar filtret")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("renders one row per entry", () => {
    render(<AuditLogTable entries={[entry(), entry({ id: "22", eventType: "Resume.Created" })]} />);
    expect(screen.getAllByRole("row")).toHaveLength(3); // 1 header + 2 data
  });

  it("renders event-type, aggregate-type, ip-address verbatim", () => {
    render(<AuditLogTable entries={[entry()]} />);
    expect(screen.getByText("Application.Created")).toBeInTheDocument();
    expect(screen.getByText("Application")).toBeInTheDocument();
    expect(screen.getByText("192.0.2.1")).toBeInTheDocument();
  });

  it("shows 'system' placeholder for entries without userId", () => {
    render(<AuditLogTable entries={[entry({ userId: null })]} />);
    expect(screen.getByText("system")).toBeInTheDocument();
  });

  it("shows '–' placeholder for missing ip/userAgent", () => {
    render(<AuditLogTable entries={[entry({ ipAddress: null, userAgent: null })]} />);
    const dashes = screen.getAllByText("–");
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  it("formats occurredAt in Swedish locale shape (YYYY-MM-DD HH:mm:ss) with Europe/Stockholm timezone", () => {
    render(<AuditLogTable entries={[entry({ occurredAt: "2026-05-11T08:32:15.000Z" })]} />);
    // Europe/Stockholm är CEST (UTC+2) i maj → 08:32 UTC = 10:32 lokalt.
    // Format kommer från Intl.DateTimeFormat("sv-SE") med explicit timeZone-option.
    expect(screen.getByText("2026-05-11 10:32:15")).toBeInTheDocument();
  });

  it("table has aria-label for screen readers", () => {
    render(<AuditLogTable entries={[entry()]} />);
    expect(screen.getByRole("table", { name: "Granskningsposter" })).toBeInTheDocument();
  });

  it("truncates long userAgent via title attribute", () => {
    const longAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0";
    const { container } = render(<AuditLogTable entries={[entry({ userAgent: longAgent })]} />);
    const cell = container.querySelector(`[title="${longAgent}"]`);
    expect(cell).not.toBeNull();
  });

  it("uses short-id form (first 8 chars) for aggregate and user IDs", () => {
    render(<AuditLogTable entries={[entry()]} />);
    expect(screen.getByText("deadbeef")).toBeInTheDocument(); // aggregateId first 8
    expect(screen.getByText("abc123ab")).toBeInTheDocument(); // userId first 8
  });

  it("renders table headers", () => {
    render(<AuditLogTable entries={[entry()]} />);
    const headers = screen.getAllByRole("columnheader");
    const labels = headers.map((h) => h.textContent);
    expect(labels).toEqual(["Tidpunkt", "Användare", "Händelse", "Aggregat", "IP", "Klient"]);
  });
});
