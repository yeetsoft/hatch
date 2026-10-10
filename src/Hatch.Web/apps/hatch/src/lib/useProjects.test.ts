import { describe, expect, it } from 'vitest';
import { hasMultipleProjects } from './useProjects';
import type { Project } from '../types';

const project = (over: Partial<Project>): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  ...over,
});

describe('hasMultipleProjects', () => {
  it('is false for no projects', () => {
    expect(hasMultipleProjects([])).toBe(false);
  });

  it('is false for a single project', () => {
    expect(hasMultipleProjects([project({ id: 1, key: 'AER' })])).toBe(false);
  });

  it('is true for two or more projects', () => {
    expect(hasMultipleProjects([project({ id: 1, key: 'AER' }), project({ id: 2, key: 'HA' })])).toBe(true);
  });
});
