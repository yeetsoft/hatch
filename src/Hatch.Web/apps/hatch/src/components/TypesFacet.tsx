import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';
import { typesSummary } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { ChecklistFacet } from './ChecklistFacet';

export function TypesFacet({
  filter,
  counts,
  onChange,
}: {
  filter: CardFilter;
  /** Cards of each type on the whole board, hidden ones included. */
  counts: Record<IssueType, number>;
  onChange: (next: CardFilter) => void;
}) {
  return (
    <ChecklistFacet
      label="Types"
      groupLabel="Issue types"
      summary={typesSummary(filter.types)}
      options={ISSUE_TYPES.map((type) => ({ value: type, text: type, count: counts[type] }))}
      chosen={filter.types}
      onChange={(next) => onChange({ ...filter, types: ISSUE_TYPES.filter((t) => next.includes(t)) })}
      lockTitle="Keep at least one type"
      allowNone
    />
  );
}
