import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Button, Card, EmptyState, PageHeader, Table } from '@hatch/ui';
import { getSearch, getStatuses } from '../api/client';
import { Facet } from '../components/Facet';
import { StatusPill } from '../components/StatusPill';
import { TypeBadge } from '../components/TypeBadge';
import { message } from '../lib/errors';
import { facetClass } from '../lib/facet';
import { createLedger } from '../lib/refresh';
import {
  EMPTY_SEARCH,
  countLine,
  hasMore,
  isSearching,
  matchedWords,
  readSearch,
  scrubbedProjectId,
  searchRequest,
  writeSearch,
} from '../lib/search';
import { useLoaded } from '../lib/useLoaded';
import { useScope } from '../lib/useScope';
import { hasMultipleProjects, useProjects } from '../lib/useProjects';
import { ISSUE_TYPES } from '../types';
import type { SearchHit, Status } from '../types';

/** How long the box waits after the last keystroke before it writes `q`. */
const DEBOUNCE_MS = 300;

/**
 * Every issue, by its words and by when it was made or closed - a search box
 * over a table of hits, with a filter row beside it (Project, Type, Column,
 * Created and Closed ranges). Every field - the box's `q` included - is the
 * URL's and nowhere else, so Back undoes a change and pasting the link
 * reproduces it.
 */
