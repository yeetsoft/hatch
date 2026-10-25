/** What the runner projects field shows: one project key per line, blank for every project. */
export function runnerProjectsDraft(projects: string[]): string {
  return projects.join('\n');
}

/** The field, split into project keys for the wire: one per line, trimmed,
    blanks dropped. The server is the only validator - it upper-cases, de-dupes
    and refuses a key it does not know. */
export function runnerProjectsRequest(draft: string): string[] {
  return draft
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '');
}

export function sameProjects(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((key, i) => key === b[i]);
}
