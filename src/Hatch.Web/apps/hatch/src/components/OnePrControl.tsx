import { onePrSaid } from '../lib/onePr';
import type { Issue } from '../types';

export type OnePrSetting = 'inherit' | 'on' | 'off';

interface OnePrChoice {
  name: OnePrSetting;
  word: string;
  title: string;
}

/** Every setting the control offers, in the order it draws them - the one place
    in the component that names one. */
const CHOICES: OnePrChoice[] = [
  { name: 'inherit', word: 'Project default', title: "Follow the project's default, whatever it is set to." },
  {
    name: 'on',
    word: 'On',
    title: "Its tasks share its branch and one pull request, whatever the project's default is.",
  },
  {
    name: 'off',
    word: 'Off',
    title: 'Each task gets its own branch and pull request, whatever the project’s default is.',
  },
];

const settingOf = (own: boolean | null): OnePrSetting => (own === null ? 'inherit' : own ? 'on' : 'off');

/**
 * The three buttons alone - project default, on, off - with `value` marked and
 * disabled. The issue page draws them under `OnePrControl`; the New Issue form
 * draws them bare, there being no story yet to say anything about.
 */
export function OnePrSteps({
  value,
  label,
  busy = false,
  onChange,
}: {
  value: OnePrSetting;
  /** The group's accessible name. */
  label: string;
  busy?: boolean;
  onChange: (setting: OnePrSetting) => void;
}) {
  return (
    <div className="hatch-one-pr-steps" role="group" aria-label={label} aria-busy={busy}>
      {CHOICES.map((choice) => {
        const here = choice.name === value;
        return (
          <button
            key={choice.name}
            type="button"
            className={`hatch-one-pr-step${here ? ' here' : ''}`}
            title={choice.title}
            aria-pressed={here}
            disabled={here || busy}
            onClick={() => onChange(choice.name)}
          >
            {choice.word}
          </button>
        );
      })}
    </div>
  );
}

/**
 * *Do this story's or bug's tasks share its branch and one pull request?* A group of
 * three buttons - project default, on, off - with its own setting
 * marked, and a line beneath saying what is in effect and where it comes from.
 *
 * Shaped after `PreApprovalControl`'s band, minus the pill: there are three
 * answers and a story or a bug has one at all times, so they are all drawn and the
 * current one is the marked, disabled step. Unlike priority and pre-approval it
 * is not gated on the caller being a person - the write is open to an API key
 * (see `IssueOnePrController`), so a control it can use is always offered.
 *
 * Presentational and fetching nothing: the page re-reads after a press, so the
 * trail, the control and the tasks beneath all redraw from the server's answer
 * rather than from the assumption that the press worked, and a refusal lands in
 * the page's own error line in the server's own words.
 */
export function OnePrControl({
  issue,
  busy = false,
  onChange,
}: {
  issue: Pick<Issue, 'key' | 'type' | 'onePr' | 'onePrOwn' | 'onePrFrom'>;
  /** A press is in flight: the group stays where it is and stops answering. */
  busy?: boolean;
  onChange: (setting: OnePrSetting) => void;
}) {
  return (
    <div className="hatch-one-pr">
      <OnePrSteps
        value={settingOf(issue.onePrOwn)}
        label={`Set ${issue.key}'s one pull request setting`}
        busy={busy}
        onChange={onChange}
      />
      <span className={`hatch-one-pr-said${issue.onePr ? ' on' : ''}`}>{onePrSaid(issue)}</span>
    </div>
  );
}
