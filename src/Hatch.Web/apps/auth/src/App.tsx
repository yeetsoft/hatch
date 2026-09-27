import { useEffect, useState } from 'react';
import { Button, Card, Text } from '@hatch/ui';
import { viewFor, errorMessage, type AuthView, type MeBody } from './lib/authState';
import { DEFAULT_LANDING, safeReturnTo, signInHref } from './lib/returnTo';

const params = new URLSearchParams(location.search);
const returnTo = params.get('r');
const error = errorMessage(params.get('error'));

async function whoAmI(): Promise<AuthView> {
  try {
    const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
    const body: MeBody | null = res.ok ? await res.json() : null;
    return viewFor(res.status, body);
  } catch {
    return { kind: 'error' };
  }
}

export function App() {
  const [view, setView] = useState<AuthView>({ kind: 'loading' });
  const [signingOut, setSigningOut] = useState(false);

  useEffect(() => {
    let live = true;
    void whoAmI().then((v) => {
      if (!live) return;
      if (v.kind === 'redirect') location.replace(safeReturnTo(returnTo) ?? DEFAULT_LANDING);
      setView(v);
    });
    return () => {
      live = false;
    };
  }, []);

  async function signOut() {
    setSigningOut(true);
    try {
      await fetch('/api/auth/sign-out', { method: 'POST', credentials: 'same-origin' });
    } finally {
      location.reload();
    }
  }

  return (
    <div className="auth-shell">
      <Card as="main" className="auth-card">
        <img className="auth-mark" src="/apps/auth/favicon.svg" alt="" />
        <h1>Hatch</h1>
        {view.kind === 'signed-out' && (
          <>
            {error && <Text tone="danger" role="alert">{error}</Text>}
            <Button as="a" variant="primary" href={signInHref(returnTo)}>
              Sign in with Google
            </Button>
          </>
        )}
        {view.kind === 'pending' && (
          <>
            <Text>
              {view.name ? <>You&rsquo;re signed in as <strong>{view.name}</strong>.</> : <>You&rsquo;re signed in.</>}{' '}
              An administrator has to approve your account before you can see the board.
            </Text>
            <Button loading={signingOut} onClick={() => void signOut()}>
              Sign out
            </Button>
          </>
        )}
        {view.kind === 'error' && (
          <>
            <Text>Couldn&rsquo;t reach Hatch.</Text>
            <Button onClick={() => location.reload()}>Reload</Button>
          </>
        )}
      </Card>
    </div>
  );
}
