import type { ReactNode } from 'react';
import type { Assignee, IssueCard, Project } from '../types';
import { UNASSIGNED, assigneeToken } from '../lib/assignee';
import { DEFAULT_FILTER, isFiltering, toggleWaiting, typeCounts } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { Facet } from './Facet';
import { facetClass } from '../lib/facet';
import { TypesFacet } from './TypesFacet';

/**
 * What the board is showing: a search box, a project, the types, a switch and
 * an assignee, all in one look (see `.hatch-facet`). A control holding its
 * default sits at rest and one holding anything else is tinted, so the bar reads
 * as "what have I changed" and not as three-of-four-lit.
 *
 * Both are the browser's - the board already holds every issue in the house
 * (BoardDto arrives in one request), so filtering is a pass over an array and
 * not a round trip. That matters for more than speed: a filter that refetched
 * would drop the drag in progress and would make typing into the box a
 * conversation with the server.
 *
 * The board opens on every type (DEFAULT_FILTER). The last type drawn cannot
 * be switched off, so the board never goes blank from a checkbox.
 */
export function BoardFilters({
  filter,
  onChange,
  assignees,
  projects,
  cards,
  showing,
  total,
  collapsed = false,
  trailing,
}: {
  filter: CardFilter;
  onChange: (next: CardFilter) => void;
  /** The whole board's cards, for the counts in the Types panel - not the
      visible ones, so choosing a type does not shrink the list it was chosen from. */
  cards: IssueCard[];
  /** The assignees this board actually has cards for - see assigneeFacets. */
  assignees: Assignee[];
  /** The projects on the board - see BoardPage's getProjects read. */
  projects: Project[];
  /** How many cards survive the filter, and how many there are - the count is the only feedback a search box gives. */
  showing: number;
  total: number;
  /** Folds everything past the search box behind a "Filters" disclosure - the
      phone row (BoardPage, AC1): the search box and the toggle are what a
      phone user reaches for first, and the rest is one press away rather
      than a row that wraps four or five lines deep. */
  collapsed?: boolean;
  /** Placed at the right end of the row, on whichever line it wraps to; the row does not learn what it is. */
  trailing?: ReactNode;
}) {
  const filtering = isFiltering(filter);

  const rest = (
    <>
      {/* Drawn only when there is a choice to make - the same rule, and the
          same option text, as the picker on the Plan page. One project makes
          this a control with a single option, and Hatch ships to operators
          who will have several. */}
      {projects.length > 1 && (
        <Facet label="Project" lit={filter.project !== ''}>
          <select value={filter.project} onChange={(e) => onChange({ ...filter, project: e.target.value })}>
            <option value="">All projects</option>
            {projects.map((project) => (
              <option key={project.id} value={project.key}>
                {project.key} — {project.name}
              </option>
            ))}
          </select>
        </Facet>
      )}

      <TypesFacet filter={filter} counts={typeCounts(cards)} onChange={onChange} />

      {/* Its own switch beside the types rather than a fifth type: a
          card waiting on an answer is not a kind of work, it is work that has
          stopped, and it is the first thing to look for when the board has. */}
      <button
        type="button"
        className={facetClass(filter.waiting)}
        aria-pressed={filter.waiting}
        title="Cards holding a question nobody has answered"
        onClick={() => onChange(toggleWaiting(filter))}
      >
        Waiting on me
      </button>

      {/* A native <select> and not IssuePicker, deliberately: that component
          exists because prefix typeahead fails over sixty options that all
          start with the same project key, and neither half of that is true of
          a household's worth of names.

          "Unassigned" is its own row above the identities rather than derived
          from the cards, because it is the one choice that is a fact about
          absence - assigneeFacets can only report who is there. */}
      <Facet label="Assignee" lit={filter.assignee !== ''}>
        <select value={filter.assignee} onChange={(e) => onChange({ ...filter, assignee: e.target.value })}>
          <option value="">— anyone —</option>
          <option value={UNASSIGNED}>Unassigned</option>
          {assignees.map((assignee) => (
            <option key={assigneeToken(assignee)} value={assigneeToken(assignee)}>
              {assignee.name}
            </option>
          ))}
        </select>
      </Facet>

      {filtering && (
        <span className="hatch-filter-count">
          {showing} of {total}
        </span>
      )}
      {filtering && (
        <button type="button" className="hatch-filter-clear" onClick={() => onChange({ ...DEFAULT_FILTER })}>
          Reset
        </button>
      )}
    </>
  );

  return (
    <div className="hatch-board-filters">
      {/* type="search" on purpose; base.css says why. */}
      <input
        type="search"
        className={`hatch-board-search${filter.query.trim() !== '' ? ' hatch-board-search--lit' : ''}`}
        value={filter.query}
        placeholder="Search titles, keys, parents…"
        aria-label="Search the board"
        onChange={(e) => onChange({ ...filter, query: e.target.value })}
      />

      {collapsed ? (
        <details className="hatch-board-filters-more">
          <summary className="hatch-section-title">Filters</summary>
          {rest}
        </details>
      ) : (
        rest
      )}

      {trailing && <div className="hatch-filter-trailing">{trailing}</div>}
    </div>
  );
}
