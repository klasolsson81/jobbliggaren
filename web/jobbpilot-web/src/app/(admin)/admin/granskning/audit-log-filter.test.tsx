import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuditLogFilter } from "./audit-log-filter";

describe("AuditLogFilter", () => {
  it("renders 5 named inputs (from, to, eventType, aggregateType, userId)", () => {
    const { container } = render(<AuditLogFilter current={{}} />);
    expect(container.querySelector('input[name="from"]')).not.toBeNull();
    expect(container.querySelector('input[name="to"]')).not.toBeNull();
    expect(container.querySelector('input[name="eventType"]')).not.toBeNull();
    expect(container.querySelector('input[name="aggregateType"]')).not.toBeNull();
    expect(container.querySelector('input[name="userId"]')).not.toBeNull();
  });

  it("form has method=get and action=/admin/granskning", () => {
    const { container } = render(<AuditLogFilter current={{}} />);
    const form = container.querySelector("form");
    expect(form).not.toBeNull();
    expect(form?.getAttribute("method")).toBe("get");
    expect(form?.getAttribute("action")).toBe("/admin/granskning");
  });

  it("has aria-label on form for a11y", () => {
    render(<AuditLogFilter current={{}} />);
    expect(
      screen.getByRole("form", { name: "Filtrera granskningsloggen" })
    ).toBeInTheDocument();
  });

  it("pre-populates defaultValue from current filter state", () => {
    const { container } = render(
      <AuditLogFilter
        current={{
          eventType: "Application.Created",
          aggregateType: "Application",
          userId: "abc-123",
        }}
      />
    );
    expect(
      (container.querySelector('input[name="eventType"]') as HTMLInputElement).value
    ).toBe("Application.Created");
    expect(
      (container.querySelector('input[name="aggregateType"]') as HTMLInputElement).value
    ).toBe("Application");
    expect(
      (container.querySelector('input[name="userId"]') as HTMLInputElement).value
    ).toBe("abc-123");
  });

  it("truncates ISO-string defaultValue to YYYY-MM-DDTHH:mm for datetime-local", () => {
    const { container } = render(
      <AuditLogFilter
        current={{
          from: "2026-05-11T08:32:15.000Z",
          to: "2026-05-12T18:45:30.500Z",
        }}
      />
    );
    expect(
      (container.querySelector('input[name="from"]') as HTMLInputElement).value
    ).toBe("2026-05-11T08:32");
    expect(
      (container.querySelector('input[name="to"]') as HTMLInputElement).value
    ).toBe("2026-05-12T18:45");
  });

  it("uses empty string default when current filter is empty", () => {
    const { container } = render(<AuditLogFilter current={{}} />);
    expect(
      (container.querySelector('input[name="from"]') as HTMLInputElement).value
    ).toBe("");
    expect(
      (container.querySelector('input[name="eventType"]') as HTMLInputElement).value
    ).toBe("");
  });

  it("renders Rensa-link pointing to /admin/granskning without params", () => {
    render(<AuditLogFilter current={{}} />);
    const clearLink = screen.getByRole("link", { name: "Rensa" });
    expect(clearLink).toHaveAttribute("href", "/admin/granskning");
  });
});
