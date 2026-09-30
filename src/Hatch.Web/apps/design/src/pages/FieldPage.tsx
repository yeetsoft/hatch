import { useState } from 'react';
import { Button, Card, Field, Grid, Text } from '@hatch/ui';
import { GalleryPage, GallerySection } from '../components/Gallery';

/* The inputs on this page are real and uncontrolled — type in them. The
   controls themselves are the app's own <input>/<select>/<textarea>, styled by
   @hatch/ui/base.css, which is why base.css is a package entry point at all: a
   Field specimen rendering a naked browser input would look nothing like a
   Field in admin. */

export function FieldPage() {
  const [port, setPort] = useState('99999');
  const portError = Number(port) > 65535 ? 'A port is at most 65535.' : undefined;

  return (
    <GalleryPage
      title="Field"
      blurb="A labelled control — 105 sites in admin, the second-largest idiom in the house after toned text. The label element wraps the control, so clicking the label focuses it."
    >
      <GallerySection
        title="The controls"
        note="One component over any control at all. It sets the label and the stacking and nothing about the control itself, which is base.css's job. Hover a control that is neither focused nor disabled and its border darkens one step, the step a secondary button takes."
      >
        <Grid cols={3}>
          <Field label="Name">
            <input type="text" defaultValue="Living Room" />
          </Field>
          <Field label="Kind">
            <select defaultValue="Interior">
              <option>Interior</option>
              <option>Outside</option>
            </select>
          </Field>
          <Field label="Sort order">
            <input type="number" defaultValue={0} />
          </Field>
          <Field label="Notes">
            <textarea rows={3} defaultValue="Faces west; warms up after 16:00." />
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="On a coarse pointer"
        note="iOS Safari zooms the page on focusing an input under 16px — the jiggle a phone user does to get the page back after editing. --t-section (16px) is the floor; above it buys nothing, which is why the desk keeps --t-body (14px)."
      >
        <div className="compare">
          <div className="compare-half">
            <Field label="Desktop, --t-body">
              <input type="text" defaultValue="14px" />
            </Field>
          </div>
          <div className="compare-half">
            <Field label="Coarse pointer, --t-section">
              <input type="text" defaultValue="16px" style={{ fontSize: 'var(--t-section)' }} />
            </Field>
          </div>
        </div>
      </GallerySection>

      <GallerySection
        title="Input types"
        note="Every type base.css dresses looks the same: a typeless input (the browser's text), a search box, and a date and time pair. The search box keeps type=search for Escape and the browser's clear button, minus the native rounding. Placeholders are in the muted ink."
      >
        <Grid cols={2}>
          <Field label="Title">
            <input placeholder="No type attribute" />
          </Field>
          <Field label="Search">
            <input type="search" placeholder="Search titles, keys, parents…" />
          </Field>
          <Field label="Due date">
            <input type="date" />
          </Field>
          <Field label="Due time">
            <input type="time" disabled />
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="Beside a button"
        note="An input and a Button on one row stand the same height, to within a pixel: the input's border is paid for out of its padding."
      >
        <div style={{ display: 'flex', gap: 'var(--sp-2)', alignItems: 'center' }}>
          <input placeholder="Remote" style={{ flex: 1 }} />
          <Button>Add</Button>
        </div>
      </GallerySection>

      <GallerySection
        title="Click the label"
        note="It focuses the control. Admin's markup had a <label> that was associated with nothing — clicking it did nothing, and a screen reader read the input as unlabelled. The label wraps the control rather than pointing at its id, because the control is the app's element and this component cannot see its id."
      >
        <Grid cols={2}>
          <Field label="Try clicking this label">
            <input type="text" placeholder="…and the caret lands here" />
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="Hint, and error"
        note="Both sit under the control at the label register. The error replaces the hint while it is showing, because two lines under one control is two things to read. Both are outside the <label> on purpose: text inside it would join the control's accessible name."
      >
        <Grid cols={2}>
          <Field label="Poll interval" hint="Seconds. Below 5 the adapter rate-limits.">
            <input type="number" defaultValue={30} />
          </Field>
          <Field
            label="Port"
            hint="The port the adapter listens on."
            error={portError}
          >
            <input type="number" value={port} onChange={(e) => setPort(e.target.value)} />
          </Field>
        </Grid>
        <Text tone="muted">
          The port field is live: it is over 65535 right now. Bring it under and the error gives the hint back
          its line.
        </Text>
      </GallerySection>

      <GallerySection
        title="Disabled, and read-only"
        note="Two different facts. Disabled means you may not change this, and it is drawn at half opacity with a not-allowed cursor; read-only means this is not yours to change. A disabled control is skipped by the tab order, which is why a value someone still needs to copy should be read-only instead."
      >
        <Grid cols={2}>
          <Field label="Adapter" hint="Set when the device was discovered.">
            <input type="text" defaultValue="mdns-generic" disabled />
          </Field>
          <Field label="Device id">
            <input type="text" defaultValue="0f3a-9c21-4d88" readOnly />
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="A control that labels itself"
        note='as="div" for children carrying their own labelling — a checkbox with its own text, a radio group. A <label> inside a <label> is invalid HTML and its click target is undefined, so the wrapper steps aside.'
      >
        <Grid cols={2}>
          <Field label="Included" as="div">
            <label className="check-row">
              <input type="checkbox" defaultChecked />
              Show on the wall panel
            </label>
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="Long content"
        note="A long label wraps rather than truncating — a field whose name you cannot read is a field you cannot fill in. The control keeps the field's full width."
      >
        <Grid cols={3}>
          <Field
            label="Comfort high, in degrees Fahrenheit, measured at the zone's primary sensor"
            hint="Leave empty to inherit the household default."
          >
            <input type="number" placeholder="—" />
          </Field>
          <Field label="Name">
            <input type="text" defaultValue="A value long enough that the input has to scroll to show its end" />
          </Field>
        </Grid>
      </GallerySection>

      <GallerySection
        title="On a card"
        note="The ground fields actually sit on. An input is --card on --card, so the hairline is the only thing separating the control from the surface — check that edge in both themes."
      >
        <Card>
          <Grid cols={2}>
            <Field label="Display name">
              <input type="text" defaultValue="Household" />
            </Field>
            <Field label="Search">
              <input type="search" placeholder="Search…" />
            </Field>
            <Field label="Time zone">
              <select defaultValue="UTC">
                <option>UTC</option>
                <option>Local</option>
              </select>
            </Field>
          </Grid>
        </Card>
      </GallerySection>
    </GalleryPage>
  );
}
