"use client";
import { useTranslations } from "next-intl";
import { ChevronLeft } from "lucide-react";
import { destinationHref } from "./destinations";
import { useInformationNavigation } from "./InformationReturnProvider";
// Live browser proof determines the named return; server rendering uses the home fallback.
export function InformationReturnLink() {
  const t = useTranslations("information");
  const navigation = useInformationNavigation();
  const destination = navigation?.destination();
  return <a href={destination ? destinationHref(destination) ?? "/" : "/"} className="jp-backlink" data-information-link="" data-information-return="" onClick={event => {
    if (!event.defaultPrevented && event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey && navigation?.back()) event.preventDefault();
  }}><ChevronLeft size={16} aria-hidden="true" /><span>{destination ? t(`return.${destination.kind}`) : t("fallback")}</span></a>;
}
