import { describe, expect, it } from 'vitest';
import { errorMessage, viewFor } from './authState';

const me = (role: string | null, name: string | null = 'Ada') => ({ personId: role ? 'p1' : null, name, role });

describe('viewFor', () => {
  it('is signed out on 401', () => {
    expect(viewFor(401, null)).toEqual({ kind: 'signed-out' });
  });

  it('is pending for a Pending person, in any case', () => {
    expect(viewFor(200, me('Pending'))).toEqual({ kind: 'pending', name: 'Ada' });
    expect(viewFor(200, me('pENDING'))).toEqual({ kind: 'pending', name: 'Ada' });
    expect(viewFor(200, me('Pending', null))).toEqual({ kind: 'pending', name: null });
  });

  it('redirects a User or Admin', () => {
    expect(viewFor(200, me('User'))).toEqual({ kind: 'redirect' });
    expect(viewFor(200, me('ADMIN'))).toEqual({ kind: 'redirect' });
  });

  it('treats an unclaimed device as signed out', () => {
    expect(viewFor(200, me(null, null))).toEqual({ kind: 'signed-out' });
  });

  it('never redirects on a failure', () => {
    expect(viewFor(500, null)).toEqual({ kind: 'error' });
    expect(viewFor(200, null)).toEqual({ kind: 'error' });
    expect(viewFor(200, me('Wizard'))).toEqual({ kind: 'error' });
  });
});

describe('errorMessage', () => {
  it('says something for each known code', () => {
    expect(errorMessage('access_denied')).toMatch(/cancelled/);
    expect(errorMessage('google_not_configured')).toMatch(/not set up/);
    expect(errorMessage('email_not_verified')).toMatch(/not verified/);
    expect(errorMessage('unknown_state')).toMatch(/too long/);
    expect(errorMessage('expired_state')).toMatch(/too long/);
  });

  it('names an unknown code', () => {
    expect(errorMessage('boom')).toContain('boom');
  });

  it('is null without a code', () => {
    expect(errorMessage(null)).toBeNull();
    expect(errorMessage('')).toBeNull();
  });
});
