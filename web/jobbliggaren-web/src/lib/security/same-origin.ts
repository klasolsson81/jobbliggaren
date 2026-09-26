import "server-only";

const AUTHORITY = /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\.?|\[[0-9a-f:.]+\])(?::[0-9]{1,5})?$/i;

function parseOrigin(value: string): URL | null {
  if (/\s/.test(value)) return null;
  const match = /^(https?):\/\/(.+)$/.exec(value);
  const authority = match?.[2];
  if (!authority || !AUTHORITY.test(authority)) return null;

  try {
    const parsed = new URL(value);
    const hostname = authority.startsWith("[")
      ? authority.slice(0, authority.indexOf("]") + 1)
      : authority.split(":")[0];
    // Refuse URL parser rewrites such as 127.1 or hexadecimal IPv4 addresses.
    return parsed.hostname === hostname?.toLowerCase() ? parsed : null;
  } catch {
    return null;
  }
}

/** The reverse proxy preserves Host; forwarded headers are not origin authority. */
export function isSameOriginRequest(request: Request): boolean {
  const origin = request.headers.get("origin");
  const host = request.headers.get("host");
  if (!origin || !host || host.toLowerCase() === "null" || !AUTHORITY.test(host)) return false;

  let protocol: string;
  if (process.env.NODE_ENV === "production") {
    // TLS terminates before Next.js, so its internal request URL can be HTTP.
    protocol = "https:";
  } else {
    try {
      protocol = new URL(request.url).protocol;
    } catch {
      return false;
    }
    if (protocol !== "http:" && protocol !== "https:") return false;
  }

  const source = parseOrigin(origin);
  const destination = parseOrigin(`${protocol}//${host}`);
  return source !== null && destination !== null && source.origin === destination.origin;
}
