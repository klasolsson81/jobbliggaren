import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import sv from "../../messages/sv";
import en from "../../messages/en";
import StatistikLoading from "./(app)/statistik/loading";
import AktivitetsrapportLoading from "./(app)/aktivitetsrapport/loading";
import CvImportLoading from "./(app)/cv/importera/loading";
import CriterionLoading from "./(app)/foretag/branschbevakningar/[id]/loading";
import CriterionAdsLoading from "./(app)/foretag/branschbevakningar/[id]/annonser/loading";

const STATIC_CASES = [
  { route: "/statistik", Loading: StatistikLoading, copy: (catalogue: typeof sv) => catalogue.statistik },
  { route: "/aktivitetsrapport", Loading: AktivitetsrapportLoading, copy: (catalogue: typeof sv) => catalogue.aktivitetsrapport },
  { route: "/cv/importera", Loading: CvImportLoading, copy: (catalogue: typeof sv) => catalogue.pages.cv.import },
];
const DYNAMIC_CASES = [
  { route: "/foretag/branschbevakningar/[id]", Loading: CriterionLoading, copy: (catalogue: typeof sv) => catalogue.pages.foretag.criteria.browse.lede },
  { route: "/foretag/branschbevakningar/[id]/annonser", Loading: CriterionAdsLoading, copy: (catalogue: typeof sv) => catalogue.pages.foretag.criteria.ads.lede },
];

describe.each(["sv", "en"] as const)("route-owned loading bands (%s, #1917)", (locale) => {
  const catalogue = locale === "sv" ? sv : en;

  it.each(STATIC_CASES)("$route renders its real title and exactly one matching static line", ({ Loading, copy }) => {
    const { container } = render(
      <NextIntlClientProvider locale={locale} messages={catalogue} timeZone="Europe/Stockholm">
        <Loading />
      </NextIntlClientProvider>,
    );
    const band = container.querySelector(".jp-pagehero__main");
    expect(band?.querySelector("h1")).toHaveTextContent(copy(catalogue).title);
    expect(band?.querySelectorAll("p.jp-pagehero__lede")).toHaveLength(1);
    expect(band?.querySelector("p.jp-pagehero__lede")).toHaveTextContent(copy(catalogue).lede);
    expect(band?.querySelector(".jp-skeleton")).toBeNull();
  });

  it.each(DYNAMIC_CASES)("$route keeps a title bar and its own static line", ({ Loading, copy }) => {
    const { container } = render(
      <NextIntlClientProvider locale={locale} messages={catalogue} timeZone="Europe/Stockholm">
        <Loading />
      </NextIntlClientProvider>,
    );
    const band = container.querySelector(".jp-pagehero__main");
    expect(band?.querySelector("h1")).toBeNull();
    expect(band?.querySelectorAll("p.jp-pagehero__lede")).toHaveLength(1);
    expect(band?.querySelector("p.jp-pagehero__lede")).toHaveTextContent(copy(catalogue));
    expect(band?.querySelectorAll(".jp-skeleton")).toHaveLength(1);
    expect(band).not.toHaveTextContent(catalogue.pages.foretag.criteria.heading);
  });
});
