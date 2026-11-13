# node: the newest release of one Node.js major from nodejs.org, checked against that release's SHASUMS256.txt and unpacked into /usr/local. A tarball rather than apt, which ships an older major.
ARG NODE_VERSION=22
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      amd64) arch=x64 ;; \
      arm64) arch=arm64 ;; \
      *) echo "hatch recipe node: no build for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && base=https://nodejs.org/dist/latest-v${NODE_VERSION%%.*}.x \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 "$base/SHASUMS256.txt" -o /tmp/SHASUMS256.txt \
 && file=$(awk -v arch="$arch" '$2 ~ "^node-v[0-9.]+-linux-" arch "\\.tar\\.gz$" {print $2}' /tmp/SHASUMS256.txt) \
 && [ -n "$file" ] \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 "$base/$file" -o "/tmp/$file" \
 && (cd /tmp && grep " $file\$" SHASUMS256.txt | sha256sum -c -) \
 && tar -xzf "/tmp/$file" -C /usr/local --strip-components=1 --no-same-owner \
 && rm -f "/tmp/$file" /tmp/SHASUMS256.txt /usr/local/CHANGELOG.md /usr/local/LICENSE /usr/local/README.md
