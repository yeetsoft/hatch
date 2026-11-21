import { describe, expect, it, vi } from 'vitest';
import {
  canCreateRunner,
  canManageKey,
  changeOwner,
  createProblem,
  createRunnerKey,
  keyAdvice,
  revokeRunnerKey,
  rotateRunnerKey,
} from './runnerKey';
import type { Me, RunnerMinted } from '../types';

const minted = (over: Partial<RunnerMinted> = {}): RunnerMinted => ({
  runner: { name: 'Quill' } as RunnerMinted['runner'],
  secret: 'hatch_ak_secret',
  ...over,
});

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
    expect(keyAdvice(me({ role: 'admin' }))).toBe('needs-key');
  });

  it('sends a user to the keys page too', () => {
    expect(keyAdvice(me({ role: 'user' }))).toBe('needs-key');
  });
});

describe('canCreateRunner', () => {
  it('is true for a signed-in admin and a signed-in user', () => {
    expect(canCreateRunner(me({ role: 'admin' }))).toBe(true);
    expect(canCreateRunner(me({ role: 'user' }))).toBe(true);
  });

  it('is false until Me is answered', () => {
    expect(canCreateRunner(null)).toBe(false);
  });

  it('is false with the wall off, where nobody has a role', () => {
    expect(canCreateRunner(me({ kind: 'local', role: null }))).toBe(false);
    expect(canCreateRunner(me({ kind: 'none', role: null }))).toBe(false);
  });
});

describe('canManageKey', () => {
  it('lets an admin manage any runner', () => {
    expect(canManageKey(me({ role: 'admin' }), { yours: false })).toBe(true);
    expect(canManageKey(me({ role: 'admin' }), { yours: true })).toBe(true);
  });

  it('lets a user manage only their own', () => {
    expect(canManageKey(me({ role: 'user' }), { yours: true })).toBe(true);
    expect(canManageKey(me({ role: 'user' }), { yours: false })).toBe(false);
  });

  it('is false with the wall off, even where yours is true', () => {
    expect(canManageKey(me({ kind: 'local', role: null }), { yours: true })).toBe(false);
    expect(canManageKey(me({ kind: 'none', role: null }), { yours: true })).toBe(false);
  });

  it('is false until Me is answered', () => {
    expect(canManageKey(null, { yours: true })).toBe(false);
  });
});

describe('createProblem', () => {
  it('refuses an empty name', () => {
    expect(createProblem('  ')).toMatch(/needs a name/);
  });

  it('refuses a name over MAX_NAME_LENGTH', () => {
    expect(createProblem('a'.repeat(121))).toMatch(/at most 120/);
  });

  it('is silent for a name that fits', () => {
    expect(createProblem('Quill')).toBeNull();
  });
});

