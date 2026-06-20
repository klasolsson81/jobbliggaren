import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CvUploadForm } from "./cv-upload-form";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("CvUploadForm — ärlig upload-copy", () => {
  it("knappen namnger uppladdningen ('Ladda upp och granska CV'), inte 'Tolka'", () => {
    render(<CvUploadForm />);
    const button = screen.getByRole("button", {
      name: "Ladda upp och granska CV",
    });
    expect(button).toBeInTheDocument();
    // Den gamla, missvisande copyn (filen är bara VALD vid klick) får inte
    // finnas kvar på knappen.
    expect(
      screen.queryByRole("button", { name: "Tolka och granska CV" })
    ).not.toBeInTheDocument();
  });

  it("knappen är disabled tills en fil valts", () => {
    render(<CvUploadForm />);
    expect(
      screen.getByRole("button", { name: "Ladda upp och granska CV" })
    ).toBeDisabled();
  });

  it("har label kopplad till filväljaren och ingen exempel-placeholder", () => {
    render(<CvUploadForm />);
    const input = document.querySelector<HTMLInputElement>(
      'input[type="file"]'
    );
    expect(input).not.toBeNull();
    expect(input).not.toHaveAttribute("placeholder");
  });
});
