import { Link } from 'react-router-dom';
import type { StatusLine } from '../lib/goToWork';

/**
 * The line beside the Go to Work button, said from `statusLine`: its words, the
 * ticket it names as a link, and how many more of the viewer's loops it leaves
 * out. Nothing at all for the empty line, which is the runners still loading.
 */
export function GoToWorkStatus({ status }: { status: StatusLine }) {
  if (status.before === '' && !status.key) return null;

  return (
    <span className={`hatch-go-to-work-status hatch-go-to-work-status--${status.tone}`} role="status">
      {status.before}
      {status.key && <Link to={`/issues/${status.key}`}>{status.key}</Link>}
      {status.after}
      {status.more > 0 && ` and ${status.more} more`}
    </span>
  );
}
