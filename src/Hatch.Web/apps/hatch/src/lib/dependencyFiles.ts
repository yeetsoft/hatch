/* Reading the dependency files the Projects page draws beside each bound
   repository: pure helpers only, so these run under plain Node with no DOM. */

import type { DependencyFileDto } from '../types';
import { shortSha } from './playbookFiles';

/** What a parsed declaration says it needs: each entry's label, with its
    version where it has one read as "or newer", joined by comma. */
export function declarationSentence(file: DependencyFileDto): string {
  if (file.entries.length === 0) return 'declares no dependencies';
  return (
    'needs ' + file.entries.map((e) => (e.version ? `${e.label} ${e.version} or newer` : e.label)).join(', ')
  );
}

/** Why an invalid declaration was refused, with the last sha it was good at
    where there was one - the wording PlaybooksPage's card uses. */
export function invalidSentence(file: DependencyFileDto): string {
  const problem = file.problem ?? '';
  return file.lastGoodSha !== null ? `${problem} — last good sha ${shortSha(file.lastGoodSha)}` : problem;
}

/** The one stored file a repository's declaration was read from, if any - the
    per-repository form of playbookFiles.ts's projectsReading. A set base
    branch picks the file read on that trunk; an unset one picks the sole file
    for the canonical, and is ambiguous (null) where two trunks were read. */
export function fileForRepository(
  repo: { canonical: string; baseBranch: string | null },
  files: DependencyFileDto[],
): DependencyFileDto | null {
  const forCanonical = files.filter((f) => f.canonical === repo.canonical);
  if (repo.baseBranch !== null) return forCanonical.find((f) => f.trunk === repo.baseBranch) ?? null;
  return forCanonical.length === 1 ? forCanonical[0] : null;
}
