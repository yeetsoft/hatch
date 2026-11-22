/**
 * The button in the filter bar that hides and shows the board's health strip,
 * built as `FlowVizControl` is: pressed, the strip is drawn above the columns.
 *
 * Owns no state - `on` and `onToggle` are the board's. It is drawn with or
 * without history, since a hidden strip reads nothing and so cannot know.
 */
export function BoardHealthToggle({ on, onToggle }: { on: boolean; onToggle: () => void }) {
  const label = 'Show the health strip above the board';

  return (
    <button
      type="button"
      className={`hatch-health-toggle${on ? ' active' : ''}`}
      aria-pressed={on}
      title={label}
      aria-label={label}
      onClick={onToggle}
    >
      <svg className="hatch-health-toggle-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path
          d="M2 12h5l3-8 4 16 3-8h5"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </button>
  );
}
