import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Button, Card, Field, PageHeader } from '@hatch/ui';
import { getBackup, getHatchSettings, putHatchSettings, runBackup } from '../api/client';
import { Command } from '../components/Command';
import { backupState, canRunNow, errorWords, folderWords, keptWords, lastSuccessWords, OFF_WORDS } from '../lib/backup';
import { LIFETIME_CHOICES, readLifetime, writeLifetime } from '../lib/confirmationLifetime';
import type { Lifetime } from '../lib/confirmationLifetime';
import { message } from '../lib/errors';
import { credentialClear, credentialIsSet, credentialPlaceholder, credentialSave, nameSave } from '../lib/hatchSettings';
import type { CredentialField } from '../lib/hatchSettings';
import { announceLocalPersonChanged } from '../lib/localPerson';
import { useAttentionContext } from '../lib/useAttentionContext';
import { useLoaded } from '../lib/useLoaded';
import type { BackupStatus, HatchSettings, HatchSettingsWriteRequest } from '../types';

/** One agent's credential card: what it is called, and how an operator gets one. */
const AGENT_CREDENTIALS: {
  field: CredentialField;
  title: string;
  /** What the credential is called on the buttons: "Save token", "Remove API key". */
  noun: string;
  label: string;
  hint: ReactNode;
  steps: ReactNode;
}[] = [
  {
    field: 'claudeSubscriptionToken',
    title: 'Claude subscription token',
    noun: 'token',
    label: 'Token',
    hint: "An OAuth token for your own Claude subscription. It is what a container runner's entrypoint authenticates the claude CLI it starts with - an install whose runners all run on somebody's own machine needs none. Stored obfuscated, and never read back.",
    steps: (
      <>
        <li>
          Install the{' '}
          <a href="https://docs.claude.com/en/docs/claude-code/setup" target="_blank" rel="noreferrer">
            claude CLI
          </a>{' '}
          if it isn&rsquo;t already there, and make sure it&rsquo;s logged into the claude.ai
          account whose subscription you want tracked.
        </li>
        <li>
          Run <Command command="claude setup-token" /> and authorize it in the browser prompt
          that opens.
        </li>
        <li>
          It prints a token starting with <code>sk-ant-oat-</code> — copy it.
        </li>
        <li>Paste it into the Token field below and click Save token.</li>
      </>
    ),
  },
  {
    field: 'codexApiKey',
    title: 'Codex API key',
    noun: 'API key',
    label: 'API key',
    hint: (
      <>
        It is what a container runner exports as <code>CODEX_API_KEY</code> for <code>codex exec</code>,
        the only mode that reads that variable. A ChatGPT-subscription login cannot be passed this way,
        since its <code>auth.json</code> is a file rather than one variable, and the models an API key
        reaches can differ from a ChatGPT login&rsquo;s. An install whose runners all run on
        somebody&rsquo;s own machine needs none. Stored obfuscated, and never read back.
      </>
    ),
    steps: (
      <>
        <li>
          Create an API key in the{' '}
          <a href="https://platform.openai.com/api-keys" target="_blank" rel="noreferrer">
            OpenAI platform dashboard
          </a>
          .
        </li>
        <li>Copy it — the dashboard shows it once.</li>
        <li>Paste it into the API key field below and click Save API key.</li>
      </>
    ),
  },
  {
    field: 'xaiApiKey',
    title: 'Grok Build API key',
    noun: 'API key',
    label: 'API key',
    hint: (
      <>
        It is what a container runner exports as <code>XAI_API_KEY</code>. <code>grok login</code> is for
        interactive machines and does not travel to a container. An install whose runners all run on
        somebody&rsquo;s own machine needs none. Stored obfuscated, and never read back.
      </>
    ),
    steps: (
      <>
        <li>
          Create an API key in the{' '}
          <a href="https://console.x.ai" target="_blank" rel="noreferrer">
            xAI console
          </a>
          .
        </li>
        <li>Copy it — the console shows it once.</li>
        <li>Paste it into the API key field below and click Save API key.</li>
      </>
    ),
  },
];

