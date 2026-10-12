# The runner, in a container, beside the stack it works for.
#
# What is in here is the whole of what the container can do, and the ticket
# says so out loud: git, the claude CLI, and the hatch binary. It can plan,
# break down, analyse and implement in any repository; it cannot build or test
# one whose toolchain this image does not happen to carry, and no generic image
# carries everybody's. The base-OS runner the Runner page hands out is the
# primary path for anything else - see src/Hatch.Web/apps/hatch/public/hatch-at-home.md.
#
# Build context is the repository root, like src/Hatch.Api/Dockerfile.api and
# unlike the three containers/ images beside this one: this needs
# src/Hatch.Cli, which is not under this directory.

# --platform=$BUILDPLATFORM pins this stage to whatever is doing the building,
# and the RID below is what makes it produce the *target's* binary anyway. The
# point is that the expensive half of this image - a restore, a trimmed publish
# - never runs under emulation: a friend on an Apple Silicon Mac and a friend
# on an Intel one each get a native image, and the arm64 half of a CI build
# costs no more than the x64 half.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . ./

# One RID per image, not Dockerfile.api's four: that image hands binaries out
# to whoever is at the browser, and this one is only ever run by itself. The
# flags are the Makefile's publish-hatch flags - self-contained and trimmed, so
# the runtime image needs no .NET in it.
#
# SourceRevisionId puts the git identity in the binary rather than in an
# environment variable the deployment could get wrong, for the reason
# Dockerfile.api gives at greater length.
ARG TARGETARCH
ARG HATCH_REVISION
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "hatch-runner: no .NET RID for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && dotnet publish src/Hatch.Cli/Hatch.Cli.csproj \
      --runtime $rid \
      --configuration Release \
      --output /out \
      -p:PublishSingleFile=true \
      -p:SelfContained=true \
      -p:PublishTrimmed=true \
      -p:SourceRevisionId=$HATCH_REVISION

# Debian-family, and that is load-bearing: the publish above targets glibc, so
# an Alpine (musl) base would produce an image whose binary cannot start. This
# is the same base mcr.microsoft.com/dotnet/aspnet:10.0 is built on, carrying
# the native dependencies a self-contained .NET binary needs and no runtime it
# does not.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

# curl and ca-certificates are the claude installer's own prerequisites; git is
# what an increment branches, commits and pushes with; libstdc++6 is what the
# CLI itself is linked against.
#
# openssh-client is named explicitly because --no-install-recommends is on:
# git only *recommends* it, so without this line a `git@...` remote fails with
# "ssh: not found" - and an SSH key or a forwarded agent is one of the three
# push credentials this container is documented to take.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      git \
      libstdc++6 \
      openssh-client \
 && rm -rf /var/lib/apt/lists/*

# gh (src/Hatch.Cli/Forge.cs) from GitHub's own apt repo, not Debian's: Debian
# ships gh too, but on its own release cadence, trailing GitHub's by whatever
# margin that distribution happens to be at. The key is already a binary
# OpenPGP key, not an ASCII-armored one, so there is no `gpg --dearmor` step
# here the way there would be for a key distributed armored.
#
# Pinned to major version 2 rather than installed as a bare `gh`: a later
# build of this image should not silently pick up a new major version with
# different flags or output `src/Hatch.Cli/Forge.cs` was not written against.
# `apt-cache madison gh` is what resolves the pin to a concrete, installable
# version string - `apt-get install gh=2.*` is not something apt can expand
# itself. Note that `madison` lists more than this one repo: Ubuntu's own
# universe repos carry a same-named `gh` package on an independent version
# number, also currently a `2.*`, so the filter below is relying on GitHub's
# own releases staying numerically ahead of Ubuntu's backport, not on telling
# the two repos apart by identity.
RUN mkdir -p -m 755 /etc/apt/keyrings \
 && curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      -o /etc/apt/keyrings/githubcli-archive-keyring.gpg \
 && chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
 && apt-get update \
 && gh_version=$(apt-cache madison gh | awk '{print $3}' | grep '^2\.' | sort -V | tail -1) \
 && apt-get install -y --no-install-recommends gh=$gh_version \
 && rm -rf /var/lib/apt/lists/*

# The native standalone installer rather than the npm one: there is no Node in
# this image and adding one to install a program that does not need it would
# double the image to buy nothing. `stable` rather than `latest` - an image
# built on any given day should carry the version Anthropic is telling people
# to run, not whatever landed that morning.
RUN curl -fsSL https://claude.ai/install.sh | bash -s stable
ENV PATH="/root/.local/bin:${PATH}"

# The CLI refuses `--permission-mode bypassPermissions` as uid 0 unless it is
# told it is in a sandbox - and an increment runs in print mode, where nothing
# can answer a permission prompt, so bypass is the only mode that works
# unattended (see ClaudeSessionRunner). This is that flag, set honestly: a
# container with one bind-mounted checkout in it is the isolation the guard is
# asking after. Root rather than a made-up user because the checkout arrives as
# a bind mount from the host, and a container user whose uid does not match the
# files cannot write to the branch it is supposed to push.
ENV IS_SANDBOX=1

COPY --from=build /out/hatch /usr/local/bin/hatch
COPY containers/hatch-runner/git-askpass.sh /usr/local/bin/git-askpass.sh
COPY containers/hatch-runner/entrypoint.sh /entrypoint.sh
RUN chmod +x /usr/local/bin/hatch /usr/local/bin/git-askpass.sh /entrypoint.sh

# Where a checkout is expected to be mounted. Named here as well as in the
# compose file so `docker run -v "$PWD:/checkout"` needs no second flag.
ENV HATCH_ROOT=/checkout

# Where the loop clones what the board binds that it has no checkout of - a
# second way in beside the mount above, for a container with nothing bind
# mounted at all. Named here for the same reason HATCH_ROOT is.
ENV HATCH_WORKSPACE=/workspace

ENTRYPOINT ["/entrypoint.sh"]
