import { Text } from '@hatch/ui';
import { drivesLabel, runnerBuildLabel, sessionRepositoriesLabel } from '../lib/runners';
import type { Runner, RunnerSession } from '../types';

/** One session's machine, unfolded: the design's widened `RunnerDetails` -
    named apart so it does not clash with the old export while both exist.
    A closed `<details>` so a card full of live sessions does not open with
    every machine's facts already spread across the page; each row is
    omitted for an older CLI that never reported the fact it is about. */
export function MachineDetails({ session, runner, now }: { session: RunnerSession; runner: Runner; now: Date }) {
  const build = runnerBuildLabel(session, now);
  const repositories = sessionRepositoriesLabel(session);

  return (
    <details>
      <summary>Machine</summary>
      {session.host != null && <Text tone="muted">Host {session.host}</Text>}
      {session.os != null && (
        <Text tone="muted">
          OS {session.os}
          {session.arch != null ? ` (${session.arch})` : ''}
        </Text>
      )}
      {session.agent != null && (
        <Text tone="muted">
          Agent {session.agent}
          {session.agentVersion != null ? ` ${session.agentVersion}` : ''}
        </Text>
      )}
      {session.image != null && <Text tone="muted">Image {session.image}</Text>}
      {build && <Text tone="muted">CLI build {build}</Text>}
      {session.username != null && <Text tone="muted">Username {session.username}</Text>}
      {session.ip != null && (
        <Text tone="muted" title="as the server saw it">
          IP {session.ip}
        </Text>
      )}
      {session.container != null && <Text tone="muted">Container {session.container ? 'Yes' : 'No'}</Text>}
      {session.processStartedAt != null && (
        <Text tone="muted">Started {new Date(session.processStartedAt).toLocaleString()}</Text>
      )}
      {session.projects != null && session.projects.length > 0 && (
        <Text tone="muted">Projects {session.projects.join(', ')}</Text>
      )}
      {session.refusesDangerous != null && (
        <Text tone="muted">Dangerous {session.refusesDangerous === false ? 'Yes' : 'No'}</Text>
      )}
      {repositories && <Text tone="muted">Repositories {repositories}</Text>}
      <Text tone="muted">Drives {drivesLabel(runner)}</Text>
    </details>
  );
}
