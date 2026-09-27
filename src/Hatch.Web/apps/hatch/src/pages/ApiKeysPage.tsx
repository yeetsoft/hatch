import { useState } from 'react';
import { Badge, Button, Card, EmptyState, Field, Modal, PageHeader, Table, Text } from '@hatch/ui';
import { getApiKeyScopes, getApiKeys, mintApiKey, revokeApiKey } from '../api/client';
import { Command } from '../components/Command';
import {
  dismissSecret,
  lastUsedLabel,
  mintProblem,
  partitionKeys,
  scopesLabel,
  showSecret,
  toggleScope,
  type SecretPanel,
} from '../lib/apiKeys';
import { message } from '../lib/errors';
import { useLoaded } from '../lib/useLoaded';
import { useMe } from '../lib/useMe';
import type { ApiKey } from '../types';

/**
 * An Admin's page over the credentials that are not browsers: mint one, see
 * every one, revoke one.
 *
 * The secret of a fresh mint is held in this component's state and nowhere
 * else - not storage, not the URL, not a log - so dismissing the panel or
 * reloading the tab is the end of it, which is what the panel says. The list
 * never carries one; the server does not send it.
 */
export function ApiKeysPage() {
  const { me, isAdmin } = useMe();
  const { data: keys, error, setError, reload } = useLoaded(getApiKeys);
  const { data: scopes } = useLoaded(getApiKeyScopes);

  const [name, setName] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [minting, setMinting] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [secret, setSecret] = useState<SecretPanel>(null);
  const [revoking, setRevoking] = useState<ApiKey | null>(null);
  const [busy, setBusy] = useState(false);

  if (me !== null && !isAdmin) return <p>Admins only.</p>;

  const problem = mintProblem(name);

  async function mint() {
    setMinting(true);
    try {
      setSecret(showSecret(await mintApiKey({ name: name.trim(), scopes: selected })));
      setName('');
      setSelected([]);
      setRefusal(null);
      await reload();
    } catch (err) {
      // The form is left as it was, so a duplicate name is one edit away from
      // working rather than a retype.
      setRefusal(message(err));
    } finally {
      setMinting(false);
    }
  }

  async function revoke(key: ApiKey) {
    setBusy(true);
    try {
      await revokeApiKey(key.id);
      setError(null);
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(false);
      setRevoking(null);
      await reload();
    }
  }

  const { live, revoked } = partitionKeys(keys ?? []);

  return (
    <div className="hatch-page">
      <PageHeader
        title="API keys"
        description="The credentials a runner or a script uses to reach this Hatch when its wall is up. A key cannot mint a key: this page is where they are handed out."
      />

      {error && <p className="text-danger">{error}</p>}

      {secret && (
        <Card>
          <h2 className="hatch-section-title">New key: {secret.key.name}</h2>
          <Command command={secret.secret} label="Copy" />
          <p className="text-muted">
            This is the only time this key is shown. Copy it now — it will not be shown again.
          </p>
          <div className="hatch-form-actions">
            <Button onClick={() => setSecret(dismissSecret())}>Done</Button>
          </div>
        </Card>
      )}

      <Card>
        <h2 className="hatch-section-title">Mint a key</h2>
        <Field label="Name" hint="What the audit trail will call it.">
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field
          label="Scopes"
          hint="What the key may reach. A key with none reaches nothing."
        >
          <div className="hatch-keys-scopes">
            {(scopes ?? []).map((scope) => (
              <label key={scope} className="hatch-keys-scope">
                <input
                  type="checkbox"
                  checked={selected.includes(scope)}
                  onChange={() => setSelected(toggleScope(selected, scope))}
                />
                {scope}
              </label>
            ))}
          </div>
        </Field>
        {refusal && <p className="text-danger">{refusal}</p>}
        <div className="hatch-form-actions">
          <Button variant="primary" loading={minting} disabled={problem !== null} onClick={() => void mint()}>
            Mint key
          </Button>
        </div>
      </Card>

      {keys?.length === 0 && <EmptyState message="No keys yet." />}

      {keys && keys.length > 0 && (
        <Card flush>
          <Table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Prefix</th>
                <th>Scopes</th>
                <th>Created</th>
                <th>Last used</th>
                <th>Revoked</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {[...live, ...revoked].map((key) => (
                <tr key={key.id} className={key.revokedAt ? 'hatch-keys-revoked' : undefined}>
                  <td>
                    <strong>{key.name}</strong> {key.revokedAt && <Badge>revoked</Badge>}
                  </td>
                  <td>
                    <code>{key.prefix}</code>
                  </td>
                  <td>{scopesLabel(key.scopes)}</td>
                  <td>{new Date(key.createdAt).toLocaleDateString()}</td>
                  <td>{lastUsedLabel(key.lastUsedAt)}</td>
                  <td>
                    {key.revokedAt ? new Date(key.revokedAt).toLocaleDateString() : <Text tone="muted">—</Text>}
                  </td>
                  <td>
                    {!key.revokedAt && (
                      <Button variant="danger" disabled={busy} onClick={() => setRevoking(key)}>
                        Revoke
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      <Modal open={revoking !== null} onClose={() => setRevoking(null)} title={`Revoke ${revoking?.name ?? ''}?`}>
        {revoking && (
          <div className="hatch-claim-clear">
            <p>
              Anything using this key is refused from the next call. The row stays, marked revoked, so the audit
              trail can still name it.
            </p>
            <div className="hatch-form-actions">
              <Button onClick={() => setRevoking(null)}>Leave it</Button>
              <Button variant="danger" loading={busy} onClick={() => void revoke(revoking)}>
                Revoke
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
