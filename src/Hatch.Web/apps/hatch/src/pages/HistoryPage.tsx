import { useCallback } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge, Button, Card, EmptyState, PageHeader, Table } from "@hatch/ui";
import { getActivity } from "../api/client";
import { describe } from "../lib/events";
import {
  activityRequest,
  hasNewer,
  historyDescription,
  parsePage,
} from "../lib/history";
import { useLoaded } from "../lib/useLoaded";
import { useScope } from "../lib/useScope";
import type { ActivityPage } from "../types";

/**
 * The event trail read across every project together, newest first, a
 * hundred to a page - the same rows `EventTrail` draws on one issue, here
 * with nothing to open first to see them.
 *
 * No filters, no titles, no counts: the server does not count the pages
 * either, so **Older** is offered only when it says there is one.
 *
 * Under a scoped tab it reads only the scope's projects. Nothing is fetched
 * until the project list is known, so the first request is already the right
 * one - the body is a component of its own so that wait is not inside `load`.
 */
export function HistoryPage() {
  const { ids, isScoped, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <HistoryBody ids={ids} isScoped={isScoped} />;
}

function HistoryBody({ ids, isScoped }: { ids: number[]; isScoped: boolean }) {
  const [params, setParams] = useSearchParams();
  const page = parsePage(params);

  const load = useCallback(
    () => getActivity(activityRequest(page, ids)),
    [page, ids],
  );
  const { data, error } = useLoaded<ActivityPage>(load);

  function write(nextPage: number) {
    const next = new URLSearchParams(params);
    next.set("page", String(nextPage));
    setParams(next, { replace: true });
  }

  return (
    <div className="hatch-page">
      <PageHeader title="History" description={historyDescription(isScoped)} />

      {error && <p className="text-danger">{error}</p>}

      {data?.events.length === 0 && (
        <EmptyState message="Nothing has happened here yet." />
      )}

      {data && data.events.length > 0 && (
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
                  <td className="text-muted">
                    {new Date(event.at).toLocaleString()}
                  </td>
                  <td>
                    <Link to={`/issues/${event.issueKey}`}>
                      {event.issueKey}
                    </Link>
                  </td>
                  <td>
                    <Badge>{event.kind.replaceAll("_", " ")}</Badge>
                  </td>
                  <td>{event.actor}</td>
                  <td className="text-muted">{describe(event)}</td>
                </tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      {data && (
        <div className="hatch-form-actions">
          {hasNewer(page) && (
            <Button onClick={() => write(page - 1)}>Newer</Button>
          )}
          {data.hasMore && (
            <Button onClick={() => write(page + 1)}>Older</Button>
          )}
        </div>
      )}
    </div>
  );
}
