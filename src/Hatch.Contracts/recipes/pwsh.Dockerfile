# pwsh: PowerShell from its GitHub release tarball into /opt/microsoft/powershell/<major>. A major means the newest release, which GitHub's releases/latest redirect names without a token; a full version names its own tag. pwsh does not need ICU, but `ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` would set it for every dotnet process in the final image, not just this one - breaking culture-aware code the image is built to run (HA-2283). A wrapper sets it for pwsh alone.
ARG PWSH_VERSION=7
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) arch=x64 ;; \
      arm64) arch=arm64 ;; \
      *) echo "hatch recipe pwsh: no build for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && major=${PWSH_VERSION%%.*} \
 && case "$PWSH_VERSION" in \
      *.*.*) version=$PWSH_VERSION ;; \
      *.*) echo "hatch recipe pwsh: declare a major (7) or a full version (7.4.6), not $PWSH_VERSION" >&2; exit 1 ;; \
      *) tag=$(curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 -o /dev/null -w '%{url_effective}' https://github.com/PowerShell/PowerShell/releases/latest) \
         && version=${tag##*/v} ;; \
    esac \
 && [ "${version%%.*}" = "$major" ] || { echo "hatch recipe pwsh: the newest release is $version, not a $major.x" >&2; exit 1; } \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 "https://github.com/PowerShell/PowerShell/releases/download/v$version/powershell-$version-linux-$arch.tar.gz" -o /tmp/pwsh.tar.gz \
 && mkdir -p /opt/microsoft/powershell/$major \
 && tar -xzf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/$major --no-same-owner \
 && rm /tmp/pwsh.tar.gz \
 && chmod +x /opt/microsoft/powershell/$major/pwsh \
 && printf '#!/bin/sh\nexec env DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 "/opt/microsoft/powershell/%s/pwsh" "$@"\n' "$major" > /usr/local/bin/pwsh \
 && chmod +x /usr/local/bin/pwsh
