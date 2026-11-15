import { describe, expect, it } from 'vitest';
import { focusedIssueKey, issueInView } from './issueInView';

describe('issueInView', () => {
  it('is the page own issue before anything else', () => {
    expect(issueInView({ pageKey: 'HA-1', peekedKey: 'HA-2', focusedKey: 'HA-3' })).toBe('HA-1');
  });

  it('is the peeked card where the page has none of its own', () => {
    expect(issueInView({ pageKey: null, peekedKey: 'HA-2', focusedKey: 'HA-3' })).toBe('HA-2');
  });

  it('is the focused card where nothing is peeked', () => {
    expect(issueInView({ pageKey: null, peekedKey: null, focusedKey: 'HA-3' })).toBe('HA-3');
  });

  it('is nothing where nothing is in view', () => {
    expect(issueInView({ pageKey: null, peekedKey: null, focusedKey: null })).toBeNull();
  });
});

/* A fake with `closest` only: the web tests run in node, with no DOM. */
const card = (key: string) => ({ getAttribute: (name: string) => (name === 'data-issue-key' ? key : null) });

describe('focusedIssueKey', () => {
  it('reads the key off a card that has focus', () => {
    const el = { closest: (selector: string) => (selector === '[data-issue-key]' ? card('HA-7') : null) };
    expect(focusedIssueKey(el)).toBe('HA-7');
  });

  it('reads the key off a card from an element inside it', () => {
    const inner = { closest: () => card('HA-8') };
    expect(focusedIssueKey(inner)).toBe('HA-8');
  });

  it('is nothing for an element outside any card', () => {
    expect(focusedIssueKey({ closest: () => null })).toBeNull();
  });

  it('is nothing where nothing has focus', () => {
    expect(focusedIssueKey(null)).toBeNull();
    expect(focusedIssueKey(undefined)).toBeNull();
  });
});
