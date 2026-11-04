import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { Badge, Button, Field } from '@hatch/ui';
import {
  isNotARunner,
  isOwnOmission,
  OMISSION_REASON_MAX,
  omissionDraft,
  omissionReason,
  omissionWho,
  reasonCounter,
} from '../lib/runnerOmissions';
import type { RunnerOmission } from '../types';

interface OmittedRunnersProps {
  /** issue.omittedRunners: the issue's own rows and every ancestor's. */
  omissions: RunnerOmission[];
  /** GET /runners/names; null while unread. */
  runnerNames: string[] | null;
  /** Names of the person-scoped GET /runners rows, offered while typing. */
  pickable: string[];
  /** The runner whose lift is out, or null. */
  liftingRunner: string | null;
  onLift: (runner: string) => void;
  /** Resolves null once added, else the API's refusal sentence. */
  onAdd: (runner: string, reason: string) => Promise<string | null>;
}

/**
 * The runners this issue, and everything under it, is never handed to: each row
 * with who kept it off and why, and the form that adds another. Pure props, so
 * it draws without a fetch; an inherited row is lifted on the ticket it came from.
 */
export function OmittedRunners({ omissions, runnerNames, pickable, liftingRunner, onLift, onAdd }: OmittedRunnersProps) {
  const listId = useId();
  const [runner, setRunner] = useState('');
  const [reason, setReason] = useState('');
  const [adding, setAdding] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  const draft = omissionDraft(runner, reason);

  async function add() {
    if (!draft) return;
    setAdding(true);
    try {
      const r = await onAdd(draft.runner, draft.reason);
      if (r === null) {
        setRunner('');
        setReason('');
      }
      setRefusal(r);
    } finally {
      setAdding(false);
    }
  }

  return (
    <Field
      label="Omitted runners"
      as="div"
      hint="Runners this ticket and everything under it is never handed to."
    >
      {omissions.length === 0 ? (
        <p className="text-muted">No runner is omitted.</p>
      ) : (
        <ul className="hatch-omissions">
          {omissions.map((o) => (
            <li className="hatch-omission" key={o.runner}>
              <strong>{o.runner}</strong>
              {isNotARunner(o.runner, runnerNames) && <Badge>not a runner on this board</Badge>}
              <span className="text-muted">
                {omissionReason(o)} · {omissionWho(o)} on {new Date(o.at).toLocaleString()}
                {!isOwnOmission(o) && (
                  <>
                    {' from '}
                    <Link to={`/issues/${o.fromKey}`}>{o.fromKey}</Link>
                  </>
                )}
              </span>
              {isOwnOmission(o) ? (
                <Button disabled={liftingRunner === o.runner} onClick={() => onLift(o.runner)}>
                  {liftingRunner === o.runner ? 'Lifting...' : 'Lift'}
                </Button>
              ) : (
                <span className="text-muted">Lifted on {o.fromKey}.</span>
              )}
            </li>
          ))}
        </ul>
      )}

      <div className="hatch-omission-add">
        <input
          type="text"
          list={listId}
          value={runner}
          placeholder="Runner name"
          aria-label="Runner to omit"
          onChange={(e) => setRunner(e.target.value)}
        />
        <datalist id={listId}>
          {pickable.map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <input
          type="text"
          value={reason}
          maxLength={OMISSION_REASON_MAX}
          aria-label="Reason"
          onChange={(e) => setReason(e.target.value)}
        />
        <span className="text-muted">{reasonCounter(reason)}</span>
        <Button disabled={draft === null || adding} onClick={() => void add()}>
          Omit
        </Button>
        {refusal !== null && (
          <p className="hatch-omission-refusal" role="alert">
            {refusal}
          </p>
        )}
      </div>
    </Field>
  );
}
