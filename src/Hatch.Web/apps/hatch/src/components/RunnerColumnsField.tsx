import { Button, Text } from '@hatch/ui';
import { statusVars } from '../lib/color';
import {
  allColumns,
  COLUMN_MODES,
  columnLit,
  columnsHint,
  noColumns,
  onlyColumn,
  toggleColumn,
  workableColumns,
} from '../lib/runnerColumns';
import type { RunnerStatusMode, Status } from '../types';

/**
 * The settings card's Columns section: which columns the runner works.
 *
 * A mode and, under `except` and `only`, a list of the columns the board draws,
 * each a pill in its own colour. Lit is a column the runner works and dim is one
 * it does not, whatever the mode - a tick means "skip" under `except` and "work"
 * under `only`, and the pill says which, so nobody has to remember. `name` keeps
 * this card's radios apart from another runner's.
 */
export function RunnerColumnsField({
  name,
  statuses,
  mode,
  ids,
  onChange,
}: {
  name: string;
  statuses: Status[];
  mode: RunnerStatusMode;
  ids: number[];
  onChange: (mode: RunnerStatusMode, ids: number[]) => void;
}) {
  const columns = workableColumns(statuses);

  return (
    <fieldset className="hatch-runner-columns">
      <legend>Columns</legend>
      <div className="hatch-runner-columns-modes">
        {COLUMN_MODES.map((choice) => (
          <label key={choice.mode}>
            <input
              type="radio"
              name={`columns:${name}`}
              checked={mode === choice.mode}
              onChange={() => onChange(choice.mode, ids)}
            />
            {choice.label}
          </label>
        ))}
      </div>

      {mode !== 'all' && (
        <>
          <div className="hatch-runner-columns-bulk">
            <Button onClick={() => onChange(mode, allColumns(columns))}>All</Button>
            <Button onClick={() => onChange(mode, noColumns())}>None</Button>
          </div>
          <ul className="hatch-runner-columns-list">
            {columns.map((column) => {
              const ticked = ids.includes(column.id);
              const lit = columnLit(mode, ticked);
              return (
                <li
                  key={column.id}
                  className={lit ? 'hatch-runner-column' : 'hatch-runner-column hatch-runner-column--dim'}
                >
                  <label>
                    <input
                      type="checkbox"
                      checked={ticked}
                      onChange={() => onChange(mode, toggleColumn(ids, column.id))}
                    />
                    <span className="hatch-status-pill hatch-status-pill-sm" style={statusVars(column.color)}>
                      {column.name}
                    </span>
                  </label>
                  <button
                    type="button"
                    className="hatch-runner-column-only"
                    aria-label={`Only ${column.name}`}
                    onClick={() => onChange(mode, onlyColumn(column.id))}
                  >
                    only
                  </button>
                </li>
              );
            })}
          </ul>
          <Text tone="muted">{columnsHint(mode)}</Text>
        </>
      )}
    </fieldset>
  );
}
