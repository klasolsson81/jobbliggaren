export const INFORMATION_PATHS = [
  "/integritet", "/villkor", "/cookies", "/kontaktperson-i-annons",
  "/tillganglighet", "/hjalpcenter", "/vanliga-fragor", "/matchning",
  "/cv-granskning", "/tips", "/om", "/kontakt", "/for-utvecklare",
] as const;
export type InformationPath = typeof INFORMATION_PATHS[number];
const ROUTES = {
  home: "/", login: "/logga-in", help: "/hjalpcenter", jobs: "/jobb",
  saved: "/sparade", matches: "/matchningar", applications: "/ansokningar",
  cv: "/cv", overview: "/oversikt", searches: "/sokningar", settings: "/mina-sidor",
  guestJobs: "/gast/jobb", guestApplications: "/gast/ansokningar", guestCV: "/gast/cv", guestOverview: "/gast/oversikt",
} as const;
export type ReturnDestination = { kind: keyof typeof ROUTES } | { kind: "job" | "application" | "guestJob" | "guestApplication"; id: string };
const IDENTIFIER = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function isInformationPath(path: string): path is InformationPath {
  return (INFORMATION_PATHS as readonly string[]).includes(path);
}
export function destinationHref(destination: ReturnDestination): string | null {
  if (destination.kind === "guestJob" || destination.kind === "guestApplication") {
    const job = destination.kind === "guestJob";
    if (!(job ? /^gj-[1-9][0-9]{0,2}$/ : /^ga-[1-9][0-9]{0,2}$/).test(destination.id)) return null;
    return `/gast/${job ? "jobb" : "ansokningar"}/${destination.id}`;
  }
  if (destination.kind === "job" || destination.kind === "application") {
    if (!IDENTIFIER.test(destination.id)) return null;
    return `${destination.kind === "job" ? "/jobb" : "/ansokningar"}/${destination.id}`;
  }
  return Object.hasOwn(ROUTES, destination.kind) ? ROUTES[destination.kind] : null;
}
// Queries and fragments stay on the source history entry, never in return parameters.
export function destinationForPath(path: string): ReturnDestination | null {
  for (const [kind, route] of Object.entries(ROUTES)) {
    if (route === path) return { kind: kind as keyof typeof ROUTES };
  }
  const guest = /^\/gast\/(jobb|ansokningar)\/([^/]+)$/.exec(path);
  if (guest?.[2]) {
    const destination: ReturnDestination = { kind: guest[1] === "jobb" ? "guestJob" : "guestApplication", id: guest[2] };
    return destinationHref(destination) === path ? destination : null;
  }
  const match = /^\/(jobb|ansokningar)\/([^/]+)$/.exec(path);
  const id = match?.[2];
  if (!id || !IDENTIFIER.test(id)) return null;
  return { kind: match?.[1] === "jobb" ? "job" : "application", id };
}
export function validInformationHref(href: string): boolean {
  const [path, fragment, extra] = href.split("#");
  return path !== undefined && extra === undefined && isInformationPath(path) &&
    (fragment === undefined || /^[a-z][a-z0-9-]*$/.test(fragment));
}
