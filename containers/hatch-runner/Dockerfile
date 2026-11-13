# The runner, in a container, beside the stack it works for.
#
# What is in here is the whole of what the container can do, and the ticket
# says so out loud: git, the claude, codex and grok CLIs, and the hatch binary -
# one image, and HATCH_AGENT says which of the three the loop spawns. It can plan,
# break down, analyse and implement in any repository; it cannot build or test
# one whose toolchain this image does not happen to carry, and no generic image
# carries everybody's. The base-OS runner the Runner page hands out is the
# primary path for anything else - see the Docs page "The container runner"
# (src/Hatch.Web/apps/hatch/docs/the-container-runner.md#whether-it-is-for-you).
#
# The contract a derived image may rely on
#
# A `FROM hatch-runner` image is an operator's own toolchain on top of this one,
# and what follows is what it can count on staying put:
#
#   - A Debian-family base. The hatch binary is a glibc build, so an Alpine
#     (musl) base would leave an image whose binary cannot start.
#   - The hatch binary at /usr/local/bin/hatch, because the entrypoint, the
#     loop and every script that shells out to it look it up there.
#   - /entrypoint.sh as the ENTRYPOINT. It is what turns the container's
#     environment into a running loop, and an image that replaces it has
#     stopped being a runner.
#   - Root as the user, with no USER instruction. A bind-mounted checkout is
#     owned by the host's uid, and a container user whose uid does not match
#     cannot write to the branch it is supposed to push.
#   - IS_SANDBOX=1, which is what lets the claude CLI run in bypass mode as
#     uid 0 in an unattended increment; the reasoning is at the ENV below.
#   - HATCH_ROOT=/checkout and HATCH_WORKSPACE=/workspace, where the loop looks
#     for a mounted checkout and where it clones what it has no checkout of.
#   - /root/.local/bin and /root/.grok/bin on PATH, where the claude and grok
#     installers put their binaries.
#   - /etc/hatch/, reserved for what an image bakes in about itself. It exists
#     and is empty here; a derived image may put files in it and may not use
#     it for anything else.
#
# A derived image adds packages and changes none of that. One thing to know
# when it does: apt's lists are removed at the end of each layer here, to keep
# the image small, so a derived image runs `apt-get update` itself before its
# first `apt-get install`.
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

