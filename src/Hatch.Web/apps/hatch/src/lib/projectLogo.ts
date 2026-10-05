/* Pure geometry and URL functions for a project's logo - no DOM, no canvas.
   This app's vitest runs in plain Node (no jsdom), so anything that touches
   window, Image or a canvas lives in a later task and stays out of here. */

/** The four signatures PersonPhoto.TryDetectContentType recognises
    (src/Hatch.Api/Common/PersonPhoto.cs). Used only to build the file input's
    `accept` attribute - the server stays the sole judge of what is actually
    stored, so there is no second, hand-maintained copy of its refusal text
    on the client. */
export const LOGO_ACCEPTED_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];

/** The fixed square, in source-image pixels, every uploaded logo is
    downscaled into before it leaves the browser. Comfortably above the
    mark's largest drawn size (lg, 48px CSS pixels, i.e. 96px at 2x), with
    headroom for reuse elsewhere, while staying small enough after PNG
    compression that the server's 2 MB cap is a formality rather than a wall. */
export const LOGO_BOX = 256;

/** The centred square region to read out of a source image before scaling
    to LOGO_BOX. The later canvas call is exactly
    `ctx.drawImage(bitmap, sx, sy, size, size, 0, 0, LOGO_BOX, LOGO_BOX)`. */
export function logoCrop(sourceWidth: number, sourceHeight: number): { sx: number; sy: number; size: number } {
  const size = Math.min(sourceWidth, sourceHeight);
  return { sx: (sourceWidth - size) / 2, sy: (sourceHeight - size) / 2, size };
}

/** The URL to read a project's logo from, or null when it has none. The query
    string is not for cache headers - the server already sends those - it is
    that an `<img src>` whose string does not change is never re-requested at
    all, by the browser's own image cache. Changing the URL is what makes a
    replaced logo actually repaint. */
export function projectLogoUrl(project: { id: number; logoUpdatedAt: string | null }): string | null {
  if (project.logoUpdatedAt === null) return null;
  return `/api/hatch/projects/${project.id}/logo?v=${Date.parse(project.logoUpdatedAt)}`;
}
