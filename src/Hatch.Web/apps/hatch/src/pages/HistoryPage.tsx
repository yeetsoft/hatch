import { useCallback } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Badge, Button, Card, EmptyState, PageHeader, Table } from '@hatch/ui';
import { getActivity } from '../api/client';
import { describe } from '../lib/events';
import { hasNewer, offsetFor, parsePage } from '../lib/history';
import { useLoaded } from '../lib/useLoaded';
import type { ActivityPage } from '../types';

/**
 * The event trail read across every project together, newest first, a
 * hundred to a page - the same rows `EventTrail` draws on one issue, here
 * with nothing to open first to see them.
 *
 * No filters, no titles, no counts: the server does not count the pages
 * either, so **Older** is offered only when it says there is one.
 */
export function HistoryPage() {
  const [params, setParams] = useSearchParams();
  const page = parsePage(params);

  const load = useCallback(() => getActivity({ offset: offsetFor(page) }), [page]);
  const { data, error } = useLoaded<ActivityPage>(load);

  function write(nextPage: number) {
    const next = new URLSearchParams(params);
    next.set('page', String(nextPage));
    setParams(next, { replace: true });
  }

  return (
    <div className="hatch-page">
      <PageHeader title="History" description="What happened last, across every project, newest first." />

      {error && <p className="text-danger">{error}</p>}

      {data?.events.length === 0 && <EmptyState message="Nothing has happened here yet." />}

      {data && data.events.length > 0 && (
        <>
          <Card flush>
            <Table>
              <thead>
                <tr>
                  <th>When</th>
                  <th>Issue</th>
                  <th>What</th>
                  <th>Who</th>
                  <th>Changed</th>
                </tr>
              </thead>
              <tbody>
                {data.events.map((event) => (
                  <tr key={event.id}>
                    <td className="text-muted">{new Date(event.at).toLocaleString()}</td>
                    <td>
                      <Link to={`/issues/${event.issueKey}`}>{event.issueKey}</Link>
                    </td>
                    <td>
                      <Badge>{event.kind.replaceAll('_', ' ')}</Badge>
                    </td>
                    <td>{event.actor}</td>
                    <td className="text-muted">{describe(event)}</td>
                  </tr>
                ))}
              </tbody>
            </Table>
          </Card>

          <div className="hatch-form-actions">
            {hasNewer(page) && <Button onClick={() => write(page - 1)}>Newer</Button>}
            {data.hasMore && <Button onClick={() => write(page + 1)}>Older</Button>}
          </div>
        </>
      )}
    </div>
  );
}
