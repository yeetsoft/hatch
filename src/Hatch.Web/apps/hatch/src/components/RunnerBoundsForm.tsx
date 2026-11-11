import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Card, Field, Text } from '@hatch/ui';
import { MomentField } from './MomentField';
import {
  boundsProblem,
  ceilingsChanged,
  ceilingsOf,
  ceilingsRequest,
  groupedTokens,
  MAX_SPEND_HINT,
  sessionTotals,
  tokenFieldHint,
  tokenFieldOf,
  type RunnerCeilings,
} from '../lib/runners';
import { compactTokens, moneyPhrase } from '../lib/workLog';
import type { Runner, RunnerPatchRequest, RunnerSession } from '../types';

/** A ceiling's label, with `(default)` beside it while the field is inheriting the site's. */
const tokenLabel = (text: string, source: Runner['maxTokensSource']) => (
  <>
    {text}
    {source === 'default' && (
      <Text as="span" tone="muted">
        {' '}
        (default)
      </Text>
    )}
  </>
);

/** A ceiling in words, for the read-only card: the figure, or what an empty one means. */
const capWords = (cap: number | null, noun: string, source: Runner['maxTokensSource']) =>
  cap === null ? 'no cap' : `${compactTokens(cap)} ${noun}${source === 'default' ? ' (default)' : ''}`;

/**
 * The detail's "Bounds" card: the five ceilings, saved on their own button and
 * never together with the tags and under.
 *
 * Saved on a button rather than on blur, for the reason `BoundsForm` is: these
 * are numbers somebody is part way through typing. A refusal that names a token
 * flag is drawn under that field; any other is a line above the buttons, both
 * inside this card. A caller who cannot edit gets the same figures as text,
 * with no input and no button.
 */
export function RunnerBoundsForm({
  runner,
  live,
  editable,
  refusal,
  onSave,
}: {
  runner: Runner;
  live: RunnerSession[];
  editable: boolean;
  refusal: string | null;
  onSave: (request: RunnerPatchRequest) => void;
}) {
  const [draft, setDraft] = useState<RunnerCeilings>(ceilingsOf(runner));
  const dirty = ceilingsChanged(draft, runner);
  const problem = boundsProblem(draft);
  const refusedField = tokenFieldOf(refusal);
  const totals = sessionTotals(live);

  const set = (field: keyof RunnerCeilings, value: string) => setDraft({ ...draft, [field]: value });

  return (
    <Card>
      <h2 className="hatch-section-title">Bounds</h2>
      <Text tone="muted">Ceilings. A session's own flags narrow these and never raise them.</Text>
      {runner.under && (
        <Text tone="muted">
          Also held under{' '}
          <Link className="hatch-card-key" to={`/issues/${runner.under}`}>
            {runner.under}
          </Link>
        </Text>
      )}
      {live.length > 0 && (
        <Text>
          {totals.runs} runs · {moneyPhrase(totals.spent)} · {compactTokens(totals.tokens)} tokens ·{' '}
          {compactTokens(totals.tokensLastHour)} this hour
        </Text>
      )}

      {editable ? (
        <div className="hatch-runner-bounds">
          <div className="hatch-field-grid">
            <Field label="Max runs" hint="Increments this night may spend, counting the ones already spent. Empty is no cap.">
              <input inputMode="numeric" value={draft.maxRuns} onChange={(e) => set('maxRuns', e.target.value)} />
            </Field>
            <Field label="Max spend" hint={MAX_SPEND_HINT}>
              <input inputMode="decimal" value={draft.maxSpend} onChange={(e) => set('maxSpend', e.target.value)} />
            </Field>
            <Field
              label={tokenLabel('Max tokens this run', runner.maxTokensSource)}
              hint={tokenFieldHint(runner, 'maxTokens')}
              error={refusedField === 'maxTokens' ? refusal : undefined}
            >
              <input
                value={draft.maxTokens}
                onChange={(e) => set('maxTokens', e.target.value)}
                onBlur={(e) => set('maxTokens', groupedTokens(e.target.value))}
              />
            </Field>
            <Field
              label={tokenLabel('Max tokens per hour', runner.maxTokensPerHourSource)}
              hint={tokenFieldHint(runner, 'maxTokensPerHour')}
              error={refusedField === 'maxTokensPerHour' ? refusal : undefined}
            >
              <input
                value={draft.maxTokensPerHour}
                onChange={(e) => set('maxTokensPerHour', e.target.value)}
                onBlur={(e) => set('maxTokensPerHour', groupedTokens(e.target.value))}
              />
            </Field>
            <MomentField
              label="Until"
              hint="An hour to stop by, in this browser's timezone."
              value={draft.untilAt === '' ? null : draft.untilAt}
              onChange={(next) => set('untilAt', next)}
            />
          </div>

          {problem && <p className="text-danger">{problem}</p>}
          {refusal && refusedField === null && <p className="text-danger">{refusal}</p>}

          <div className="hatch-form-actions">
            <Button
              variant="primary"
              disabled={!dirty || problem !== null}
              onClick={() => onSave(ceilingsRequest(draft))}
            >
              Save bounds
            </Button>
            <Button disabled={!dirty} onClick={() => setDraft(ceilingsOf(runner))}>
              Revert
            </Button>
          </div>

          <Text tone="muted">
            Folded into the loop at its next heartbeat, and applied from its next ticket onward. A cap set below what
            this night has already spent stops it at the next pass.
          </Text>
        </div>
      ) : (
        <>
          <Text>Max runs: {runner.maxRuns === null ? 'no cap' : runner.maxRuns}</Text>
          <Text>Max spend: {runner.maxSpend === null ? 'no cap' : moneyPhrase(runner.maxSpend)}</Text>
          <Text>Max tokens this run: {capWords(runner.maxTokens, 'tokens', runner.maxTokensSource)}</Text>
          <Text>Max tokens per hour: {capWords(runner.maxTokensPerHour, 'per hour', runner.maxTokensPerHourSource)}</Text>
          <Text>Until: {runner.untilAt === null ? 'no time set' : new Date(runner.untilAt).toLocaleString()}</Text>
        </>
      )}
    </Card>
  );
}
