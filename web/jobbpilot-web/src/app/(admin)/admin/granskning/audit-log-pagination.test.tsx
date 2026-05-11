import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuditLogPagination } from "./audit-log-pagination";

function buildHref(p: number) {
  return `/admin/granskning?page=${p}`;
}

describe("AuditLogPagination", () => {
  it("shows current page, total pages, and total count", () => {
    render(
      <AuditLogPagination page={2} totalPages={5} totalCount={123} buildHref={buildHref} />
    );
    expect(screen.getByText(/Sida 2 av 5/)).toBeInTheDocument();
    expect(screen.getByText(/123 poster totalt/)).toBeInTheDocument();
  });

  it("renders Previous as link when page > 1", () => {
    render(
      <AuditLogPagination page={3} totalPages={5} totalCount={100} buildHref={buildHref} />
    );
    const prev = screen.getByRole("link", { name: /Föregående/ });
    expect(prev).toHaveAttribute("href", "/admin/granskning?page=2");
  });

  it("renders Next as link when page < totalPages", () => {
    render(
      <AuditLogPagination page={3} totalPages={5} totalCount={100} buildHref={buildHref} />
    );
    const next = screen.getByRole("link", { name: /Nästa/ });
    expect(next).toHaveAttribute("href", "/admin/granskning?page=4");
  });

  it("renders Previous as aria-disabled span on first page", () => {
    render(
      <AuditLogPagination page={1} totalPages={3} totalCount={50} buildHref={buildHref} />
    );
    expect(screen.queryByRole("link", { name: /Föregående/ })).not.toBeInTheDocument();
    const disabled = screen.getByText(/Föregående/);
    expect(disabled).toHaveAttribute("aria-disabled", "true");
  });

  it("renders Next as aria-disabled span on last page", () => {
    render(
      <AuditLogPagination page={3} totalPages={3} totalCount={50} buildHref={buildHref} />
    );
    expect(screen.queryByRole("link", { name: /Nästa/ })).not.toBeInTheDocument();
    const disabled = screen.getByText(/Nästa/);
    expect(disabled).toHaveAttribute("aria-disabled", "true");
  });

  it("handles zero results gracefully", () => {
    render(
      <AuditLogPagination page={1} totalPages={0} totalCount={0} buildHref={buildHref} />
    );
    expect(screen.getByText(/Sida 1 av 1/)).toBeInTheDocument();
    expect(screen.getByText(/0 poster totalt/)).toBeInTheDocument();
  });

  it("uses nav landmark with aria-label", () => {
    render(
      <AuditLogPagination page={1} totalPages={1} totalCount={0} buildHref={buildHref} />
    );
    expect(screen.getByRole("navigation", { name: "Sidnavigering" })).toBeInTheDocument();
  });
});
