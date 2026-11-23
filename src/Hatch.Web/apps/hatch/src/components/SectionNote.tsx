import type { ReactNode } from 'react';

/** What a section has to say when it has nothing else: that it is still being
    read, that the read failed, or that there is nothing in it. */
export type SectionNoteKind = 'empty' | 'loading' | 'failed';

/**
 * The one sentence a section on an ambition's page says in place of its list:
 * loading, failed or empty. One element, one size and one colour per kind, so
 * the six sections read alike. The words are the caller's.
 */
export function SectionNote({ kind, children }: { kind: SectionNoteKind; children: ReactNode }) {
  return (
    <p className={`hatch-ambition-note ${kind === 'failed' ? 'text-danger' : 'text-muted'}`}>{children}</p>
  );
}
