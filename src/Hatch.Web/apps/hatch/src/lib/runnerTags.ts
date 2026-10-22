/** What the runner tags field shows: one tag per line, blank for any runner. */
export function runnerTagsDraft(tags: string[]): string {
  return tags.join('\n');
}

/** The field, split into tags for the wire: one per line, trimmed, blanks dropped. */
export function runnerTagsRequest(draft: string): string[] {
  return draft
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '');
}

/** The non-owner read-out, after the landing sentence: "Any runner." with
    nothing required, or "Runners tagged a and b." - no comma for two, none
    before "and" for more, the join lib/playbooks.ts's andList already uses;
    reimplemented rather than imported, so this project-domain file does not
    reach into the playbooks one for one line. */
export function runnerTagsSentence(tags: string[]): string {
  if (tags.length === 0) return 'Any runner.';
  const joined = tags.length < 2 ? tags.join('') : `${tags.slice(0, -1).join(', ')} and ${tags[tags.length - 1]}`;
  return `Runners tagged ${joined}.`;
}

export function sameTags(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((tag, i) => tag === b[i]);
}
