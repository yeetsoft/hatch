import { describe, expect, it } from 'vitest';
import { keyAdvice } from './runnerKey';
import type { Me } from '../types';

const me = (over: Partial<Me>): Me => ({
  kind: 'person',
  name: 'Someone',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  ...over,
});

describe('keyAdvice', () => {
  it('is silent until Me is answered', () => {
    expect(keyAdvice(null)).toBe('none');
  });

  it('says a key can be left blank when the wall is off', () => {
    expect(keyAdvice(me({ kind: 'local', role: null }))).toBe('none');
  });

  it('sends an Admin to the keys page', () => {
    expect(keyAdvice(me({ role: 'admin' }))).toBe('needs-key-admin');
  });

  it('tells anyone else to ask', () => {
    expect(keyAdvice(me({ role: 'user' }))).toBe('needs-key-user');
  });
});
