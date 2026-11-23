import { HEALTH_WINDOWS } from '../lib/boardHealth';
import type { HealthWindow } from '../lib/boardHealth';

/** The window buttons, one pressed. The strip draws them and so does each tile's
    dialog: the buttons are the same, and so is what they do, because both call
    the strip's own `onWindow` - which resets the marked bar and stores the
    choice. */
export function HealthWindowControl({
  window: chosen,
  onWindow,
}: {
  window: HealthWindow;
  onWindow: (choice: HealthWindow) => void;
}) {
  return (
    <div className="hatch-health-windows" role="group" aria-label="Window">
      {HEALTH_WINDOWS.map((choice) => (
        <button
          key={choice}
          type="button"
          className={`hatch-health-window${choice === chosen ? ' active' : ''}`}
          aria-pressed={choice === chosen}
          onClick={() => onWindow(choice)}
        >
          {choice}
        </button>
      ))}
    </div>
  );
}
