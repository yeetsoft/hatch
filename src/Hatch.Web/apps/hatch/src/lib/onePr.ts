import type { OnePrSetting } from '../components/OnePrControl';
import type { Issue, IssueType } from '../types';

/** What the story or bug is doing right now and why, in a sentence - the answer to
    "is this one PR" that the control gives without anybody pressing anything. */
export function onePrSaid(
  issue: Pick<Issue, 'type' | 'onePr' | 'onePrOwn' | 'onePrFrom'> & { onePrEnforced?: boolean },
): string {
  const value = issue.onePr ? 'On' : 'Off';
  if (issue.onePrEnforced && (issue.type === 'story' || issue.type === 'bug')) {
    return issue.onePrOwn === null
      ? `${value} · the project's default, enforced`
      : `${value} · set on this ${issue.type} before the project enforced its default; locked`;
  }
  if (issue.onePrOwn !== null) return `${value} · set on this ${issue.type}`;
  if (issue.onePrFrom) return `${value} · inherited from ${issue.onePrFrom}`;
  return issue.type === 'story' || issue.type === 'bug'
    ? `${value} · the project's default`
    : `${value} · not under a story or bug`;
}

/** What a filing sends for `onePr`: the setting for a story or a bug, nothing for any
    other type - the server refuses the field on anything that is not one. Nothing
    either when the chosen project enforces its own default, since there is then no
    setting of this issue's own to send. */
export function onePrToFile(type: IssueType, setting: OnePrSetting, enforced = false): OnePrSetting | undefined {
  return enforced || !(type === 'story' || type === 'bug') ? undefined : setting;
}
