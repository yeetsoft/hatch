/**
 * The one line a newcomer pastes into Claude to be walked from a bare machine
 * to their own board (HA-613): a sentence, then the address of the setup guide
 * this install's repository publishes at /setup/ on its Pages site.
 *
 * The address is never written here (docs/ethos.md). publish.yml asks GitHub
 * where the repository's Pages site is and passes it to the image build, and
 * vite-plugin-hatch-revision.mts stamps it into index.html as a
 * `hatch-setup-guide` meta. A build that was told no address - `docker compose
 * up` from a checkout, a fork with Pages off, `npm run dev` - stamps none, and
 * every place that offers the line draws nothing rather than a line that sets
 * somebody up from the wrong place.
 *
 * The sentence is the one apps/site/content/setup.md shows; a change to
 * one is a change to the other.
 */
export const SETUP_PROMPT_LEAD = 'Set me up with Hatch:';

/** The line for a guide at `guide`, or null when there is no usable address. */
export function setupPrompt(guide: string | null | undefined): string | null {
  const address = guide?.trim();
  if (!address) return null;
  let url: URL;
  try {
    url = new URL(address);
  } catch {
    return null;
  }
  // A guide Claude is told to fetch is a web page; anything else stamped here
  // is a build that went wrong, and a line built on it would send a newcomer
  // somewhere that cannot set them up.
  if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
  return `${SETUP_PROMPT_LEAD} ${address}`;
}

/** The address this page's build was stamped with, or null. No DOM, no address. */
export function stampedSetupGuide(): string | null {
  if (typeof document === 'undefined') return null;
  return document.querySelector<HTMLMetaElement>('meta[name="hatch-setup-guide"]')?.content ?? null;
}
