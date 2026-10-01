/** Where the keyboard lands when the priority band opens on the issue's own
    level: one entry forward in the band's fixed order, wrapping to the first
    entry past the end - since the own-level button is disabled the same way
    `columns.ts`'s current column is, and there is always a next entry to land
    on rather than a last one to back away from.

    Pure and generic rather than importing `PriorityControl`'s own level list,
    so it is unit-testable without a render the way `landingFocus` is for
    columns - see `lib/columns.ts`. */
export function priorityLandingFocus<T extends { name: string }>(
  levels: readonly T[],
  ownName: string,
): T['name'] | null {
  const at = levels.findIndex((level) => level.name === ownName);
  if (levels.length === 0) return null;
  if (at < 0) return levels[0].name;
  return levels[(at + 1) % levels.length].name;
}
