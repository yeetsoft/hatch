import { Link } from 'react-router-dom';
import { Badge, Text } from '@hatch/ui';
import { RunnerGlyph } from './RunnerGlyph';
import {
  liveSessions,
  rowSecondLine,
  rowThirdLine,
  runnerGlyphState,
  sessionParts,
  speakingSession,
} from '../lib/runners';
import type { Runner } from '../types';

/**
 * One runner in the list: a link to its detail, in three lines. The glyph and
 * name, with a badge for several live sessions or a revoked key; what it is
 * doing; and whose it is and where it runs. The words are `lib/runners.ts`'s -
 * this only draws them.
 */
export function RunnerRow({ runner, selected, now }: { runner: Runner; selected: boolean; now: Date }) {
  const state = runnerGlyphState(runner, now);
  const speaking = speakingSession(runner, now);
  const label = speaking ? sessionParts(speaking, runner, now).words : state === 'gone' ? 'Gone' : 'Not started';
  const sessions = liveSessions(runner, now).length;
  const second = rowSecondLine(runner, now);

  return (
    <Link
      to={`/runners/${encodeURIComponent(runner.name)}`}
      className="hatch-runner-row"
      aria-current={selected ? 'page' : undefined}
    >
      <span className="hatch-runner-row__head">
        <RunnerGlyph state={state} label={label} />
        <span className="hatch-runner-row__name">
          <strong className="hatch-runner-name">{runner.name}</strong>
        </span>
        {sessions > 1 && <Badge tone="muted">x{sessions}</Badge>}
        {runner.revokedAt && <Badge tone="muted">revoked</Badge>}
      </span>
      <Text as="span" tone="muted" className="hatch-runner-row__second" title={second}>
        {second}
      </Text>
      <Text as="span" tone="muted" className="hatch-runner-row__third">
        {rowThirdLine(runner, now)}
      </Text>
    </Link>
  );
}
