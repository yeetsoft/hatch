import { describe, expect, it } from 'vitest';
import { runnerProjectsDraft, runnerProjectsRequest, sameProjects } from './runnerProjects';

describe('runnerProjectsDraft', () => {
  it('shows no projects as a blank box', () => {
    expect(runnerProjectsDraft([])).toBe('');
  });

  it('shows each project key on its own line', () => {
    expect(runnerProjectsDraft(['HA', 'AER'])).toBe('HA\nAER');
  });
});

describe('runnerProjectsRequest', () => {
  it('a blank field clears the projects', () => {
    expect(runnerProjectsRequest('')).toEqual([]);
  });

  it('splits the field into keys for the wire', () => {
    expect(runnerProjectsRequest('HA\nAER')).toEqual(['HA', 'AER']);
  });

  it('drops blank lines and trims the rest, leaving case to the server', () => {
    expect(runnerProjectsRequest('ha\n\n  AER  \n')).toEqual(['ha', 'AER']);
  });
});

describe('sameProjects', () => {
  it('is true for the same keys in the same order', () => {
    expect(sameProjects(['HA', 'AER'], ['HA', 'AER'])).toBe(true);
  });

  it('is order-sensitive', () => {
    expect(sameProjects(['HA', 'AER'], ['AER', 'HA'])).toBe(false);
  });

  it('is false for a different length', () => {
    expect(sameProjects(['HA'], [])).toBe(false);
  });
});
