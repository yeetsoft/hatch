/** Adds `value` to `chosen`, or removes it if `chosen` holds it, and hands back
    the result as the members of `order` that are in it, in `order`'s order - so
    a list of checkboxes never reorders under the hand ticking it. A value or an
    entry that is not in `order` is dropped. Unticking the last one returns `[]`:
    the guard against emptying a choice belongs to the caller. */
export function toggleChoice<T extends string>(chosen: readonly T[], value: T, order: readonly T[]): T[] {
  const next = new Set(chosen.includes(value) ? chosen.filter((c) => c !== value) : [...chosen, value]);
  return order.filter((o) => next.has(o));
}
