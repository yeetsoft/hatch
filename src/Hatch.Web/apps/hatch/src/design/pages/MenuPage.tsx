import { NavLink } from 'react-router-dom';
import { Menu, ThemeSwitch, TopBar } from '@hatch/ui';
import { GalleryPage, GallerySection } from '../components/Gallery';

/* Every menu here is the real component. The rows are real NavLinks into the
   gallery itself, so an active row and a row that closes its menu on activation
   can be seen rather than described. */

const LONG = Array.from({ length: 14 }, (_, i) => `Runner ${i + 1}`);

export function MenuPage() {
  return (
    <GalleryPage
      title="Menu"
      blurb="One trigger, one panel: opened on hover, click or tap, and from the keyboard. The primary nav, the gear and the attention list all share this behaviour."
    >
      <GallerySection
        title="Closed and open"
        note="The same menu twice. The open one is forced open on load; every other menu opens the way a person would."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu label="Closed">
            <Menu.Item as={NavLink} to="/design/menu">Menu</Menu.Item>
            <Menu.Item as={NavLink} to="/design/color">Color</Menu.Item>
          </Menu>
          <Menu label="Open" defaultOpen>
            <Menu.Item as={NavLink} to="/design/menu">Menu (this page, so lit)</Menu.Item>
            <Menu.Item as={NavLink} to="/design/color">Color</Menu.Item>
            <Menu.Item as={NavLink} to="/design/type">Type</Menu.Item>
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="Hover versus click"
        note="Hovering opens after about 120ms, under a pointer that can hover. Moving to the next trigger switches at once, so two are never open; leaving closes after about 200ms, and crossing the gap to the panel does not count as leaving. A click on a trigger hover already opened just leaves it open, so it cannot fight the hover that showed it; a click inside the open delay opens it at once. In devtools touch emulation, hover does nothing and a tap opens, a second tap closes."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu.Bar>
            <Menu label="Foundations">
              <Menu.Item as={NavLink} to="/design/color">Color</Menu.Item>
              <Menu.Item as={NavLink} to="/design/type">Type</Menu.Item>
            </Menu>
            <Menu label="Components" active>
              <Menu.Item as={NavLink} to="/design/top-bar">Top bar</Menu.Item>
              <Menu.Item as={NavLink} to="/design/menu">Menu</Menu.Item>
            </Menu>
            <Menu label="Elsewhere">
              <Menu.Item href="/">All apps</Menu.Item>
            </Menu>
          </Menu.Bar>
        </div>
      </GallerySection>

      <GallerySection
        title="From the keyboard"
        note="Tab to a trigger. Enter, Space or ArrowDown opens the panel; Enter and ArrowDown also move focus to the first row. Tab walks the rows like any other content, and Tab past the last one closes the panel. Escape closes it from anywhere inside and returns focus to the trigger. It is not role=menu: the rows are links, so there is no arrow-key roving."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu label="Try the keyboard">
            <Menu.Item as={NavLink} to="/design/color">One</Menu.Item>
            <Menu.Item as={NavLink} to="/design/type">Two</Menu.Item>
            <Menu.Item as={NavLink} to="/design/spacing">Three</Menu.Item>
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="Start and end alignment"
        note="The panel lines up with the trigger's start or end edge and is never narrower than the trigger. Use end near the right edge of a page."
      >
        <div className="design-stage-page design-menu-stage design-menu-stage--spread">
          <Menu label="align start" align="start">
            <Menu.Item as={NavLink} to="/design/color">A rather long row label</Menu.Item>
          </Menu>
          <Menu label="align end" align="end">
            <Menu.Item as={NavLink} to="/design/color">A rather long row label</Menu.Item>
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="A long panel"
        note="The panel scrolls at 60% of the viewport height rather than running off the page."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu label="Runners">
            {LONG.map((name) => (
              <Menu.Item key={name} as={NavLink} to="/design/menu">{name}</Menu.Item>
            ))}
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="A row that is not a link"
        note="Any content goes in the panel. This is the gear's case: the theme control, which stays open while it is used because only a Menu.Item closes the menu."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu
            label="Settings"
            align="start"
            trigger={(props) => (
              <button {...props}>
                <span aria-hidden="true">⚙</span>
              </button>
            )}
          >
            <div className="design-menu-stage__row">
              <ThemeSwitch tone="surface" />
            </div>
            <Menu.Item href="/">All apps</Menu.Item>
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="Press only"
        note="hover={false}: resting on the trigger or the panel does nothing, and leaving an open panel does not close it. A click, Enter, Space or ArrowDown opens it; it stays open while its rows are used, and closes on Escape, an outside press or Tab past the end. For a panel that is worked in, such as a filter, which a passing pointer must not open."
      >
        <div className="design-stage-page design-menu-stage">
          <Menu label="Filter" hover={false}>
            <label className="design-menu-stage__check">
              <input type="checkbox" /> Backlog
            </label>
            <label className="design-menu-stage__check">
              <input type="checkbox" /> In progress
            </label>
            <label className="design-menu-stage__check">
              <input type="checkbox" /> In review
            </label>
          </Menu>
        </div>
      </GallerySection>

      <GallerySection
        title="Both tones, on their grounds"
        note="tone=surface on a page, tone=accent on the bar's fill, where the ink, the hover wash and the focus ring come from --on-chrome, --on-chrome-wash and --chrome-glow. The panel is a card either way. active lights the trigger."
      >
        <div className="design-stage">
          <TopBar
            appName="Hatch"
            trailing={
              <Menu.Bar>
                <Menu label="Plain" tone="accent">
                  <Menu.Item as={NavLink} to="/design/color">Color</Menu.Item>
                </Menu>
                <Menu label="Lit" tone="accent" active align="end">
                  <Menu.Item as={NavLink} to="/design/menu">Menu</Menu.Item>
                </Menu>
              </Menu.Bar>
            }
          />
          <div className="design-stage-page design-menu-stage">
            <Menu label="On a surface" active>
              <Menu.Item as={NavLink} to="/design/menu">Menu</Menu.Item>
            </Menu>
          </div>
        </div>
      </GallerySection>
    </GalleryPage>
  );
}
