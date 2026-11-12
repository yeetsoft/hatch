import { toggleChoice } from '../lib/checklist';

/**
 * The body of a checklist: a row per option with its count and **Only**, and
 * **All** / **None** at the foot. Lifted out of `ChecklistFacet` so the same
 * rows can sit inside another panel (`MoreFilters`) without a second trigger
 * around them.
 */
export function ChecklistPanel({
  groupLabel,
  options,
  chosen,
  onChange,
}: {
  groupLabel: string;
  options: { value: string; text: string; count?: number }[];
  chosen: string[];
  onChange: (next: string[]) => void;
}) {
  const lit = chosen.length < options.length;

  return (
    <div className="hatch-types-panel" role="group" aria-label={groupLabel}>
      {options.map((option) => {
        const on = chosen.includes(option.value);
        return (
          <div key={option.value} className="hatch-types-item">
            <label className="hatch-types-label">
              <input
                type="checkbox"
                checked={on}
                onChange={() => onChange(toggleChoice(chosen, option.value, options.map((o) => o.value)))}
              />
              <span className="hatch-types-name">{option.text}</span>
              {option.count !== undefined && <span className="hatch-types-count">{option.count}</span>}
            </label>
            <button
              type="button"
              className="hatch-filter-clear hatch-types-only"
              aria-label={`Only ${option.text}`}
              onClick={() => onChange([option.value])}
            >
              Only
            </button>
          </div>
        );
      })}
      <div className="hatch-types-footer">
        <button
          type="button"
          className="hatch-filter-clear"
          disabled={!lit}
          onClick={() => onChange(options.map((o) => o.value))}
        >
          All
        </button>
        <button type="button" className="hatch-filter-clear" disabled={chosen.length === 0} onClick={() => onChange([])}>
          None
        </button>
      </div>
    </div>
  );
}
