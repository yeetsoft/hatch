/** What the dangerous field shows: this issue's own reason, blank for none. */
export function dangerousDraft(own: string | null): string {
  return own ?? '';
}

/** The field, trimmed for the wire: an empty string clears the flag. */
export function dangerousRequest(draft: string): string {
  return draft.trim();
}

/** The board card marker's hover title. */
export function dangerousTitle(reason: string): string {
  return `Dangerous - ${reason}`;
}

/** The read-mode fact: the reason, plus where it was inherited from. */
export function dangerousFact(reason: string, from: string | null): string {
  return from ? `${reason} · inherited from ${from}` : reason;
}
