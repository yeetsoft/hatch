import { contrastInk, isHexColor, safeColor } from '../color';
import { PROJECT_ICONS_BY_SLUG } from '../icons';
import './ProjectMark.css';

export type ProjectMarkSize = 'sm' | 'md' | 'lg';

export interface ProjectMarkProps {
  /** The project key, drawn when there is neither a logo nor a recognised icon. */
  letters: string;
  /** The project's own colour, or null for one that has not chosen one. */
  color?: string | null;
  /** A slug from `PROJECT_ICONS`, or null. A slug this set does not recognise
      draws the letters instead of refusing to render - the set can grow or an
      install can be mid-upgrade without a project's mark going blank. */
  icon?: string | null;
  /** An uploaded logo. Takes precedence over both the icon and the letters. */
  logoUrl?: string | null;
  size?: ProjectMarkSize;
  /** The project's name, read by a screen reader and shown on hover - this
      mark has no text of its own to fall back on once a logo covers it. */
  title: string;
  className?: string;
}

/** The custom properties the mark paints from, mirroring `statusVars` - unset
    when there is no colour, so the stylesheet's own `--muted` fallback applies. */
function markVars(color: string | null | undefined): Record<string, string> {
  if (!isHexColor(color)) return {};
  const safe = safeColor(color);
  return { '--mark-color': safe, '--mark-ink': contrastInk(safe) };
}

/**
 * A project's identity, drawn the same way everywhere one is shown: its own
 * uploaded logo if it has one, else the icon it picked from the closed stock
 * set, else its key's letters on a ground of its own colour.
 *
 * The ground is the project's colour, or `--muted` for a project that has not
 * chosen one; the ink on it - for the letters and for the icon, which draws in
 * `currentColor` - is `contrastInk`, the same arithmetic a status column uses
 * to pick white or black for the name inside its own pill.
 */
export function ProjectMark({ letters, color, icon, logoUrl, size = 'md', title, className }: ProjectMarkProps) {
  const classes = ['hatch-project-mark', `hatch-project-mark--${size}`];
  if (className) classes.push(className);

  const stockIcon = icon ? PROJECT_ICONS_BY_SLUG[icon] : undefined;

  return (
    <span className={classes.join(' ')} style={markVars(color)} role="img" aria-label={title} title={title}>
      {logoUrl ? (
        <img className="hatch-project-mark-logo" src={logoUrl} alt="" />
      ) : stockIcon ? (
        <stockIcon.Icon className="hatch-project-mark-icon" />
      ) : (
        <span className="hatch-project-mark-letters" aria-hidden="true">
          {letters}
        </span>
      )}
    </span>
  );
}