/**
 * The things a Hatch install of its own configures - a credential per agent, and
 * a name - and the one thing this browser does.
 *
 * Here rather than on the admin app's Settings page because an install with no
 * admin app - which is every install that is only somebody's tracker - still
 * has to be able to set them. They are the same site settings under the same
 * keys, so a token set on the old page is already set on this one.
 *
 * The credentials are one table, one card per supported agent, because they
 * follow the same rules - each is never read back, saved only when non-blank,
 * and removed by its own button. The name is written out beside them: it
 * behaves differently, since it is a name that always is read back. The rules
 * each follows are in lib/hatchSettings.ts, which is where they are tested.
 *
 * The Backups card reads `getBackup` on its own load, so neither it nor the
 * settings failing to load hides the other, and lib/backup.ts owns every
 * sentence it says.
 *
 * The last card is neither: how long a confirmation stays in the corner is
 * remembered by this browser, like the theme, and never sent anywhere. So it
 * is drawn outside everything the server says - the wall being up, or the
 * settings failing to load, takes nothing from it.
 */
export function SettingsPage() {
  const { data: settings, setData, error, setError } = useLoaded<HatchSettings>(getHatchSettings);
  const [drafts, setDrafts] = useState<Record<CredentialField, string>>({
    claudeSubscriptionToken: '',
    codexApiKey: '',
    xaiApiKey: '',
  });
  const [name, setName] = useState<string | null>(null);
  // Which button is mid-request, so one card's Save does not spin the other's.
  const [busy, setBusy] = useState<CredentialField | 'name' | null>(null);
  const [lifetime, setLifetime] = useState<Lifetime>(readLifetime);
  // Its own load, not `settings`: the card is drawn whichever of the two fails.
  const {
    data: backup,
    setData: setBackup,
    error: backupError,
    reload: reloadBackup,
  } = useLoaded<BackupStatus>(getBackup);
  const { reload: reloadAttention } = useAttentionContext();
  const [backupRunning, setBackupRunning] = useState(false);
  const [backupRunError, setBackupRunError] = useState<string | null>(null);

  async function backUpNow() {
    setBackupRunning(true);
    try {
      // A failed run is a 200 with lastError set, so it shows through errorWords;
      // only a refusal throws. setData also drops any read still in flight.
      setBackup(await runBackup());
      setBackupRunError(null);
      // So the badge drops at once rather than a poll later.
      void reloadAttention();
    } catch (err) {
      setBackupRunError(message(err));
      // A 409 means a run is finishing: show the card's truth, not the refusal's.
      void reloadBackup();
    } finally {
      setBackupRunning(false);
    }
  }

  async function save(what: CredentialField | 'name', request: HatchSettingsWriteRequest) {
    setBusy(what);
    try {
      // The response is the settings as they now read, so there is nothing to
      // reload - and that card's input is emptied rather than filled with what
      // came back, which is dots.
      setData(await putHatchSettings(request));
      if (what !== 'name') setDrafts((d) => ({ ...d, [what]: '' }));
      setError(null);
      // The bar is mounted alongside this page and a route change here is
      // not a page load, so it is told rather than left to find out.
      if (what === 'name') announceLocalPersonChanged();
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="hatch-page">
      <PageHeader
        title="Settings"
        description="What this installation of Hatch knows about the account it runs on and the person it runs for, and how this browser behaves."
      />

      {error && <p className="text-danger">{error}</p>}

      {settings &&
        AGENT_CREDENTIALS.map((c) => (
          <Card key={c.field}>
            <h2 className="hatch-section-title">{c.title}</h2>
            <ol className="hatch-runner-needs">{c.steps}</ol>
            <Field label={c.label} hint={c.hint}>
              <input
                type="password"
                autoComplete="off"
                value={drafts[c.field]}
                placeholder={credentialPlaceholder(settings, c.field)}
                onChange={(e) => setDrafts((d) => ({ ...d, [c.field]: e.target.value }))}
              />
            </Field>
            <div className="hatch-form-actions">
              <Button
                variant="primary"
                loading={busy === c.field}
                disabled={busy !== null || credentialSave(c.field, drafts[c.field]) === null}
                onClick={() => void save(c.field, credentialSave(c.field, drafts[c.field])!)}
              >
                Save {c.noun}
              </Button>
              {/* Its own button rather than "save a blank field", so a credential
                  cannot be removed by tabbing through a form and pressing Save. */}
              <Button
                variant="danger"
                disabled={busy !== null || !credentialIsSet(settings, c.field)}
                onClick={() => void save(c.field, credentialClear(c.field))}
              >
                Remove {c.noun}
              </Button>
            </div>
          </Card>
        ))}

      <p className="text-muted">
        An <code>ollama</code> runner needs no credential here: it is configured on the runner
        itself, by <code>HATCH_OLLAMA_HOST</code>.
      </p>

      {/* Absent wherever the wall is up, which is every cluster install: there
          a person is a person because they enrolled, and their name is already
          on everything they write. A box that could not change that would be a
          box that lied. */}
      {settings?.localPersonNameApplies && (
        <Card>
          <h2 className="hatch-section-title">Your name</h2>
          <Field
            label="Name"
            hint="What Hatch signs everything this browser does with - every comment, every move, every event. Leave it empty and Hatch uses a default and says so in the nav."
          >
            <input
              value={name ?? settings.localPersonName}
              placeholder="Nobody has said"
              onChange={(e) => setName(e.target.value)}
            />
          </Field>
          <div className="hatch-form-actions">
            <Button
              variant="primary"
              loading={busy === 'name'}
              disabled={busy !== null || name === null || name.trim() === settings.localPersonName}
              onClick={() => void save('name', nameSave(name ?? '')).then(() => setName(null))}
            >
              Save name
            </Button>
          </div>
        </Card>
      )}

      <Card>
        <h2 className="hatch-section-title">Backups</h2>
        {backupError && <p className="text-danger">{backupError}</p>}
        {backup && backupState(backup) === 'off' && (
          <p>
            {OFF_WORDS} <Link to="/docs/backup-restore-and-replication">How to set one</Link>
          </p>
        )}
        {backup && backupState(backup) !== 'off' && (
          <>
            <p>{lastSuccessWords(backup)}</p>
            {errorWords(backup) !== null && <p className="text-danger">{errorWords(backup)}</p>}
            {backupState(backup) === 'kept' && <p>{keptWords(backup)}</p>}
            <p>{folderWords(backup)}</p>
          </>
        )}
        {backup?.configured && (
          <>
            {backupRunError && <p className="text-danger">{backupRunError}</p>}
            <div className="hatch-form-actions">
              <Button
                variant="primary"
                loading={backupRunning}
                disabled={!canRunNow(backup, backupRunning)}
                onClick={() => void backUpNow()}
              >
                Back up now
              </Button>
            </div>
          </>
        )}
      </Card>

      <Card>
        <h2 className="hatch-section-title">Confirmations</h2>
        <Field
          label="Close after"
          hint="How long a chicklet stays in the bottom-left corner after an issue is filed or a card is moved. It waits while the pointer or keyboard focus is on it, while a dialog is open, and while the tab is hidden. Remembered by this browser only, and it takes effect for the next one."
        >
          <select
            value={choiceOf(lifetime)}
            onChange={(e) => {
              const chosen = LIFETIME_CHOICES.find((c) => choiceOf(c.value) === e.target.value)?.value ?? null;
              setLifetime(chosen);
              writeLifetime(chosen);
            }}
          >
            {LIFETIME_CHOICES.map((c) => (
              <option key={choiceOf(c.value)} value={choiceOf(c.value)}>
                {c.label}
              </option>
            ))}
          </select>
        </Field>
      </Card>
    </div>
  );
}

/** An option's value: a select's values are strings, and Never is null. */
const choiceOf = (value: Lifetime): string => (value === null ? 'never' : String(value));
