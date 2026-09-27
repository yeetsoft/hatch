import { beforeEach, describe, expect, it, vi } from 'vitest';

const refusal = (status: number, body: string) => new Response(body, { status });

describe('handledRefusal', () => {
  const replace = vi.fn();

  beforeEach(() => {
    // `leaving` is module state, so each test needs a fresh module.
    vi.resetModules();
    replace.mockReset();
    vi.stubGlobal('location', { pathname: '/apps/hatch/', search: '', replace });
  });

  const load = async () => (await import('./signIn')).handledRefusal;

  it('sends a 401 to sign in', async () => {
    expect(await (await load())(refusal(401, ''))).toBe(true);
    expect(replace).toHaveBeenCalledOnce();
  });

  it('sends a 403 pending_approval to sign in', async () => {
    const handled = await load();

    expect(await handled(refusal(403, JSON.stringify({ error: 'pending_approval' })))).toBe(true);
    expect(replace).toHaveBeenCalledOnce();
  });

  it('leaves the body readable for the caller', async () => {
    const res = refusal(403, JSON.stringify({ error: 'pending_approval' }));
    await (await load())(res);

    expect(await res.text()).toContain('pending_approval');
  });

  it('leaves any other 403 to the caller', async () => {
    const handled = await load();

    expect(await handled(refusal(403, JSON.stringify({ error: 'not_admin' })))).toBe(false);
    expect(await handled(refusal(403, 'nope'))).toBe(false);
    expect(replace).not.toHaveBeenCalled();
  });

  it('ignores success', async () => {
    expect(await (await load())(refusal(200, '{}'))).toBe(false);
  });
});
