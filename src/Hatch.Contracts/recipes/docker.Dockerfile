# docker: the client only (docker-ce-cli and the compose plugin) from Docker's own apt repo. No daemon: a container uses the host's socket.
RUN mkdir -p -m 755 /etc/apt/keyrings \
 && . /etc/os-release \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 "https://download.docker.com/linux/$ID/gpg" \
      -o /etc/apt/keyrings/docker.asc \
 && chmod a+r /etc/apt/keyrings/docker.asc \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/$ID $VERSION_CODENAME stable" \
      > /etc/apt/sources.list.d/docker.list \
 && for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/*
