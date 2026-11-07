import type { Project } from '../types';

/**
 * The project an import would write into: the one picked, if the tab's scope
 * still holds it; else the sole project of a scoped tab; else nothing.
 *
 * A pick left over from a scope that has since narrowed is dropped here rather
 * than sent. A one-project install is not "scoped" - nobody narrowed anything -
 * so it still asks, the way `ImportPage` always has.
 */
export function importTarget(inScope: Project[], picked: number | null, isScoped: boolean): number | null {
  if (picked !== null && inScope.some((p) => p.id === picked)) return picked;

  return isScoped && inScope.length === 1 ? inScope[0].id : null;
}
