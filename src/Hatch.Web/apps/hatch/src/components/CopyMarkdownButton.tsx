import { useEffect, useState } from 'react';
import { facetClass } from '../lib/facet';

/** How long "Copied" stays before the button goes back to its label. */
const COPIED_FOR_MS = 2000;

const REFUSED = 'Copy was refused by the browser';

/**
 * One press to put a Markdown string on the clipboard.
 *
 * Drawn only where the clipboard API is there - an insecure origin or an old
 * browser has no way to take the paste, and a button that silently does nothing
 * is worse than none, as Command argues. A write the browser refuses after the
 * press says so, and says so until the next press.
 */
export function CopyMarkdownButton({ markdown }: { markdown: string }) {
  const [said, setSaid] = useState<string | null>(null);
  const [known, setKnown] = useState(markdown);

  // What was said is about the string that was copied, so it goes when that
  // string changes underneath - a refetch must not leave "Copied" over a
  // different paste. Adjusted during render, as Command does.
  if (markdown !== known) {
    setKnown(markdown);
    setSaid(null);
  }

  // "Copied" is the only thing that expires; a refusal stays until the next press.
  useEffect(() => {
    if (said !== 'Copied') return;

    const timer = setTimeout(() => setSaid(null), COPIED_FOR_MS);

    return () => clearTimeout(timer);
  }, [said]);

  const canCopy = typeof navigator !== 'undefined' && typeof navigator.clipboard?.writeText === 'function';

  if (!canCopy) return null;

  async function copy() {
    try {
      await navigator.clipboard.writeText(markdown);
      setSaid('Copied');
    } catch {
      setSaid(REFUSED);
    }
  }

  return (
    <button type="button" className={facetClass(false)} aria-live="polite" onClick={() => void copy()}>
      {said ?? 'Copy as Markdown'}
    </button>
  );
}
