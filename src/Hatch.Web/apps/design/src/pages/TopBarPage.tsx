import { NavLink } from 'react-router-dom';
import { Badge, Button, Menu, TopBar } from '@hatch/ui';
import { GalleryPage, GallerySection } from '../components/Gallery';

/* Every bar on this page is the real component, wired to the real theme: click
   a specimen's Light and the whole gallery goes light, because there is one
   theme and this is its control. The app-switcher links really do leave for the
   picker, for the same reason - a specimen that mocks the behaviour is a
   specimen that can be wrong about it.

   The <header> each <TopBar> renders is nested inside this page's <article>,
   so none of them is a second banner landmark. Only the app's own bar, above
   the page, is one. */

/* Board and Plan, drawn the way PrimaryNav.tsx draws them: a plain link
   wearing the same accent trigger a Menu draws for itself, so the two read as
   one row of controls. Restated here rather than imported, because this page
   is the design system's own and must not depend on the hatch app's. */
const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `hatch-menu__trigger hatch-menu__trigger--accent${isActive ? ' hatch-menu__trigger--active' : ''}`;

export function TopBarPage() {
  return (
    <GalleryPage
      title="Top bar"
      blurb="The bar every Hatch app wears: where you are, the logo that links home, and the theme. One row, 48px, and nothing else."
    >
      <GallerySection
        title="As it ships"
        note="What the hatch app renders: PrimaryNav in leading, wearing the same accent-tone triggers the gear itself does, and the session cluster - the battery and the attention control, stood in for here with a Badge - in trailing. It passes width full, below, so the bar reaches the same edges the board under it does; every other app leaves the default and gets a centred, --measure-capped bar instead. Board is forced active here (the page this specimen actually renders on is not `/color`), Plan sits at rest, and Agents is forced open with defaultOpen, so a resting link, the active link and an open menu are all on the bar at once. Hover any of them to see the hover paint live."
      >
        <div className="stage">
          <TopBar
            appName="Hatch"
            width="full"
            leading={
              <nav aria-label="Primary">
                <Menu.Bar>
                  <NavLink
                    to="/color"
                    end
                    className="hatch-menu__trigger hatch-menu__trigger--accent hatch-menu__trigger--active"
                  >
                    Board
                  </NavLink>
                  <NavLink to="/typography" className={navLinkClass}>Plan</NavLink>
                  <Menu label="Agents" tone="accent" defaultOpen>
                    <Menu.Item href="/">Runners</Menu.Item>
                    <Menu.Item href="/">Playbooks</Menu.Item>
                  </Menu>
                  <Menu label="Manage" tone="accent">
                    <Menu.Item href="/">Projects</Menu.Item>
                    <Menu.Item href="/">Statuses</Menu.Item>
                  </Menu>
                </Menu.Bar>
              </nav>
            }
            trailing={<Badge tone="primary">3 waiting</Badge>}
            menu={
              <>
                <Menu.Item href="/">Settings</Menu.Item>
                <Menu.Item href="/">Docs</Menu.Item>
              </>
            }
          />
          <div className="stage-page menu-stage">
            <h3>Board</h3>
            <p className="gallery-note">The page starts here.</p>
          </div>
        </div>
      </GallerySection>

      <GallerySection
        title="Width"
        note="`measure` caps the bar at --measure and centres it, the default every app but hatch leaves alone. `full` drops the cap and runs it to the box it sits in - the window, in a real app. This stage narrows --measure on itself, gallery furniture rather than a real width, so the two are visibly different without resizing the window."
      >
        <div className="stage stage--narrow-measure">
          <TopBar appName="Hatch" />
          <div className="stage-page">
            <p className="gallery-note">width=&quot;measure&quot;, the default: centred, capped.</p>
          </div>
        </div>
        <div className="stage stage--narrow-measure">
          <TopBar appName="Hatch" width="full" />
          <div className="stage-page">
            <p className="gallery-note">width=&quot;full&quot;: runs to the stage's own edges.</p>
          </div>
        </div>
      </GallerySection>

      <GallerySection
        title="Slots"
        note="An app adds to the bar rather than forking it: the leading slot sits after the logo and name, the trailing slot before the gear. The gear itself takes a menu slot, for whatever the app wants behind it beside Theme. The gear is always last, so it is in the same place in every app."
      >
        <div className="stage">
          <TopBar
            appName="Hatch Docs"
            leading={<Badge tone="primary">Staging</Badge>}
            trailing={<Button>Sign out</Button>}
          />
        </div>
      </GallerySection>

      <GallerySection
        title="A narrow window, and a long name"
        note="The name gives way first: it ellipses, while the logo and the gear keep their size. A target that shrank to make room for a title would be the wrong thing to shrink."
      >
        <div className="stage stage--narrow">
          <TopBar appName="Hatch Provisioning Console" />
        </div>
      </GallerySection>

      <GallerySection
        title="The gear, open"
        note="Pressing the gear opens Theme first, with the current choice checked. No menu passed, no divider and nothing below it."
      >
        <div className="stage">
          <TopBar appName="Hatch Admin" menuDefaultOpen />
        </div>
      </GallerySection>

      <GallerySection
        title="The gear, with app items"
        note="A divider, then whatever the app passes as menu - here, two rows a real app's Settings and Docs would occupy."
      >
        <div className="stage">
          <TopBar
            appName="Hatch"
            menuDefaultOpen
            menu={
              <>
                <Menu.Item as={NavLink} to="/color">Settings</Menu.Item>
                <Menu.Item href="/">Docs</Menu.Item>
              </>
            }
          />
        </div>
      </GallerySection>
    </GalleryPage>
  );
}
