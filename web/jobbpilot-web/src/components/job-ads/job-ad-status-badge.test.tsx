import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdStatusBadge } from "./job-ad-status-badge";

describe("JobAdStatusBadge", () => {
  it("renders Active label", () => {
    render(<JobAdStatusBadge status="Active" />);
    expect(screen.getByText("Aktiv")).toBeInTheDocument();
  });

  it("renders Expired label", () => {
    render(<JobAdStatusBadge status="Expired" />);
    expect(screen.getByText("Utgången")).toBeInTheDocument();
  });

  it("renders Archived label", () => {
    render(<JobAdStatusBadge status="Archived" />);
    expect(screen.getByText("Arkiverad")).toBeInTheDocument();
  });

  it("does not use role=status (would announce N times on list)", () => {
    const { container } = render(<JobAdStatusBadge status="Active" />);
    expect(container.querySelector("[role='status']")).toBeNull();
  });

  it("applies passed className", () => {
    render(<JobAdStatusBadge status="Active" className="extra-class" />);
    expect(screen.getByText("Aktiv").className).toContain("extra-class");
  });
});
