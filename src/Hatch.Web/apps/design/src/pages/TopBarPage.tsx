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

export function TopBarPage() {
  return (
    <GalleryPage
      title="Top bar"
      blurb="The bar every Hatch app wears: where you are, the logo that links home, and the theme. One row, 48px, and nothing else."
    >
      <GallerySection
        title="As it ships"
        note="What admin renders. The logo, the app name and the gear - nothing else. The bar says where you are, and the page below says what is on it. It replaces a 131px gradient header carrying a 32px title and a subtitle: 63% of the chrome above every admin page, given back to the page."
      >
        <div className="stage">
          <TopBar appName="Hatch Admin" />
          <div className="stage-page">
            <h3>Zones</h3>
            <p className="gallery-note">The page starts here.</p>
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
