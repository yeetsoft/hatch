import { Card, Table } from '@hatch/ui';
import { recentSessions } from '../lib/runners';
import { compactTokensOrDash, moneyPhrase } from '../lib/workLog';
import type { Runner } from '../types';

/** The sessions no longer live: newest first, at most 20 - the server's own
    retention. Omitted entirely when there are none, by the
    renders-nothing discipline. */
export function RecentSessions({ runner, now }: { runner: Runner; now: Date }) {
  const sessions = recentSessions(runner, now);
  if (sessions.length === 0) return null;

  return (
    <Card>
      <h2 className="hatch-section-title">Recent sessions</h2>
      <Table scroll>
        <thead>
          <tr>
            <th scope="col">Where</th>
            <th scope="col">Kind</th>
            <th scope="col">Started</th>
            <th scope="col">Ended</th>
            <th scope="col">Runs</th>
            <th scope="col">Spent</th>
            <th scope="col">Tokens</th>
          </tr>
        </thead>
        <tbody>
          {sessions.map((session) => (
            <tr key={session.id}>
              <td>{session.where ?? '-'}</td>
              <td>{session.kind}</td>
              <td>{new Date(session.startedAt).toLocaleString()}</td>
              <td>{session.endedAt ? new Date(session.endedAt).toLocaleString() : '—'}</td>
              <td>{(session.runs ?? 0).toLocaleString()}</td>
              <td>{moneyPhrase(session.spent ?? 0)}</td>
              <td>{compactTokensOrDash(session.tokensUsed ?? null)}</td>
            </tr>
          ))}
        </tbody>
      </Table>
    </Card>
  );
}
