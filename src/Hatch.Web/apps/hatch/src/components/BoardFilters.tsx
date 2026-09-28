import { ISSUE_TYPES } from '../types';
import type { Assignee, Project } from '../types';
import { UNASSIGNED, assigneeToken } from '../lib/assignee';
import { DEFAULT_FILTER, isDefault, isFiltering, toggleType, toggleWaiting } from '../lib/filter';
import type { CardFilter } from '../lib/filter';

/**
 * What the board is showing: a toggle per type, and a search box.
 *
 * Both are the browser's - the board already holds every issue in the house
 * (BoardDto arrives in one request), so filtering is a pass over an array and
 * not a round trip. That matters for more than speed: a filter that refetched
 * would drop the drag in progress and would make typing into the box a
 * conversation with the server.
 *
 * The board opens on epics, stories and bugs (DEFAULT_FILTER). The last type
 * drawn cannot be switched off, so the board never goes blank from a chip.
 */
export function BoardFilters({
  filter,
  onChange,
  assignees,
  projects,
  showing,
  total,
}: {
  filter: CardFilter;
  onChange: (next: CardFilter) => void;
  /** The assignees this board actually has cards for - see assigneeFacets. */
  assignees: Assignee[];
  /** The projects on the board - see BoardPage's getProjects read. */
  projects: Project[];
  /** How many cards survive the filter, and how many there are - the count is the only feedback a search box gives. */
  showing: number;
  total: number;
}) {
  const filtering = isFiltering(filter);

  return (
    <div className="hatch-board-filters">
      <input
        type="search"
        className="hatch-board-search"
        value={filter.query}
        placeholder="Search titles, keys, parents…"
        aria-label="Search the board"
        onChange={(e) => onChange({ ...filter, query: e.target.value })}
      />

      {/* Drawn only when there is a choice to make - the same rule, and the
          same option text, as the picker on the Plan page. One project makes
          this a control with a single option, and Hatch ships to operators
          who will have several. */}
      {projects.length > 1 && (
        <select
          className="hatch-project-filter"
          aria-label="Project"
          value={filter.project}
          onChange={(e) => onChange({ ...filter, project: e.target.value })}
        >
          <option value="">All projects</option>
          {projects.map((project) => (
            <option key={project.id} value={project.key}>
              {project.key} — {project.name}
            </option>
          ))}
        </select>
      )}

      <div className="hatch-type-toggles" role="group" aria-label="Issue types">
        {ISSUE_TYPES.map((type) => {
          const on = filter.types.includes(type);
          const last = on && filter.types.length === 1;
          return (
            <button
              key={type}
              type="button"
              disabled={last}
              title={last ? 'Keep at least one type' : undefined}
              className={`hatch-type-toggle${on ? ' on' : ''}`}
              aria-pressed={on}
              onClick={() => onChange(toggleType(filter, type))}
            >
              {type}
            </button>
          );
        })}
      </div>

      {/* Its own switch beside the type toggles rather than a fifth type: a
          card waiting on an answer is not a kind of work, it is work that has
          stopped, and it is the first thing to look for when the board has. */}
      <button
        type="button"
        className={`hatch-type-toggle hatch-waiting-toggle${filter.waiting ? ' on' : ''}`}
        aria-pressed={filter.waiting}
        title="Cards holding a question nobody has answered"
        onClick={() => onChange(toggleWaiting(filter))}
      >
        waiting on me
      </button>

      {/* A native <select> and not IssuePicker, deliberately: that component
          exists because prefix typeahead fails over sixty options that all
          start with the same project key, and neither half of that is true of
          a household's worth of names.

          "Unassigned" is its own row above the identities rather than derived
          from the cards, because it is the one choice that is a fact about
          absence - assigneeFacets can only report who is there. */}
      <select
        className="hatch-assignee-filter"
        aria-label="Assignee"
        value={filter.assignee}
        onChange={(e) => onChange({ ...filter, assignee: e.target.value })}
      >
        <option value="">— anyone —</option>
        <option value={UNASSIGNED}>Unassigned</option>
        {assignees.map((assignee) => (
          <option key={assigneeToken(assignee)} value={assigneeToken(assignee)}>
            {assignee.name}
          </option>
        ))}
      </select>

      {filtering && (
        <span className="hatch-filter-count">
          {showing} of {total}
        </span>
      )}
      {!isDefault(filter) && (
        <button type="button" className="hatch-filter-clear" onClick={() => onChange({ ...DEFAULT_FILTER })}>
          Clear
        </button>
      )}
    </div>
  );
}
