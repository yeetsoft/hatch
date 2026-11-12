import { useEffect, useRef, useState } from 'react';

/** How long "Seed copied" stays up. */
const COPIED_MS = 2000;

/**
 * Copy-to-clipboard with a message for a `role="status"` region: what was copied, or why it was not, shown for a
 * couple of seconds and then cleared. <ThemeSwitch> and the palette configurator copy a seed the same way and must
 * say the same things, so the hook is written once.
 */
export function useCopyAnnounce(): { message: string; copy: (text: string, done: string, failMessage: string) => void } {
  const [message, setMessage] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  const announce = (next: string) => {
    setMessage(next);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setMessage(''), COPIED_MS);
  };

  const copy = (text: string, done: string, failMessage: string) => {
    /* Plain http has no clipboard, and a denied permission rejects. */
    const failed = () => announce(failMessage);
    if (!navigator.clipboard) return failed();
    navigator.clipboard.writeText(text).then(() => announce(done), failed);
  };

  return { message, copy };
}
