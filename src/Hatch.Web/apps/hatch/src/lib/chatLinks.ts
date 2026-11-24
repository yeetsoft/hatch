import type { MeChat, MeChatLink, MeChatMember, MeChatWorkspace } from '../types';
import { HttpError, message } from './errors';

/**
 * The rules behind the Slack accounts section on the Settings page: whether it
 * is drawn, the words for where a link stands, how a member is found in the
 * picker, how a confirmation link's secret is read off the address, and what a
 * refusal says.
 *
 * Pulled out of the component as lib/homeScreen.ts is, so each can be tested
 * without a DOM runner.
 */

/** The query parameter a message's confirmation link carries its secret in. */
export const CHAT_LINK_PARAM = 'chatLink';

/** Drawn when Hatch has no address to put in a message. Names the variable that sets it. */
export const LINKS_OFF_WORDS =
  'Hatch has no address to send in a message, so it cannot link a Slack account. An operator sets HATCH_PUBLIC_URL to give it one.';

/** Whether the section is drawn: only when there is a workspace to link in. */
export const chatSectionApplies = (chat: MeChat | null): chat is MeChat =>
  chat !== null && chat.workspaces.length > 0;

/** A waiting link whose time has passed: the message's link no longer works. */
export const linkRanOut = (link: MeChatLink | null, now: number): boolean =>
  link !== null && link.state === 'waiting' && link.expiresAt !== null && Date.parse(link.expiresAt) <= now;

/** Where a link stands, which button the row offers, and the words for it. */
export interface LinkStanding {
  words: string;
  /** Whether *Link* is offered; the section also needs `linksOn` for it. */
  canLink: boolean;
  canUnlink: boolean;
}

export function standing(workspace: MeChatWorkspace, now: number): LinkStanding {
  const link = workspace.link;
  if (link === null) return { words: 'Not linked', canLink: true, canUnlink: false };
  if (link.state === 'linked') return { words: `Linked as ${link.memberName}`, canLink: false, canUnlink: true };
  if (linkRanOut(link, now)) {
    return { words: `The link sent to ${link.memberName} ran out`, canLink: true, canUnlink: true };
  }
  return {
    words: `Waiting: a message with a link went to ${link.memberName} in Slack`,
    canLink: true,
    canUnlink: true,
  };
}

/** What a Slack member reads as in the picker: the name, then the display name when it differs. */
export const memberWords = (member: MeChatMember): { name: string; displayName: string | null } => ({
  name: member.name,
  displayName: member.displayName && member.displayName !== member.name ? member.displayName : null,
});

/** The members a typed query keeps, case-blind, on name and display name. */
export function filterMembers(members: MeChatMember[], query: string): MeChatMember[] {
  const q = query.trim().toLowerCase();
  if (q === '') return members;
  return members.filter((m) => m.name.toLowerCase().includes(q) || (m.displayName ?? '').toLowerCase().includes(q));
}

/** The secret in an address's query, or null when there is none or it is empty. */
export function readChatLink(search: string): string | null {
  const secret = new URLSearchParams(search).get(CHAT_LINK_PARAM);
  return secret !== null && secret !== '' ? secret : null;
}

/** The query with the secret taken out: `?` and what is left, or empty when nothing is. */
export function stripChatLink(search: string): string {
  const params = new URLSearchParams(search);
  params.delete(CHAT_LINK_PARAM);
  const rest = params.toString();
  return rest === '' ? '' : `?${rest}`;
}

const SENTENCES: Record<number, string> = {
  410: 'That link was already used, was replaced by a newer one, or ran out. Link again to get a new one.',
  403: "That link belongs to somebody else's request, so it did nothing here.",
  429: 'You can ask for five links an hour. Try again later.',
  404: 'That workspace is not one you can see. Reload the list and try again.',
};

/**
 * The sentence for a refused link call. A 410, 403, 429 and 404 get a fixed one;
 * a 409, 400 and 502 show the server's own, which already says what to do, and
 * nothing else is matched on its text.
 */
export function refusalWords(err: unknown): string {
  if (err instanceof HttpError) {
    const fixed = SENTENCES[err.status];
    if (fixed !== undefined) return fixed;
  }
  return message(err);
}

/** What the confirm page says above the section, and whether it is a failure. */
export interface ConfirmOutcome {
  words: string;
  failed: boolean;
}

export const confirmedWords = (link: MeChatLink): ConfirmOutcome => ({
  words: `Linked as ${link.memberName}.`,
  failed: false,
});

export const confirmFailedWords = (err: unknown): ConfirmOutcome => ({ words: refusalWords(err), failed: true });

/** The line under the Runner failures switch. */
export const RUNNER_FAILURES_HINT = 'Direct messages when one of your runners cannot work.';

/** What a linked row says Slack last told it when a direct message to it was refused, with when
    in the viewer's own time - or null where no refusal is on file. */
export function refusalLine(link: MeChatLink, locale?: string, timeZone?: string): string | null {
  if (link.lastRefusal === null || link.lastRefusalAt === null) return null;
  const sentence = link.lastRefusal.endsWith('.') ? link.lastRefusal : `${link.lastRefusal}.`;
  const when = new Date(link.lastRefusalAt).toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone });
  return `Slack could not send to this account: ${sentence} (${when})`;
}

/** What a flip of the Runner failures switch comes to: the updated link, or a refusal's words. */
export type RunnerFailuresSave = { ok: true; link: MeChatLink } | { ok: false; words: string };

/** Saves the switch through `send`, the client's own PUT, and reads a refusal's words off the
    error so the card can show them without knowing the shape of a failure. */
export async function saveRunnerFailures(
  link: MeChatLink,
  on: boolean,
  send: (linkId: number, body: { runnerFailures: boolean }) => Promise<MeChatLink>,
): Promise<RunnerFailuresSave> {
  try {
    const updated = await send(link.id, { runnerFailures: on });
    return { ok: true, link: updated };
  } catch (err) {
    return { ok: false, words: refusalWords(err) };
  }
}
