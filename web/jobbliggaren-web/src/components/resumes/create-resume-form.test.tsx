import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CreateResumeForm } from "./create-resume-form";

// createResumeAction är en server-action; i jsdom mockar vi den till en
// no-op så useActionState kan montera utan att nå nätverket.
vi.mock("@/lib/actions/resumes", () => ({
  createResumeAction: vi.fn(),
}));

describe("CreateResumeForm", () => {
  it("renderar båda fälten med kopplade labels (htmlFor/id)", () => {
    render(<CreateResumeForm />);
    expect(screen.getByLabelText("Namn på CV")).toBeInTheDocument();
    expect(screen.getByLabelText("Fullständigt namn")).toBeInTheDocument();
  });

  it("har inga placeholder-exempel i fälten (Klas hård regel)", () => {
    render(<CreateResumeForm />);
    expect(screen.getByLabelText("Namn på CV")).not.toHaveAttribute(
      "placeholder"
    );
    expect(screen.getByLabelText("Fullständigt namn")).not.toHaveAttribute(
      "placeholder"
    );
  });

  it("kopplar hjälptext via aria-describedby", () => {
    render(<CreateResumeForm />);
    const nameInput = screen.getByLabelText("Namn på CV");
    const describedBy = nameInput.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent(
      "Master-CV"
    );
  });

  it("renderar primär 'Skapa CV'-knapp och 'Avbryt'-länk till /cv", () => {
    render(<CreateResumeForm />);
    expect(
      screen.getByRole("button", { name: "Skapa CV" })
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Avbryt" })).toHaveAttribute(
      "href",
      "/cv"
    );
  });

  it("renderar INGEN egen rubrik (titeln ägs av sida/modal-header)", () => {
    render(<CreateResumeForm />);
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
  });
});
