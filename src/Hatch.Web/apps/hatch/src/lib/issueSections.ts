/* What a section on the issue page remembers about being open or closed, and
   what it resolves to - a storage pair plus a pure function, the same shape
   phoneBoard.ts's view pair and defaultProject.ts's remembered-project pair
   already have. The page itself decides when to call these; nothing here
   touches a component. */

/** Where every section's choice is persisted - namespaced, the way
    hatch.board.view and hatch.project.last are. One entry per section id,
    not one key per section: seven sections are one read and one write. */
export const ISSUE_SECTIONS_STORAGE_KEY = 'hatch.issue.sections';

export type SectionOpenState = 'open' | 'closed';

/** The stored choices, or none. try/catch around the read and the parse
    alike - localStorage throws in a partitioned or locked-down context, and
    a corrupt value is the same "can't trust the store" case as a throwing
    one, so both fall back to the same empty record and the page still
    renders. */
export function readSectionStates(): Record<string, SectionOpenState> {
  try {
    const raw = window.localStorage.getItem(ISSUE_SECTIONS_STORAGE_KEY);
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

/** Persists one section's choice among the others. A store that cannot be
    written is not worth surfacing - the choice holds until reload, as the
    theme's does. */
export function writeSectionState(id: string, open: boolean): void {
  try {
    const states = readSectionStates();
    states[id] = open ? 'open' : 'closed';
    window.localStorage.setItem(ISSUE_SECTIONS_STORAGE_KEY, JSON.stringify(states));
  } catch {
    /* See above. */
  }
}

/** Whether a section opens: the stored choice wins; absent one, the default
    the call site asked for. Pure, so this needs no browser to test. */
export function sectionOpens(
  stored: Record<string, SectionOpenState>,
  id: string,
  defaultOpen: boolean,
): boolean {
  const state = stored[id];
  if (state === 'open') return true;
  if (state === 'closed') return false;
  return defaultOpen;
}
