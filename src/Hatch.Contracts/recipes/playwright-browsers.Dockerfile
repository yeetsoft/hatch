# playwright-browsers: Playwright's chromium and webkit with their system libraries, into /ms-playwright. The version must match the Microsoft.Playwright package the tests run, whose browser revisions it pins. Needs node, for npx.
ARG PLAYWRIGHT_VERSION=1.63.0
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN ok= ; for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && npx --yes playwright@$PLAYWRIGHT_VERSION install --with-deps chromium webkit; then ok=1; break; fi; \
      echo "playwright: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/* \
 && chmod -R a+rX /ms-playwright