# curl and ca-certificates are the claude, codex and grok installers' own
# prerequisites; git is
# what an increment branches, commits and pushes with; libstdc++6 is what the
# CLI itself is linked against.
#
# openssh-client is named explicitly because --no-install-recommends is on:
# git only *recommends* it, so without this line a `git@...` remote fails with
# "ssh: not found" - and an SSH key or a forwarded agent is one of the three
# push credentials this container is documented to take.
#
# bubblewrap is codex's own sandbox helper. Without it, `codex exec
# --approve-for-me` (workspace-write) still starts as root here - confirmed on
# HA-1053, a real increment under an API key - by falling back to the copy it
# bundles, with one warning line and no error. Installed anyway so that
# fallback, which the vendor does not document as permanent, is never the only
# copy this image has.
#
# Retried as a whole, update and install together, for about five minutes (six
# attempts, 50s apart): on 2026-11-10 Ubuntu's mirror was mid-sync, with a
# `Packages.gz` of the wrong size and a 404 on a package the index named but
# the pool did not have yet. apt's own Acquire::Retries re-requests the same
# URL, which does not help against a stale index; only a fresh `apt-get
# update` does. `[ -n "$ok" ]` turns a failure that outlasts every attempt
# back into a failed build, and there is no sleep after the last attempt.
RUN ok= ; for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && apt-get install -y --no-install-recommends \
              bubblewrap \
              ca-certificates \
              curl \
              git \
              libstdc++6 \
              openssh-client; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
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
#
# update and install are retried together, for the reason given on the first
# apt step above; the keyring fetch and the .list write stay outside the loop.
RUN mkdir -p -m 755 /etc/apt/keyrings \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      -o /etc/apt/keyrings/githubcli-archive-keyring.gpg \
 && chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
 && for attempt in 1 2 3 4 5 6; do \
      if apt-get update \
         && gh_version=$(apt-cache madison gh | awk '{print $3}' | grep '^2\.' | sort -V | tail -1) \
         && apt-get install -y --no-install-recommends gh=$gh_version; then ok=1; break; fi; \
      echo "apt: attempt $attempt failed" >&2; \
      [ "$attempt" -lt 6 ] && sleep 50; \
    done; \
    [ -n "$ok" ] \
 && rm -rf /var/lib/apt/lists/*

# The native standalone installer rather than the npm one: there is no Node in
# this image and adding one to install a program that does not need it would
# double the image to buy nothing. `stable` rather than `latest` - an image
# built on any given day should carry the version Anthropic is telling people
# to run, not whatever landed that morning.
# Run from a file, not piped, for the reason the grok step gives below. Retried
# like every fetch here: a transient 5xx should not fail the build.
RUN curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 https://claude.ai/install.sh -o /tmp/claude-install.sh \
 && bash /tmp/claude-install.sh stable \
 && rm /tmp/claude-install.sh

# codex: the vendor's release binary rather than the npm package, for the same
# reason as above - there is no Node in this image. It is a static musl build,
# so the glibc base here runs it as it would any other. Unpinned
# (CODEX_VERSION=latest) and that is not ideal: a build on a given day picks up
# whatever shipped that morning, so set CODEX_VERSION to a release tag where
# that matters. The asset names were checked against the releases page when
# this landed (both triples answer under /releases/latest/download/); a rename
# upstream fails this build, not an increment. The download is retried because
# GitHub's release assets answer 504 now and then, for up to five minutes
# (--retry-max-time 300 is the governor; --retry and --retry-delay only need to
# reach it). The earlier ~50s budget was too short on 2026-11-09, when a 504
# outlasted all six attempts. --retry-max-time only bounds the time spent
# retrying after a failed attempt - it never gets a chance to count against an
# attempt that doesn't fail, just stalls, which is what hung main's build on
# 2026-11-12 for 624s fetching dotnet-install.sh. --connect-timeout 15 bounds
# the connect phase, and --speed-limit 1000 --speed-time 60 aborts an attempt
# whose transfer drops under 1000 bytes/sec for 60s straight, so a stall like
# that one fails in seconds and gets retried inside the 300s budget instead of
# blowing past it; a large file that's genuinely still moving never trips it.
# The same flags are on every vendor fetch in this file. It goes to a file
# rather than a pipe so a retry cannot hand tar a corrupt stream; a 404 still
# fails.
# TARGETARCH is declared again because the build stage's does not carry over.
ARG TARGETARCH
ARG CODEX_VERSION=latest
RUN case "$TARGETARCH" in \
      amd64) triple=x86_64-unknown-linux-musl ;; \
      arm64) triple=aarch64-unknown-linux-musl ;; \
      *) echo "hatch-runner: no codex release for TARGETARCH=$TARGETARCH" >&2; exit 1 ;; \
    esac \
 && if [ "$CODEX_VERSION" = latest ]; then \
      url=https://github.com/openai/codex/releases/latest/download/codex-$triple.tar.gz; \
    else \
      url=https://github.com/openai/codex/releases/download/$CODEX_VERSION/codex-$triple.tar.gz; \
    fi \
 && curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 "$url" -o /tmp/codex.tar.gz \
 && tar -xzf /tmp/codex.tar.gz -C /usr/local/bin \
 && rm /tmp/codex.tar.gz \
 && mv /usr/local/bin/codex-$triple /usr/local/bin/codex \
 && chmod +x /usr/local/bin/codex \
 && codex --version

# grok: the vendor's installer (https://x.ai/cli/install.sh - a prebuilt binary
# per architecture, no Node, no sudo; the URL was checked when this landed). It
# is a bash script, run from a file rather than piped so a failed download
# fails the build: sh has no pipefail, and `curl | bash` reports bash's exit
# and swallows curl's. It installs under ~/.grok/bin, which the PATH below
# carries. The grok adapter passes --no-auto-update, so what is installed here
# is what runs.
RUN curl -fsSL --retry-all-errors --retry 60 --retry-delay 5 --retry-max-time 300 --connect-timeout 15 --speed-limit 1000 --speed-time 60 https://x.ai/cli/install.sh -o /tmp/grok-install.sh \
 && bash /tmp/grok-install.sh \
 && rm /tmp/grok-install.sh \
 && /root/.grok/bin/grok --version

ENV PATH="/root/.local/bin:/root/.grok/bin:${PATH}"

# The CLI refuses `--permission-mode bypassPermissions` as uid 0 unless it is
# told it is in a sandbox - and an increment runs in print mode, where nothing
# can answer a permission prompt, so bypass is the only mode that works
# unattended (see ClaudeSessionRunner). This is that flag, set honestly: a
# container with one bind-mounted checkout in it is the isolation the guard is
# asking after. Root rather than a made-up user because the checkout arrives as
# a bind mount from the host, and a container user whose uid does not match the
# files cannot write to the branch it is supposed to push.
#
# That flag is Claude's. grok's grant is --always-approve --trust, which has no
# OS sandbox to satisfy, and ran as root here with no privilege error - the one
# real increment attempted (HA-1053) never got past its own "Not signed in"
# refusal to say more than that. codex's is --approve-for-me (workspace-write):
# PROVEN on HA-1053 - its Linux sandbox starts as root in this unprivileged
# container, via bubblewrap (now installed above; it fell back to its own
# bundled copy first, with a warning, before that). No user namespace or
# landlock error, no security_opt or cap_add needed. The adapter never passes
# codex's bypass-the-sandbox flag to make it go, and that stays true. That proof
# holds under ollama too: OllamaAdapter (src/Hatch.Cli/OllamaAdapter.cs) is a
# CodexAdapter underneath, spawning the same binary with the same
# --approve-for-me grant through the same bubblewrap - only the provider flags
# (--oss --local-provider ollama) and the CODEX_OSS_BASE_URL environment
# variable differ.
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

# /etc/hatch/ is reserved for what an image bakes in about itself, and is empty
# here on purpose (HA-1882 will read /etc/hatch/dependencies.json from it).
RUN mkdir -p /etc/hatch

ENTRYPOINT ["/entrypoint.sh"]
