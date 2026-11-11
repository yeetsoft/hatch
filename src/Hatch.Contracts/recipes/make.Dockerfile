# make: GNU make from apt, for a build that shells out to `make`.
RUN ok= ; for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends make; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/*
