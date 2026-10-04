/* Which project the New issue dialog defaults to, and what it remembers - a
   storage pair plus a pure function, the same shape as phoneBoard.ts's own
   view pair. The dialog itself decides when to call these; nothing here
   touches the dialog. */

import type { Project } from '../types';

/** Where the last project a filing went into is persisted - namespaced, the
    way phoneBoard's BOARD_VIEW_STORAGE_KEY is. */
export const REMEMBERED_PROJECT_STORAGE_KEY = 'hatch.project.last';

/** The remembered project's key, or null. try/catch for the same reason
    themeStore.readChoice has one: localStorage throws in a partitioned or
    locked-down context, and the dialog still has to open. */
export function readRememberedProject(): string | null {
  try {
    return window.localStorage.getItem(REMEMBERED_PROJECT_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Persists the choice. A store that cannot be written is not worth
    surfacing: the dialog simply stops remembering for the rest of the tab,
    as phoneBoard's writeBoardView already treats its own store. */
export function writeRememberedProject(key: string): void {
  try {
    window.localStorage.setItem(REMEMBERED_PROJECT_STORAGE_KEY, key);
  } catch {
    /* See above. */
  }
}

/** Which project the dialog opens on. The filter wins because it is what the
    operator is looking at right now; the remembered project is next because
    it is the best guess absent a filter; the first project is the fallback
    that guarantees an answer. Either key may name no project in the list -
    stale, or never filtered - and a name that matches nothing simply falls
    through to the next rule rather than throwing. */
export function resolveDefaultProject(
  projects: Project[],
  filterKey: string,
  rememberedKey: string | null,
): number | null {
  const byKey = (key: string | null) => projects.find((project) => project.key === key) ?? null;
  const resolved = byKey(filterKey) ?? byKey(rememberedKey) ?? projects[0] ?? null;
  return resolved ? resolved.id : null;
}
