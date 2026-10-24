/* How a project's three claim limits read in a sentence: its own where it sets
   one, the install's where it does not. No DOM. */

import type { Project } from '../types';

function read(value: number | null | undefined, unit: string): string {
  if (value === null || value === undefined) return 'follows this install';
  return value === 0 ? 'off' : `${value}${unit}`;
}

export function claimLimitsSentence(project: Pick<Project, 'stallLapseMinutes' | 'stallGraceMaxMinutes' | 'claimLapseLimit'>): string {
  return `Claims: quiet window ${read(project.stallLapseMinutes, ' min')}, grace cap ${read(project.stallGraceMaxMinutes, ' min')}, lapse limit ${read(project.claimLapseLimit, '')}.`;
}
