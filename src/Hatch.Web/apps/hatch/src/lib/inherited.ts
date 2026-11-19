/* The one sentence a card, the peek, the issue page and the plan say about an
   inherited level or pull: where it came from, and whether it is honoured.

   Pure, so the five places that draw it share one wording. Where the reader's
   scope cannot see the ancestor, the key is dropped and the level and the
   verdict stay - which is why a sentence takes a key-or-null and not a flag. */

import { PRE_APPROVAL_LEVELS, type PreApproval } from './preApproval';
import { PRIORITY_LEVELS, type Priority } from './priority';

/** What this reads of an `IssueCard` or an `Issue`, and nothing else. */
export interface InheritedFields {
  preApproval: PreApproval;
  preApprovalFrom: string | null;
  preApprovalPassedOver?: string | null;
  preApprovalPassedOverLevel?: PreApproval | null;
  priority: Priority;
  priorityFrom: string | null;
  priorityPassedOver?: string | null;
  priorityPassedOverLevel?: Priority | null;
  pullPassedOver?: string | null;
  fromBeyondScope?: string[];
}

export interface InheritedSentences {
  preApproval: string | null;
  priority: string | null;
  pull: string | null;
}

/** `where` is the ancestor's key, or null where the reader's scope does not
    hold it. `honoured` false is the level that stopped at a project line. */
export function levelSentence(word: string, where: string | null, honoured: boolean, projectKey: string): string {
  const from = where ? `${word} on ${where}` : `${word} from an issue outside your scope`;
  return honoured ? from : `${from}, not honoured in ${projectKey}`;
}

export function pullSentence(parentKey: string | null, projectKey: string): string {
  return parentKey
    ? `${parentKey} is in progress, but its pull is not honoured in ${projectKey}`
    : `from an issue outside your scope, not honoured in ${projectKey}`;
}

/** The level's lowercase word, or null for none and normal, which are the
    absence of a level and so have no source to name. */
function wordOf(levels: { name: string; word: string }[], name: string): string | null {
  if (name === 'none' || name === 'normal') return null;
  const word = levels.find((l) => l.name === name)?.word;
  return word ? word.toLowerCase() : null;
}

/** The passed-over form, then the honoured one, joined where both apply. */
function sentenceFor(
  levels: { name: string; word: string }[],
  effective: string,
  from: string | null,
  passedOver: string | null | undefined,
  passedOverLevel: string | null | undefined,
  beyond: boolean,
  projectKey: string,
): string | null {
  const parts: string[] = [];
  const passedWord = passedOverLevel ? wordOf(levels, passedOverLevel) : null;
  if (passedWord) parts.push(levelSentence(passedWord, passedOver ?? null, false, projectKey));
  const word = from || beyond ? wordOf(levels, effective) : null;
  if (word) parts.push(levelSentence(word, from, true, projectKey));
  return parts.length ? parts.join('; ') : null;
}

/** The sentences for one item in the project `projectKey`, each null where
    there is nothing to say. A passed-over level and a farther honoured one can
    both apply, and then both are said. */
export function inheritedSentences(item: InheritedFields, projectKey: string): InheritedSentences {
  const beyond = item.fromBeyondScope ?? [];
  return {
    preApproval: sentenceFor(
      PRE_APPROVAL_LEVELS,
      item.preApproval,
      item.preApprovalFrom,
      item.preApprovalPassedOver,
      item.preApprovalPassedOverLevel,
      beyond.includes('preApproval'),
      projectKey,
    ),
    priority: sentenceFor(
      PRIORITY_LEVELS,
      item.priority,
      item.priorityFrom,
      item.priorityPassedOver,
      item.priorityPassedOverLevel,
      beyond.includes('priority'),
      projectKey,
    ),
    pull:
      item.pullPassedOver || beyond.includes('pullPassedOver')
        ? pullSentence(item.pullPassedOver ?? null, projectKey)
        : null,
  };
}

/** The plan's small mark for an epic: the board's own glyph for each thing
    said, and every sentence joined into its title. Null where nothing is. */
export function inheritedMark(item: InheritedFields, projectKey: string): { glyph: string; title: string } | null {
  const said = inheritedSentences(item, projectKey);
  const glyphs: string[] = [];
  if (said.preApproval) {
    const name = item.preApprovalPassedOverLevel ?? item.preApproval;
    glyphs.push(PRE_APPROVAL_LEVELS.find((l) => l.name === name)?.glyph ?? '');
  }
  if (said.priority) {
    const name = item.priorityPassedOverLevel ?? item.priority;
    glyphs.push(PRIORITY_LEVELS.find((l) => l.name === name)?.glyph ?? '');
  }
  if (said.pull) glyphs.push('↳');
  const title = [said.preApproval, said.priority, said.pull].filter((s): s is string => !!s).join(' · ');
  return title ? { glyph: glyphs.filter(Boolean).join(' '), title } : null;
}
