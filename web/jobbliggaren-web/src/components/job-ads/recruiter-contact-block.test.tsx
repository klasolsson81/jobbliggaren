import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { RecruiterContactBlock } from "./recruiter-contact-block";
import type { AdContactDto } from "@/lib/dto/job-ads";

// The test render shim wraps every render in NextIntlClientProvider with the
// Swedish catalog (src/test/render-intl.tsx), so useTranslations("jobads.ui.
// contact") resolves the real strings ("Kontakt" / "Från annonstexten" / …).

const declared: AdContactDto = {
  name: "Anna Svensson",
  role: "Rekryterare",
  email: "anna.svensson@acme.se",
  phone: "070-123 45 67",
  isDerived: false,
};

describe("RecruiterContactBlock (#842 PR4)", () => {
  it("renders nothing when there are no contacts", () => {
    const { container } = render(<RecruiterContactBlock contacts={[]} />);
    expect(container.firstChild).toBeNull();
  });

  it("renders a declared contact's name, role, mailto and tel links", () => {
    render(<RecruiterContactBlock contacts={[declared]} />);

    expect(screen.getByText("Kontakt")).toBeInTheDocument();
    expect(screen.getByText("Anna Svensson")).toBeInTheDocument();
    expect(screen.getByText("Rekryterare")).toBeInTheDocument();

    const email = screen.getByRole("link", { name: "anna.svensson@acme.se" });
    expect(email).toHaveAttribute("href", "mailto:anna.svensson@acme.se");

    // Phone text is rendered verbatim (spaces kept) and the tel: href carries it
    // exactly — we never re-format the number.
    const phone = screen.getByRole("link", { name: "070-123 45 67" });
    expect(phone).toHaveAttribute("href", "tel:070-123 45 67");
  });

  it("does NOT mark a declared contact as derived", () => {
    render(<RecruiterContactBlock contacts={[declared]} />);
    expect(screen.queryByText("Från annonstexten")).not.toBeInTheDocument();
  });

  it("omits a method row when the field is null (no placeholder dash)", () => {
    render(
      <RecruiterContactBlock
        contacts={[{ ...declared, phone: null }]}
      />,
    );
    expect(
      screen.getByRole("link", { name: "anna.svensson@acme.se" }),
    ).toBeInTheDocument();
    // No phone → no tel link and no "Telefon" label.
    expect(screen.queryByText("Telefon")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "070-123 45 67" }),
    ).not.toBeInTheDocument();
  });

  it("marks a derived contact 'Från annonstexten' and leads with its value (name is null)", () => {
    const derived: AdContactDto = {
      name: null,
      role: null,
      email: "jobb@acme.se",
      phone: null,
      isDerived: true,
    };
    render(<RecruiterContactBlock contacts={[derived]} />);

    expect(screen.getByText("Från annonstexten")).toBeInTheDocument();

    // The value itself leads the entry (there is no name to headline it): the
    // email is the headline link, not a labelled "E-post" row.
    const email = screen.getByRole("link", { name: "jobb@acme.se" });
    expect(email).toHaveAttribute("href", "mailto:jobb@acme.se");
    expect(screen.queryByText("E-post")).not.toBeInTheDocument();
  });

  it("a derived contact leads with the email and labels a secondary phone row", () => {
    const derived: AdContactDto = {
      name: null,
      role: null,
      email: "rekrytering@acme.se",
      phone: "08-555 00 00",
      isDerived: true,
    };
    render(<RecruiterContactBlock contacts={[derived]} />);

    expect(screen.getByText("Från annonstexten")).toBeInTheDocument();
    // Email leads (headline link, unlabelled); phone is the labelled secondary row.
    expect(
      screen.getByRole("link", { name: "rekrytering@acme.se" }),
    ).toHaveAttribute("href", "mailto:rekrytering@acme.se");
    expect(screen.getByText("Telefon")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "08-555 00 00" }),
    ).toHaveAttribute("href", "tel:08-555 00 00");
  });

  it("leads with the phone value when a derived contact has no email", () => {
    const derived: AdContactDto = {
      name: null,
      role: null,
      email: null,
      phone: "0701234567",
      isDerived: true,
    };
    render(<RecruiterContactBlock contacts={[derived]} />);

    expect(screen.getByText("Från annonstexten")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "0701234567" }),
    ).toHaveAttribute("href", "tel:0701234567");
  });

  it("renders every contact in the list (declared + derived together)", () => {
    const derived: AdContactDto = {
      name: null,
      role: null,
      email: "jobb@acme.se",
      phone: null,
      isDerived: true,
    };
    render(<RecruiterContactBlock contacts={[declared, derived]} />);

    expect(screen.getByText("Anna Svensson")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "jobb@acme.se" }),
    ).toBeInTheDocument();
    // Exactly one derived marker — the declared entry is not tagged.
    expect(screen.getAllByText("Från annonstexten")).toHaveLength(1);
  });
});
