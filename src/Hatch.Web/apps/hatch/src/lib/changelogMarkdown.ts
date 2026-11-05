/* The changelog report as markdown, for pasting where the page cannot go. Pure:
   no React, no DOM, no `window`. The caller supplies what a bucket is called
   and where an issue lives, so nothing here reads a locale or an origin.

   The shape follows the page - buckets in the order the answer gave them,
   epics or the stories beneath them by `report.level`, loose work only with
   `report.loose` on - except that a quiet gap is not written, as
   `hatch changelog` writes none either. */

import type { ChangelogReport, ChangelogReportEntry, ChangelogReportGroup } from '../types';
import { fixLine } from './changelog';
import { changelogReportEmptyWords } from './changelogReport';
import { beneathLabel } from './overnight';

export interface ChangelogMarkdownOptions {
  bucketLabel: (startIso: string) => string;
  issueUrl: (key: string) => string;
}

/** `text` with every character markdown would read as markup backslash-escaped,
    so a title is only ever text - `\<b\>` is not a tag. */
export const escapeMarkdown = (text: string): string => text.replace(/[\\`*_[\]<>]/g, '\\$&');

const link = (key: string, options: ChangelogMarkdownOptions): string => `[${key}](${options.issueUrl(key)})`;

/** One group's lines: the epic alone at `epic` level, the epic and the stories
    that closed under it at `story` level. */
export function groupLines(
  group: ChangelogReportGroup,
  level: ChangelogReport['level'],
  options: ChangelogMarkdownOptions,
): string[] {
  const epic = link(group.epic.key, options);
  const title = escapeMarkdown(group.epic.title);
  if (level === 'epic') return [`- ${epic} ${title} (${beneathLabel(group.descendantCount)})`];

  return [
    `- ${epic} ${title}${group.closedHere ? ' (completed)' : ''}`,
    ...group.stories.map((story) => `  - ${link(story.key, options)} ${escapeMarkdown(fixLine(story))}`),
  ];
}

/** One loose entry's line; an emergency says so in bold ahead of the link. */
export function looseLine(entry: ChangelogReportEntry, options: ChangelogMarkdownOptions): string {
  const flag = entry.priority === 'emergency' ? '**Emergency** ' : '';

  return `- ${flag}${link(entry.key, options)} ${escapeMarkdown(fixLine(entry))}`;
}

/** The report as markdown: one block per heading and per run of bullets,
    separated by a blank line, ending in one newline. */
export function changelogMarkdown(report: ChangelogReport, options: ChangelogMarkdownOptions): string {
  if (report.buckets.length === 0) return `${changelogReportEmptyWords()}\n`;

  const blocks: string[] = [];
  for (const bucket of report.buckets) {
    blocks.push(`## ${options.bucketLabel(bucket.start)}`);
    if (bucket.groups.length > 0) {
      blocks.push(bucket.groups.flatMap((group) => groupLines(group, report.level, options)).join('\n'));
    }
    if (report.loose && bucket.loose.length > 0) {
      blocks.push('### Also landed');
      blocks.push(bucket.loose.map((entry) => looseLine(entry, options)).join('\n'));
    }
  }

  return `${blocks.join('\n\n')}\n`;
}
