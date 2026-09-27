import { Link } from 'react-router-dom';
import { signOut } from '../api/client';
import { describeMe } from '../lib/me';
import { SIGN_IN_PATH } from '../lib/signIn';
import { useMe } from '../lib/useMe';

/**
 * What to say when Hatch does not know who is sitting here.
 *
 * No longer says what to set, because there is now somewhere to press: the
 * rendered hint carries a link to the Settings page beside this sentence. It
 * stays a single exported constant because it is also the tooltip, and a
 * tooltip cannot hold a link - so the sentence has to read on its own.
 */
export const UNNAMED_HINT = 'Hatch does not know your name.';

/**
 * Who Hatch thinks is at this browser, in the nav strip - in both modes.
 *
 * The local person with the hint to name themselves when the wall is off; the
 * signed-in person with a Sign out when it is up. Draws nothing for a caller
 * that is nobody. What it says comes from `useMe`, read once by the provider
 * above the routes.
 */
export function NavLocalPerson() {
  const view = describeMe(useMe().me);

  if (view === null) return null;

  const signOutAndLeave = async () => {
    try {
      await signOut();
    } catch {
      // The grant may already be gone; sign in is the right place either way.
    }
    location.replace(SIGN_IN_PATH);
  };

  return (
    <span
      className={`hatch-local-person${view.showHint ? ' hatch-local-person-unnamed' : ''}`}
      title={view.showHint ? UNNAMED_HINT : undefined}
    >
      <span className="hatch-local-person-name">{view.name}</span>
      {/* The hint is drawn as well as being the tooltip: an operator who has
          just started Hatch for the first time is exactly the person who will
          not think to hover over their own name. */}
      {view.showHint ? (
        <span className="hatch-local-person-hint">
          {UNNAMED_HINT} <Link to="/settings">Set it</Link>
        </span>
      ) : null}
      {view.showSignOut ? (
        <button type="button" className="hatch-sign-out" onClick={() => void signOutAndLeave()}>
          Sign out
        </button>
      ) : null}
    </span>
  );
}
