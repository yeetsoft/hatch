import { agoPhrase } from '../lib/claim';

/**
 * The day something happened, in the reader's locale, with the full time and
 * how long ago as the title.
 *
 * The date is printed inline rather than left to the title because a phone has
 * no hover. The ago phrase is the title because it goes stale on a page left
 * open, where the date does not. Used wherever a row dates a thing, so the rows
 * agree.
 */
export function DatedAt({ at, now }: { at: string; now: Date }) {
  const when = new Date(at);

  return (
    <time dateTime={at} title={`${when.toLocaleString()}, ${agoPhrase(at, now)}`}>
      {when.toLocaleDateString()}
    </time>
  );
}
