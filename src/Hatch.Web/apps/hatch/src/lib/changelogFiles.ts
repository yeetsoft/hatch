/* Reading the changelog files the Projects page draws beside each repository
   that writes one: pure helpers only, so these run under plain Node with no DOM. */

import type { ChangelogFileDto } from '../types';
import { shortSha } from './playbookFiles';

/** What the board found on a repository's trunk, in a sentence: current at a
    sha, behind by some entries, absent, or - for a file somebody else wrote -
    the sentence the board stored, which names the sha and says what to do. */
export function changelogSentence(file: ChangelogFileDto): string {
  switch (file.state) {
    case 'current':
      return `changelog current at ${shortSha(file.sha)}`;
    case 'behind': {
      const behind = file.behind ?? 1;
      return `changelog behind by ${behind} ${behind === 1 ? 'entry' : 'entries'}`;
    }
    case 'absent':
      return 'no changelog on trunk yet';
    default:
      return file.problem ?? `the changelog file at ${shortSha(file.sha)} is not one Hatch wrote`;
  }
}
