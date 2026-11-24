import { describe, expect, it } from 'vitest';
import {
  chatSectionApplies,
  confirmedWords,
  confirmFailedWords,
  filterMembers,
  LINKS_OFF_WORDS,
  linkRanOut,
  memberWords,
  readChatLink,
  refusalWords,
  standing,
  stripChatLink,
} from './chatLinks';
import { HttpError } from './errors';
import type { MeChatLink, MeChatMember, MeChatWorkspace } from '../types';

const NOW = Date.parse('2026-06-01T12:00:00Z');
const workspace = (link: MeChatLink | null): MeChatWorkspace => ({ id: 3, name: 'Acme', link });
const waiting = (expiresAt: string | null): MeChatLink => ({ id: 9, state: 'waiting', memberName: 'Ada Reed', expiresAt });
const linked: MeChatLink = { id: 9, state: 'linked', memberName: 'Ada Reed', expiresAt: null };
const member = (id: string, name: string, displayName: string | null = null): MeChatMember => ({
  id,
  name,
  displayName,
});

describe('chatSectionApplies', () => {
  it('draws nothing before the first read answers', () => {
    expect(chatSectionApplies(null)).toBe(false);
  });
  it('draws nothing with no workspace', () => {
    expect(chatSectionApplies({ linksOn: true, workspaces: [] })).toBe(false);
  });
  it('draws with a workspace, whether or not links are on', () => {
    expect(chatSectionApplies({ linksOn: false, workspaces: [workspace(null)] })).toBe(true);
  });
});

describe('standing', () => {
  it('reads none as not linked and offers Link', () => {
    expect(standing(workspace(null), NOW)).toEqual({ words: 'Not linked', canLink: true, canUnlink: false });
  });
  it('reads linked with the member and offers Unlink only', () => {
    const s = standing(workspace(linked), NOW);
    expect(s.words).toBe('Linked as Ada Reed');
    expect(s).toMatchObject({ canLink: false, canUnlink: true });
  });
  it('reads waiting with whose member the message went to, and offers Link again', () => {
    const s = standing(workspace(waiting('2026-06-01T13:00:00Z')), NOW);
    expect(s.words).toContain('Ada Reed');
    expect(s.words).toContain('Waiting');
    expect(s).toMatchObject({ canLink: true, canUnlink: true });
  });
  it('reads a waiting link past its expiry as ran out', () => {
    const s = standing(workspace(waiting('2026-06-01T11:00:00Z')), NOW);
    expect(s.words).toContain('ran out');
    expect(s.canLink).toBe(true);
  });
});

describe('linkRanOut', () => {
  it('is false for none, for linked and for a waiting link still good', () => {
    expect(linkRanOut(null, NOW)).toBe(false);
    expect(linkRanOut(linked, NOW)).toBe(false);
    expect(linkRanOut(waiting('2026-06-01T12:00:01Z'), NOW)).toBe(false);
  });
  it('is true at and after the expiry', () => {
    expect(linkRanOut(waiting('2026-06-01T12:00:00Z'), NOW)).toBe(true);
  });
});

describe('the member filter', () => {
  const all = [member('U1', 'Ada Reed', 'ada'), member('U2', 'Bo Tan', 'Bobby'), member('U3', 'Cy Lo')];
  it('keeps everyone for an empty or blank query', () => {
    expect(filterMembers(all, '')).toEqual(all);
    expect(filterMembers(all, '  ')).toEqual(all);
  });
  it('matches the name regardless of case', () => {
    expect(filterMembers(all, 'REED').map((m) => m.id)).toEqual(['U1']);
  });
  it('matches the display name too', () => {
    expect(filterMembers(all, 'bobb').map((m) => m.id)).toEqual(['U2']);
  });
  it('keeps nobody when nothing matches', () => {
    expect(filterMembers(all, 'zzz')).toEqual([]);
  });
  it('shows a display name only when it adds something', () => {
    expect(memberWords(member('U1', 'Ada', 'Ada'))).toEqual({ name: 'Ada', displayName: null });
    expect(memberWords(member('U1', 'Ada Reed', 'ada'))).toEqual({ name: 'Ada Reed', displayName: 'ada' });
  });
});

describe('the address', () => {
  it('reads the secret', () => {
    expect(readChatLink('?chatLink=abc_123')).toBe('abc_123');
  });
  it('reads nothing when there is none or it is empty', () => {
    expect(readChatLink('')).toBeNull();
    expect(readChatLink('?x=1')).toBeNull();
    expect(readChatLink('?chatLink=')).toBeNull();
  });
  it('strips it, keeping any other parameter', () => {
    expect(stripChatLink('?chatLink=abc')).toBe('');
    expect(stripChatLink('?a=1&chatLink=abc&b=2')).toBe('?a=1&b=2');
    expect(stripChatLink('?a=1')).toBe('?a=1');
  });
});

describe('refusalWords', () => {
  it('gives a fixed sentence for 410, 403, 429 and 404', () => {
    expect(refusalWords(new HttpError('x', 410))).toContain('already used');
    expect(refusalWords(new HttpError('x', 403))).toContain("somebody else's request");
    expect(refusalWords(new HttpError('x', 429))).toContain('five links an hour');
    expect(refusalWords(new HttpError('x', 404))).toContain('Reload the list');
  });
  it("shows the server's own sentence for 409, 400 and 502, unchanged", () => {
    for (const status of [409, 400, 502]) {
      expect(refusalWords(new HttpError('Unlink first.', status))).toBe('Unlink first.');
    }
  });
  it('shows an error that is not a refusal as it reads', () => {
    expect(refusalWords(new Error('offline'))).toBe('offline');
  });
  it('gives a confirm outcome for each end', () => {
    expect(confirmedWords(linked)).toEqual({ words: 'Linked as Ada Reed.', failed: false });
    expect(confirmFailedWords(new HttpError('x', 410))).toMatchObject({ failed: true });
  });
});

describe('the links-off sentence', () => {
  it('names the variable', () => {
    expect(LINKS_OFF_WORDS).toContain('HATCH_PUBLIC_URL');
  });
});
