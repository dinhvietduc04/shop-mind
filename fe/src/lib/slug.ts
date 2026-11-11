/** URL-safe slug from a display name, same rules as the mock db and the real
 *  backend's slug generator. */
export const slugify = (s: string) =>
  s
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
