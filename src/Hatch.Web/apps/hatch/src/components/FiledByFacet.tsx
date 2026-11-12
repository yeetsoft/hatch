import { filedBySummary } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { ChecklistFacet } from './ChecklistFacet';

/** The filter `next` makes: every name ticked is `null` - everyone, with one
    spelling - and a subset is itself. `names` is every name on offer, in
    offer order, which `toggleChoice` has already put `next` in. */
export const filedByChange = (filter: CardFilter, next: string[], names: string[]): CardFilter => ({
  ...filter,
  filedBy: next.length === names.length ? null : next,
});

export function FiledByFacet({
  filter,
  filers,
  onChange,
}: {
  filter: CardFilter;
  /** Who filed the cards on the whole board, hidden ones included, each with a count. */
  filers: { name: string; count: number }[];
  onChange: (next: CardFilter) => void;
}) {
  const names = filers.map((f) => f.name);

  return (
    <ChecklistFacet
      label="Filed by"
      groupLabel="Filed by"
      summary={filedBySummary(filter.filedBy, names.length)}
      options={filers.map((f) => ({ value: f.name, text: f.name, count: f.count }))}
      chosen={filter.filedBy ?? names}
      onChange={(next) => onChange(filedByChange(filter, next, names))}
    />
  );
}