describe('createRunnerKey', () => {
  it('posts the trimmed name and owner when one is chosen', async () => {
    const createRunner = vi.fn().mockResolvedValue(minted());
    const setRunnerOwner = vi.fn().mockResolvedValue(undefined);
    const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name: '  Quill  ', ownerPersonId: 'p1' });

    expect(createRunner).toHaveBeenCalledWith({ name: 'Quill', ownerPersonId: 'p1' });
    expect(setRunnerOwner).not.toHaveBeenCalled();
    expect(result).toEqual({ secret: { name: 'Quill', secret: 'hatch_ak_secret', rotated: false } });
  });

  it('posts only the name, and never touches the owner, where none is given', async () => {
    const createRunner = vi.fn().mockResolvedValue(minted());
    const setRunnerOwner = vi.fn().mockResolvedValue(undefined);
    const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name: ' Quill ' });

    expect(createRunner).toHaveBeenCalledWith({ name: 'Quill' });
    expect(setRunnerOwner).not.toHaveBeenCalled();
    expect(result).toEqual({ secret: { name: 'Quill', secret: 'hatch_ak_secret', rotated: false } });
  });

  it('posts only the name for "nobody", then clears the owner', async () => {
    const createRunner = vi.fn().mockResolvedValue(minted());
    const setRunnerOwner = vi.fn().mockResolvedValue(undefined);
    const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name: 'Quill', ownerPersonId: '' });

    expect(createRunner).toHaveBeenCalledWith({ name: 'Quill' });
    expect(setRunnerOwner).toHaveBeenCalledWith('Quill', null);
    expect(result).toEqual({ secret: { name: 'Quill', secret: 'hatch_ak_secret', rotated: false } });
  });

  it('still returns the secret when clearing the owner fails', async () => {
    const createRunner = vi.fn().mockResolvedValue(minted());
    const setRunnerOwner = vi.fn().mockRejectedValue(new Error('refused'));
    const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name: 'Quill', ownerPersonId: '' });

    expect(result).toEqual({ secret: { name: 'Quill', secret: 'hatch_ak_secret', rotated: false } });
  });

  it('turns a rejected create into a refusal, with no secret', async () => {
    const createRunner = vi.fn().mockRejectedValue(new Error('a runner called Quill already exists'));
    const setRunnerOwner = vi.fn();
    const result = await createRunnerKey({ createRunner, setRunnerOwner }, { name: 'Quill', ownerPersonId: '' });

    expect(result).toEqual({ refusal: 'a runner called Quill already exists' });
    expect(setRunnerOwner).not.toHaveBeenCalled();
  });
});

describe('rotateRunnerKey', () => {
  it('returns the new secret, marked rotated', async () => {
    const rotateRunner = vi.fn().mockResolvedValue(minted({ secret: 'hatch_ak_new' }));
    const result = await rotateRunnerKey({ rotateRunner }, 'Quill');

    expect(rotateRunner).toHaveBeenCalledWith('Quill');
    expect(result).toEqual({ secret: { name: 'Quill', secret: 'hatch_ak_new', rotated: true } });
  });

  it('turns a rejection into a refusal', async () => {
    const rotateRunner = vi.fn().mockRejectedValue(new Error('refused'));
    expect(await rotateRunnerKey({ rotateRunner }, 'Quill')).toEqual({ refusal: 'refused' });
  });
});

describe('revokeRunnerKey', () => {
  it('reloads and returns null on success', async () => {
    const revokeRunner = vi.fn().mockResolvedValue(undefined);
    const reload = vi.fn().mockResolvedValue(undefined);
    const result = await revokeRunnerKey({ revokeRunner }, 'Quill', reload);

    expect(revokeRunner).toHaveBeenCalledWith('Quill');
    expect(result).toBeNull();
    expect(reload).toHaveBeenCalled();
  });

  it('reloads and returns the refusal on failure', async () => {
    const revokeRunner = vi.fn().mockRejectedValue(new Error('refused'));
    const reload = vi.fn().mockResolvedValue(undefined);
    expect(await revokeRunnerKey({ revokeRunner }, 'Quill', reload)).toBe('refused');
    expect(reload).toHaveBeenCalled();
  });
});

describe('changeOwner', () => {
  it('reloads and returns null on success', async () => {
    const setRunnerOwner = vi.fn().mockResolvedValue(undefined);
    const reload = vi.fn().mockResolvedValue(undefined);
    const result = await changeOwner({ setRunnerOwner }, 'Quill', 'p1', reload);

    expect(setRunnerOwner).toHaveBeenCalledWith('Quill', 'p1');
    expect(result).toBeNull();
    expect(reload).toHaveBeenCalled();
  });

  it('reloads and returns the refusal on failure', async () => {
    const setRunnerOwner = vi.fn().mockRejectedValue(new Error('refused'));
    const reload = vi.fn().mockResolvedValue(undefined);
    expect(await changeOwner({ setRunnerOwner }, 'Quill', null, reload)).toBe('refused');
    expect(reload).toHaveBeenCalled();
  });
});
