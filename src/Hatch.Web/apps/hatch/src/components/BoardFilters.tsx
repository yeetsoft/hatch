import type { ReactNode } from 'react';
import type { Assignee, IssueCard, Project } from '../types';
import { DEFAULT_FILTER, isFiltering, toggleWaiting, typeCounts } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { Facet } from './Facet';
import { facetClass } from '../lib/facet';
import { TypesFacet } from './TypesFacet';
import { MoreFilters, assigneeOptions } from './MoreFilters';
import { hasMultipleProjects } from '../lib/useProjects';

/**
 * What the board is showing: a search box, a project, the types, a switch and
 * an assignee (behind "…" on a desktop), all in one look (see `.hatch-facet`). A control holding its
 * default sits at rest and one holding anything else is tinted, so the bar reads
 * as "what have I changed" and not as three-of-four-lit.
 *
 * Both are the browser's - the board already holds every issue in the house
 * (BoardDto arrives in one request), so filtering is a pass over an array and
 * not a round trip. That matters for more than speed: a filter that refetched
 * would drop the drag in progress and would make typing into the box a
 * conversation with the server.
 *
 * A browser's first visit opens the board on every type (DEFAULT_FILTER); after
 * that it opens on the types last chosen there, and Reset returns to the
 * default. None is a choice too: it leaves the board blank on purpose, and the
 * bar says so.
 */
export function BoardFilters({
  filter,
  onChange,
  assignees,
  projects,
  cards,
  showing,
  total,
  scoped = false,
  collapsed = false,
  leading,
  trailing,
}: {
  filter: CardFilter;
  onChange: (next: CardFilter) => void;
  /** The whole board's cards, for the counts in the Types panel - not the
      visible ones, so choosing a type does not shrink the list it was chosen from. */
  cards: IssueCard[];
  /** The assignees this board actually has cards for - see assigneeFacets. */
  assignees: Assignee[];
  /** The projects to offer - under a scope, the ones it names rather than
      every project; see BoardPage's `offered`. */
  projects: Project[];
  /** How many cards survive the filter, and how many there are - the count is the only feedback a search box gives. */
  showing: number;
  total: number;
  /** A scope is narrowing `projects` already, so the picker's empty choice
      reads "All in scope" rather than "All projects". */
  scoped?: boolean;
  /** Folds everything past the search box behind a "Filters" disclosure - the
      phone row (BoardPage, AC1): the search box and the toggle are what a
      phone user reaches for first, and the rest is one press away rather
      than a row that wraps four or five lines deep. */
  collapsed?: boolean;
  /** Placed right after the search box, on whichever line it lands on - the
      phone row (BoardPage, AC1): a compact New issue beside the search box
      it was passed in next to. */
  leading?: ReactNode;
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
      {hasMultipleProjects(projects) && (
        <Facet
          label="Project"
          lit={filter.project !== ''}
          value={filter.project}
          options={[
            { value: '', text: scoped ? 'All in scope' : 'All projects', short: 'All' },
            ...projects.map((project) => ({
              value: project.key,
              text: `${project.key} — ${project.name}`,
              short: project.key,
            })),
          ]}
          onChange={(project) => onChange({ ...filter, project })}
        />
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
        Needs me
      </button>

      {/* Assignee is the rarely used filter, so on the desktop row it sits behind
          "…" (MoreFilters) to keep the row on one line. The phone row has the
          room inside its Filters disclosure and keeps it inline. */}
      {collapsed ? (
        <Facet
          label="Assignee"
          lit={filter.assignee !== ''}
          value={filter.assignee}
          options={assigneeOptions(assignees)}
          onChange={(assignee) => onChange({ ...filter, assignee })}
        />
      ) : (
        <MoreFilters filter={filter} assignees={assignees} onChange={onChange} />
      )}

      {filtering && (
        <span className="hatch-filter-count">
          {showing} of {total}
        </span>
      )}
      {filtering && (
        <button
          type="button"
          className="hatch-filter-reset"
          aria-label="Reset filters"
          title="Reset filters"
          onClick={() => onChange({ ...DEFAULT_FILTER })}
        >
          <svg className="hatch-filter-reset-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
          </svg>
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
        placeholder="Search…"
        aria-label="Search the board"
        onChange={(e) => onChange({ ...filter, query: e.target.value })}
      />

      {leading}

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