export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const filters = readSearch(params);

  const { data: statuses } = useLoaded<Status[]>(getStatuses);
  const { projects, loaded: projectsLoaded } = useProjects();
  const scope = useScope();
  // Derived from this page's own `projects`, not `scope.projects`: the
  // provider's list can land after this page's, and an empty one would scrub
  // a chosen project before the page's own list has arrived.
  const offered = useMemo(
    () => (scope.isScoped ? projects.filter((p) => scope.keys.includes(p.key)) : projects),
    [projects, scope.isScoped, scope.keys],
  );
  const columns = [...(statuses ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);

  const [draft, setDraft] = useState(filters.q);
  const [lastQ, setLastQ] = useState(filters.q);
  // The URL's q changed from outside (Back, or a link reached from elsewhere)
  // - adjusted during render, the way InlineTitle re-syncs its own draft.
  if (filters.q !== lastQ) {
    setLastQ(filters.q);
    setDraft(filters.q);
  }

  useEffect(() => {
    if (draft === filters.q) return;
    const timer = setTimeout(() => {
      setParams(writeSearch({ ...filters, q: draft }), { replace: true });
    }, DEBOUNCE_MS);
    return () => clearTimeout(timer);
    // filters.q, not filters: a re-render with the same q (loading, statuses
    // arriving) must not restart the timer, or a quick re-render could starve it.
  }, [draft, filters.q, setParams]);

  // A project in the URL that the scope no longer offers reads as none chosen.
  useEffect(() => {
    if (!projectsLoaded) return;
    const scrubbed = scrubbedProjectId(filters.projectId, offered);
    if (scrubbed !== filters.projectId) setParams(writeSearch({ ...filters, projectId: scrubbed }), { replace: true });
  }, [offered, projectsLoaded, filters.projectId]);

  const [hits, setHits] = useState<SearchHit[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ledger] = useState(createLedger);

  // The scope rides along: with no project chosen the request is the scope's,
  // so a changed scope is a changed search.
  const key = `${writeSearch(filters).toString()}#${scope.keys.join(',')}`;

  useEffect(() => {
    const ticket = ledger.begin();
    setHits([]);
    setTotal(0);
    setLoading(true);
    setError(null);

    getSearch(searchRequest(filters, 0, scope.ids)).then(
      (result) => {
        if (!ledger.isCurrent(ticket)) return;
        setHits(result.hits);
        setTotal(result.total);
        setLoading(false);
      },
      (err: unknown) => {
        if (!ledger.isCurrent(ticket)) return;
        setError(message(err));
        setLoading(false);
      },
    );

    return () => ledger.invalidate();
    // key, not filters: it is the same string for an unchanged search, so a
    // re-render that leaves the search alone does not reload it.
  }, [key]);

  function showMore() {
    const ticket = ledger.begin();
    setLoadingMore(true);
    setError(null);

    getSearch(searchRequest(filters, hits.length, scope.ids)).then(
      (result) => {
        if (!ledger.isCurrent(ticket)) return;
        setHits((prev) => [...prev, ...result.hits]);
        setTotal(result.total);
        setLoadingMore(false);
      },
      (err: unknown) => {
        if (!ledger.isCurrent(ticket)) return;
        setError(message(err));
        setLoadingMore(false);
      },
    );
  }

  return (
    <div className="hatch-page">
      <PageHeader title="Search" description="Every issue, by its words and by when it was made or closed." />

      <div className="hatch-board-filters hatch-search-filters">
        {/* type="search" on purpose; base.css says why. */}
        <input
          type="search"
          className={`hatch-board-search hatch-search-box${draft.trim() !== '' ? ' hatch-board-search--lit' : ''}`}
          aria-label="Search issues"
          placeholder="Search titles, descriptions, comments…"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />

        {hasMultipleProjects(offered) && (
          <Facet
            label="Project"
            lit={filters.projectId !== ''}
            value={filters.projectId}
            options={[
              { value: '', text: scope.isScoped ? 'All in scope' : 'All projects' },
              ...offered.map((project) => ({ value: String(project.id), text: `${project.key} — ${project.name}` })),
            ]}
            onChange={(projectId) => setParams(writeSearch({ ...filters, projectId }))}
          />
        )}

        <Facet
          label="Type"
          lit={filters.type !== ''}
          value={filters.type}
          options={[{ value: '', text: 'Any type' }, ...ISSUE_TYPES.map((type) => ({ value: type, text: type }))]}
          onChange={(type) => setParams(writeSearch({ ...filters, type }))}
        />

        <Facet
          label="Column"
          lit={filters.statusId !== ''}
          value={filters.statusId}
          options={[
            { value: '', text: 'Any column' },
            ...columns.map((status) => ({ value: String(status.id), text: status.name })),
          ]}
          onChange={(statusId) => setParams(writeSearch({ ...filters, statusId }))}
        />

        <label className={`${facetClass(filters.createdFrom !== '' || filters.createdTo !== '')} hatch-search-range`}>
          <span className="hatch-facet__label">Created</span>
          <input
            type="date"
            aria-label="Created from"
            value={filters.createdFrom}
            onChange={(e) => setParams(writeSearch({ ...filters, createdFrom: e.target.value }))}
          />
          <span aria-hidden="true">–</span>
          <input
            type="date"
            aria-label="Created to"
            value={filters.createdTo}
            onChange={(e) => setParams(writeSearch({ ...filters, createdTo: e.target.value }))}
          />
        </label>

        <label className={`${facetClass(filters.closedFrom !== '' || filters.closedTo !== '')} hatch-search-range`}>
          <span className="hatch-facet__label">Closed</span>
          <input
            type="date"
            aria-label="Closed from"
            value={filters.closedFrom}
            onChange={(e) => setParams(writeSearch({ ...filters, closedFrom: e.target.value }))}
          />
          <span aria-hidden="true">–</span>
          <input
            type="date"
            aria-label="Closed to"
            value={filters.closedTo}
            onChange={(e) => setParams(writeSearch({ ...filters, closedTo: e.target.value }))}
          />
        </label>

        {isSearching(filters) && (
          <button type="button" className="hatch-filter-clear" onClick={() => setParams(writeSearch(EMPTY_SEARCH))}>
            Reset
          </button>
        )}
      </div>

      {loading && <p className="text-muted">Searching…</p>}

      {!loading && error && hits.length === 0 && <p className="text-danger">{error}</p>}

      {!loading && !error && hits.length === 0 && <EmptyState message="Nothing matches." />}

      {!loading && hits.length > 0 && (
        <>
          {error && <p className="text-danger">{error}</p>}

          <p className="text-muted" aria-live="polite">
            {countLine(hits.length, total)}
          </p>

          <Card flush>
            <Table>
              <thead>
                <tr>
                  <th>Issue</th>
                  <th>Type</th>
                  <th>Title</th>
                  <th>Column</th>
                  <th>Project</th>
                  <th>Created</th>
                  <th>Closed</th>
                  <th>Matched</th>
                </tr>
              </thead>
              <tbody>
                {hits.map((hit) => {
                  const status = statuses?.find((s) => s.id === hit.card.statusId);
                  const words = matchedWords(hit.matched);

                  return (
                    <tr key={hit.card.key}>
                      <td>
                        <Link to={`/issues/${hit.card.key}`}>{hit.card.key}</Link>
                      </td>
                      <td>
                        <TypeBadge type={hit.card.type} />
                      </td>
                      <td>{hit.card.title}</td>
                      <td>
                        {status ? <StatusPill status={status} /> : <span>#{hit.card.statusId}</span>}
                      </td>
                      <td>{hit.card.projectKey}</td>
                      <td className="text-muted">{new Date(hit.createdAt).toLocaleDateString()}</td>
                      <td className="text-muted">{hit.closedAt ? new Date(hit.closedAt).toLocaleDateString() : ''}</td>
                      <td>{words && <span className="text-muted">{words}</span>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </Table>
          </Card>

          {hasMore(hits.length, total) && (
            <div className="hatch-form-actions">
              <Button onClick={showMore} loading={loadingMore}>
                Show more
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
