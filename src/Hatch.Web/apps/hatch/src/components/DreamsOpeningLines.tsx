/* The three lines under the Dreams title: what the page is for, one verb each.
   A component of its own because PageHeader puts its description in a <p>,
   which cannot hold a list, and so the page draws the list itself. */

const LINES = [
  { lead: 'Drop in', rest: 'snippets of ideas that are not epics yet.' },
  { lead: 'Recycle', rest: 'epics that are not ready.' },
  { lead: 'Weave', rest: 'new work out of the cloud of the undefined.' },
];

export function DreamsOpeningLines() {
  // role="list": Safari drops list semantics once list-style is none.
  return (
    <ol className="hatch-dream-lines" role="list">
      {LINES.map(({ lead, rest }, at) => (
        <li key={lead}>
          {/* The <ol> already numbers the items for a screen reader. */}
          <span className="hatch-dream-lines__n" aria-hidden="true">
            {String(at + 1).padStart(2, '0')}
          </span>
          <span className="hatch-gradient-ink hatch-dream-lines__lead">{lead}</span> {rest}
        </li>
      ))}
    </ol>
  );
}
