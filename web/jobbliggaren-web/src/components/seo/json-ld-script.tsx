/**
 * Structured data as one `application/ld+json` script. Every `<` is written as
 * `<`, so no string inside the data can close the script element; the JSON it
 * parses to is unchanged.
 */
export function JsonLdScript({ data }: { data: Record<string, unknown> }) {
  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: JSON.stringify(data).replace(/</g, "\\u003c") }}
    />
  );
}
