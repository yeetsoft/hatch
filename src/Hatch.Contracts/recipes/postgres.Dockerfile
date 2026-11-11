# postgres: the server and client from PostgreSQL's own apt repo (PGDG), for one major, with its binaries linked onto PATH. The service is not started: a build that needs a database runs `pg_ctl` itself.
ARG POSTGRES_VERSION=17
RUN major=${POSTGRES_VERSION%%.*} \
 && mkdir -p -m 755 /etc/apt/keyrings \
 && . /etc/os-release \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 https://www.postgresql.org/media/keys/ACCC4CF8.asc \
      -o /etc/apt/keyrings/postgresql.asc \
 && chmod a+r /etc/apt/keyrings/postgresql.asc \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/postgresql.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" \
      > /etc/apt/sources.list.d/pgdg.list \
 && ok= \
 && for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends postgresql-$major postgresql-client-$major; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/* \
 && ln -s /usr/lib/postgresql/$major/bin/* /usr/local/bin/
