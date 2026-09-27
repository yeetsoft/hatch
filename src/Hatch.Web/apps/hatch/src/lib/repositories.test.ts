import { describe, expect, it } from 'vitest';
import { moved, repositoryObjection, withoutRemote, withRemote } from './repositories';

describe('withRemote', () => {
  it('appends', () => {
    const list = [{ remote: 'a' }];
    expect(withRemote(list, { remote: 'b' })).toEqual([{ remote: 'a' }, { remote: 'b' }]);
  });
});

describe('withoutRemote', () => {
  it('drops the right index and leaves the rest in order', () => {
    const list = [{ remote: 'a' }, { remote: 'b' }, { remote: 'c' }];
    expect(withoutRemote(list, 1)).toEqual([{ remote: 'a' }, { remote: 'c' }]);
  });
});

describe('moved', () => {
  it('swaps adjacent entries, moving index 1 to 0 makes it primary', () => {
    const list = [{ remote: 'a' }, { remote: 'b' }];
    expect(moved(list, 1, 0)).toEqual([{ remote: 'b' }, { remote: 'a' }]);
  });

  it('no-ops past either end', () => {
    const list = [{ remote: 'a' }, { remote: 'b' }];
    expect(moved(list, 0, -1)).toEqual(list);
    expect(moved(list, 1, 2)).toEqual(list);
  });
});

describe('repositoryObjection', () => {
  it('objects to a blank remote', () => {
    expect(repositoryObjection('')).not.toBeNull();
    expect(repositoryObjection('   ')).not.toBeNull();
  });

  it('is silent on a real remote', () => {
    expect(repositoryObjection('git@example.com:org/repo.git')).toBeNull();
  });
});
