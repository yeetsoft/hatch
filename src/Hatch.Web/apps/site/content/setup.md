---
title: Set up Hatch with an agent
summary: A guide a coding agent on your own machine follows to install what Hatch needs, one plain-language step at a time.
---

This page is written for Claude to read, not for you. You do not have to
follow it yourself. Open Claude Code on the machine you want Hatch on and
paste the line below into it. If it still shows angle brackets, put the
address of this page in place of them first:

```
Set me up with Hatch: <the address of this page>
```

Claude will read the rest of this page and then walk you through it, one step
at a time, doing the typing and asking you only for the few things that have
to be done by a person. If you would rather do the steps yourself, the
[Quick start](index.md) is the same path written for you.

## Before you paste

Claude needs two things on this machine before it can set Hatch up for you.

- **A Claude plan** - Pro, Max, Team or Enterprise, or a Console account. The
  free claude.ai plan does not include Claude Code.
- **Claude Code**, running in a terminal or in the Claude desktop app's Code
  tab (see the [Desktop quickstart](https://code.claude.com/docs/en/desktop-quickstart)).
  claude.ai in a browser cannot run commands, so it will not do.

Install it with the line for this machine:

macOS and Linux:

```
curl -fsSL https://claude.ai/install.sh | bash
```

Windows PowerShell:

```
irm https://claude.ai/install.ps1 | iex
```

Have both already? Paste the line above into Claude and it takes it from
there.

## For the agent reading this

You are setting up Hatch, a self-hosted kanban board worked by AI agents, on
the machine you are running on. Read this whole section before step 1.

### Who you are talking to

- **The person is new to this and is not a developer.** They want to be
  walked through, not lectured. Go one step at a time.
- **Before each step, say what is about to happen, why, and roughly how long
  it takes.** After it, say what it meant in a sentence.
- **Explain any term the first time you use it, in one sentence**: terminal,
  Docker, repository, port, and so on. Do not assume they know any of them.
- **Never paste a wall of command output at them.** Say what it meant and
  quote only the line that matters.

### Read this page whole

If this page reached you through a fetch tool that summarises what it
fetches, a summary drops the commands, and the commands are the point. Fetch
it again verbatim before step 1, with the address the person gave you:

```
curl -fsSL <the address of this page>
```

On Windows PowerShell:

```
Invoke-WebRequest -UseBasicParsing <the address of this page> | Select-Object -ExpandProperty Content
```

or `irm <the address of this page>` in PowerShell 7. The `-UseBasicParsing`
flag is what Windows PowerShell 5.1 needs.

### If you cannot run commands here

If you are in a chat with no way to run commands on the person's machine
(claude.ai in a browser, for instance), say so plainly. Do not pretend to
install anything and do not turn the steps into a list for them to type.
Tell them to open Claude Code on the machine they want Hatch on and paste
the same line there.

### The rules

- **Check before you install anything.** Run the check a step names first;
  if it already passes, say so and move on.
- **Use the machine's own package manager**: Homebrew on macOS, winget on
  Windows, apt or dnf on Linux, unless a step names another route and says
  why.
- **Never type a password.** Anything that needs `sudo`, an administrator
  prompt, a restart or a sign-in is handed to the person as one exact line
  or click to do themselves, in their own terminal window or browser. Wait
  for them to say it is done, then check it yourself.
- **Never put a credential into a file, a command or a message.** That covers
  passwords, tokens and keys, theirs and anyone else's.
- **Check every step with the check this page names before you move on.** A
  command that exited without an error has not passed a check.
- **When a step fails, stop.** Say which step, what you saw and what you
  suggest trying. Do not improvise past it.

### Where this page ends

At the end of step 15 the board is open with the person's name and a first
project on it. `hatch` is installed, on the `PATH` and pointed at the board,
and `hatch queue` has answered from the repository folder from step 10, with
`claude` signed in, `gh` signed in where the person's code is on GitHub, and
git knowing who they are (steps 7 to 10). If the steps below stop before
then, the guide is not finished yet: do the steps that are there, then tell
the person plainly that the rest is not published yet, and stop.

### Which shell you are in

Claude Code on native Windows uses Git Bash when Git for Windows is
installed and PowerShell otherwise ([Claude Code setup](https://code.claude.com/docs/en/setup)).
So every Windows block below is written for PowerShell, and the few that
differ in Git Bash say so.

If your session is inside WSL (the Linux that runs within Windows), tell the
person to close it and start Claude Code from PowerShell instead. Docker
Desktop and the folder Hatch is cloned into are on the Windows side, and a
session in WSL cannot reach them cleanly.

## The steps

### 1. Work out the machine

**What this is.** Hatch needs to know which operating system (the software
that runs the whole computer, such as macOS, Windows or Linux) and which
kind of processor (the chip, which comes in an Arm kind and an Intel or AMD
kind) the person has, because some downloads come in one build per kind.

**Tell the person.** "First I am going to look at what kind of computer this
is, so I pick the right downloads later. It is one command and takes a
second."

**Do it.**

macOS:

```
sw_vers -productVersion; uname -m
```

Windows PowerShell:

```
[System.Environment]::OSVersion.Version; $env:PROCESSOR_ARCHITECTURE
```

Linux:

```
. /etc/os-release && echo "$PRETTY_NAME"; uname -m
```

**Check.** The command prints a version and an architecture. On macOS the
architecture is `arm64` (Apple silicon) or `x86_64` (Intel). On Windows it
is `AMD64` or `ARM64`. On Linux it is `x86_64` or `aarch64`. Tell the person
what you found in a sentence, such as "This is macOS 15 on an Apple silicon
chip."

Say two things now, so they are not a surprise later:

- The board runs on either kind of chip.
- The runner, the program that does the agent's work, is published for
  Windows on x64, macOS on Apple silicon and Intel, and Linux on x64. There
  is no build for Linux on Arm or for Windows on Arm. If the machine is one
  of those, say so now and stop, because the guide cannot finish on it.

**If you see** a version or architecture you do not recognise, or a command
that is not found, stop and say what you saw.

### 2. Git

**What this is.** Git is the program that keeps a history of a project's
files, and a repository is a folder it keeps that history for. Hatch is
downloaded as a repository, so git has to be installed to get it.

**Tell the person.** "Next I am going to make sure git is installed. It is a
small tool and the install takes about a minute, if it is not there already.
It is how I will download Hatch itself."

**Do it.** First run the check below. If it prints a version, git is
installed: say so and go on to the next step.

```
git --version
```

If it is not found, install it.

macOS. If `brew --version` works, use Homebrew:

```
brew install git
```

If Homebrew is not installed, do not install it just for git. Use the Xcode
Command Line Tools, which include git:

```
xcode-select --install
```

That opens a window on the person's screen with an **Install** button.

> **Hand this to the person.** "A window just opened asking to install the
> command line developer tools. Click **Install**, agree to the terms, and
> wait for it to finish. It can take several minutes. Tell me when it says
> it is done."

Wait for them, then run the check again.

Homebrew is only worth installing when a later step needs it. It is
installed by a line that asks for the person's password, which you must never
type.

> **Hand this to the person.** "Open the Terminal app, paste this one line,
> press Return, and type your Mac password when it asks (nothing shows as you
> type, that is normal). Tell me when it finishes."
>
> ```
> /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
> ```

Wait for them, then check `brew --version`. On Apple silicon the installer
ends with a "Next steps" section containing a line that starts
`eval "$(/opt/homebrew/bin/brew shellenv)"`. The person has to run that line
in their Terminal, or open a new Terminal window, before `brew` is found.

Windows PowerShell:

```
winget install --id Git.Git -e --source winget
```

When it finishes, the new program is not yet on this terminal's search path.
Tell the person to open a new terminal window, or restart Claude Code in
one, and carry on from there. If winget asks to accept an agreement, or
Windows shows an administrator prompt, hand that to the person to click and
wait.

Linux. On Debian and Ubuntu:

```
sudo apt-get install git
```

On Fedora and Red Hat:

```
sudo dnf install git
```

Both need `sudo`, so do not run them yourself.

> **Hand this to the person.** "Please run this line in your own terminal
> window, type your password when it asks, and tell me when it finishes:
> `sudo apt-get install git`" (or the `dnf` line, whichever matches the
> machine).

**Check.** `git --version` prints a line such as `git version 2.47.0`. The
exact number does not matter. Tell the person git is installed.

**If you see** `command not found` or `'git' is not recognized` right after
an install, the terminal has not picked up the new program: on Windows open
a new terminal; on macOS after Homebrew, run the `shellenv` line above. If it
still fails, stop and say what you saw.

Reference: [git-scm.com for macOS](https://git-scm.com/install/mac),
[Windows](https://git-scm.com/install/windows) and
[Linux](https://git-scm.com/install/linux); [Homebrew](https://brew.sh).

### 3. Docker

**What this is.** Docker is a program that runs other programs in sealed
boxes called containers, so they behave the same on every machine; Hatch is
delivered as containers. Docker Desktop is the app that provides Docker on
macOS and Windows, and Docker Engine is the background service that provides
it on Linux. Compose is the part of Docker that starts several containers
together, and Hatch needs it.

**Tell the person.** "Next I am going to make sure Docker is installed and
running. It is how Hatch will run on this machine. If it is not there yet,
this is a large download and takes about 5 to 15 minutes. There will be one
or two things only you can do: agree to Docker's terms, type your computer
password or approve an administrator prompt, and maybe restart the
computer. I will tell you exactly when."

**Do it.** First run the check below. If both commands pass, Docker is
installed and running: say so and go on to the next step.

```
docker version
docker compose version
```

Otherwise install it for this machine. On macOS and Windows, tell the person
what **The licence** paragraph below says before they install Docker Desktop.

macOS. This installs Docker Desktop. If `brew --version` works, use
Homebrew; do not install Homebrew just for this:

```
brew install --cask docker-desktop
```

If Homebrew is not installed, the person downloads the installer
themselves: they open the Docker Desktop page for Mac below, pick the
**Apple silicon** or **Intel** download to match what step 1 found, open the
`Docker.dmg` it saves and drag Docker into the Applications folder. Docker
Desktop needs at least 4 GB of memory and one of the three newest macOS
releases; the Homebrew cask itself requires macOS 14 or later. Then start it:

```
open -a Docker
```

> **Hand this to the person.** "Docker's own window just opened. Read its
> terms, click **Accept** to agree, and let it finish starting. If it asks
> for your Mac password so it can set itself up, type it there, in its
> window; I cannot and will not. Tell me when the window says Docker is
> running."

Windows PowerShell. This installs Docker Desktop. The `winget` route below
is the package manager's own and is not on Docker's page, which documents
only the installer download (**Docker Desktop Installer.exe**, from the
Docker Desktop page for Windows below). If `winget` fails, hand that
download to the person instead.

Docker Desktop on Windows needs WSL 2 (the Windows Subsystem for Linux, the
part of Windows that runs the Linux programs Docker is built on) at version 2.1.5 or
later, a 64-bit processor with second-level address translation, 8 GB of
memory, Windows 10 22H2 (build 19045) or newer or Windows 11 23H2 (build
22631) or newer, and hardware virtualization switched on. Check the ones
that can be checked first:

```
wsl --version
systeminfo
```

In the `systeminfo` output, the line "A hypervisor has been detected" means
virtualization is on. (In Task Manager, the **Performance** tab, **CPU**,
says "Virtualization: Enabled" when it is.) Then install:

```
winget install -e --id Docker.DockerDesktop
```

If `wsl --version` is not found, WSL is missing. Installing it needs an
administrator.

> **Hand this to the person.** "Please open the Start menu, type
> **PowerShell**, right-click **Windows PowerShell** and choose **Run as
> administrator**. Paste this line, press Enter, and tell me when it
> finishes. Then restart the computer, open Claude Code again, and tell me
> you are back."
>
> ```
> wsl --install
> ```

If `wsl --version` works but reports a version older than 2.1.5, the person
updates it the same way, with `wsl --update` in an administrator PowerShell.

If virtualization is off, it is a setting in the computer's firmware screen
(the BIOS or UEFI, reached while the computer starts up). The key that opens
that screen differs by maker, so do not guess one: tell the person the
maker's name and have them look up "enable virtualization" on that maker's
support page.

> **Hand this to the person.** "Virtualization is switched off on this
> computer. It is a setting in the firmware screen you reach while the
> computer starts up, and the key to press differs by maker, so please
> search for your computer maker's support page on enabling virtualization.
> Turn it on, start Windows again, and tell me when you are back."

The Docker Desktop installer asks whether to install for just the person or
for all users. For just the person needs no administrator prompt. If winget,
or the installer, shows a prompt, a licence agreement or an administrator
question, hand it to the person to answer and wait. When it finishes the new
program is not on this terminal's search path yet, so tell the person to
open a new terminal window, or restart Claude Code in one, and carry on from
there. Then start Docker Desktop from the Start menu.

> **Hand this to the person.** "Please open **Docker Desktop** from the Start
> menu, read its terms and click **Accept**, and let it finish starting. Tell
> me when the window says Docker is running."

Linux. This installs Docker Engine, the service, not Docker Desktop. Find out
which distribution this is:

```
. /etc/os-release && echo "$ID"
```

If it prints `debian` or `ubuntu`, use the matching block below. If it prints
`fedora`, use the Fedora block. If it prints anything else, including a
derivative such as Kali or Mint where the release name is not Docker's, stop
and ask the person: Docker publishes steps for other distributions at
[docs.docker.com/engine/install](https://docs.docker.com/engine/install/),
and this guide does not cover them.

Everything below needs `sudo`, so do not run any of it yourself. Hand each
block over whole, as one paste.

On Ubuntu:

> **Hand this to the person.** "Please open your own terminal window, paste
> all of this at once, type your password when it asks (nothing shows as you
> type, that is normal), and tell me when it finishes. It adds Docker's
> package source and installs Docker."
>
> ```
> sudo apt update
> sudo apt install ca-certificates curl
> sudo install -m 0755 -d /etc/apt/keyrings
> sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
> sudo chmod a+r /etc/apt/keyrings/docker.asc
> sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
> Types: deb
> URIs: https://download.docker.com/linux/ubuntu
> Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
> Components: stable
> Architectures: $(dpkg --print-architecture)
> Signed-By: /etc/apt/keyrings/docker.asc
> EOF
> sudo apt update
> sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
> ```

On Debian, the same block with two differences, the `debian` address and the
plain `$VERSION_CODENAME`:

> **Hand this to the person.** "Please open your own terminal window, paste
> all of this at once, type your password when it asks (nothing shows as you
> type, that is normal), and tell me when it finishes. It adds Docker's
> package source and installs Docker."
>
> ```
> sudo apt update
> sudo apt install ca-certificates curl
> sudo install -m 0755 -d /etc/apt/keyrings
> sudo curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
> sudo chmod a+r /etc/apt/keyrings/docker.asc
> sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
> Types: deb
> URIs: https://download.docker.com/linux/debian
> Suites: $(. /etc/os-release && echo "$VERSION_CODENAME")
> Components: stable
> Architectures: $(dpkg --print-architecture)
> Signed-By: /etc/apt/keyrings/docker.asc
> EOF
> sudo apt update
> sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
> ```

On Fedora (Debian and Ubuntu start the service by themselves when it is
installed, Fedora does not, which is the last line here):

> **Hand this to the person.** "Please paste all of this into your own
> terminal window, type your password when it asks, and tell me when it
> finishes. It adds Docker's package source, installs Docker and starts it."
>
> ```
> sudo dnf config-manager addrepo --from-repofile https://download.docker.com/linux/fedora/docker-ce.repo
> sudo dnf install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
> sudo systemctl enable --now docker
> ```

Last, the person has to be allowed to use Docker without `sudo`. Adding an
account to the `docker` group gives it administrator-equivalent power on this
machine, because anything that can talk to Docker can ask it to act as root.
That is the person's decision, so tell them in one sentence and do not decide
for them. If they decline, stop and say that the rest of this guide needs
Docker to work without `sudo`.

> **Hand this to the person.** "Docker needs your account to be in a group
> called `docker`. That group gives your account administrator-equivalent
> power on this machine. If you are fine with that, paste this line, type
> your password, then log out of the computer and back in, and tell me when
> you are back."
>
> ```
> sudo usermod -aG docker $USER
> ```

A session that was already open does not see the new group. After they are
back, run the check from a fresh login, in a new terminal. The person can
also run `newgrp docker` in the terminal the session lives in. If the check
still says `permission denied`, say so and stop; do not reach for
`sudo docker` yourself.

**Check.** Run these two commands:

```
docker version
docker compose version
```

`docker version` prints a **Client** section and a **Server** section.
`docker compose version` prints a line such as `Docker Compose version
v2.40.0`; the exact number does not matter. Tell the person Docker is
installed and running.

If `docker version` shows a Client but says `Cannot connect to the Docker
daemon`, or there is no Server section, the engine is not running. On macOS
and Windows, start Docker Desktop and wait for it to say it is running, which
takes a minute or two. On Linux, hand the person this:

> **Hand this to the person.** "Please run this in your own terminal window,
> type your password, and tell me when it finishes: `sudo systemctl start
> docker`"

**The licence.** Docker Desktop is free for personal use, education,
non-commercial open source projects and small businesses (fewer than 250
employees and under $10 million in annual revenue), and needs a paid
subscription otherwise. Whether that covers the person is theirs to decide,
not yours; say it in a sentence, point them at
[Docker's licence terms](https://docs.docker.com/subscription/desktop-license/)
and wait for them to go ahead. Docker Engine on Linux has no such licence.

**If you see** `docker: command not found` straight after installing Docker
Desktop, the terminal has not picked up the new program: open a new terminal
window. On macOS also check that `/Applications/Docker.app` exists; if it
does not, the install did not finish.

**If you see** Docker Desktop installed but `docker version` shows no Server
section, Docker Desktop is not running, has not been opened since it was
installed, or its terms have not been accepted. Open it, hand the person the
terms window, and wait for it to say it is running.

**If you see** on Windows an error from `docker version` that mentions WSL
and an update or a version, WSL is out of date: hand the person `wsl
--update` to run in an administrator PowerShell, then have them restart
Docker Desktop.

**If you see** `permission denied while trying to connect to the Docker
daemon socket` on Linux, the `docker` group step has not applied to this
login. Have the person log out and in again, then check again from a new
terminal.

**If you see** that Docker Desktop's installer is blocked, or asks for
approval you cannot give, on a machine managed by a school or an employer,
that is an administrator's decision. Stop and say so.

Reference: Docker Desktop for
[Mac](https://docs.docker.com/desktop/setup/install/mac-install/) and
[Windows](https://docs.docker.com/desktop/setup/install/windows-install/);
Docker Engine on
[Ubuntu](https://docs.docker.com/engine/install/ubuntu/),
[Debian](https://docs.docker.com/engine/install/debian/) and
[Fedora](https://docs.docker.com/engine/install/fedora/), and its
[after-install steps](https://docs.docker.com/engine/install/linux-postinstall/);
[Compose on Linux](https://docs.docker.com/compose/install/linux/);
[Docker Desktop troubleshooting](https://docs.docker.com/desktop/troubleshoot-and-support/troubleshoot/);
[WSL](https://learn.microsoft.com/windows/wsl/install);
[Homebrew's Docker cask](https://formulae.brew.sh/cask/docker-desktop).

### 4. Clone Hatch

**What this is.** Cloning is downloading a repository, the folder git keeps
a project's files and history in, so there is a copy of it on this machine.
Hatch is delivered as a repository, and it holds the file that tells Docker
how to start the board.

**Tell the person.** "Next I am going to download Hatch into a folder called
`hatch` in your home folder, which is the folder that holds your documents
and downloads. It is one command and takes about a minute. If that folder is
already there, I will look at it first and I will not delete or overwrite
anything."

**Do it.** The folder is `~/hatch` (`$HOME/hatch`; `$env:USERPROFILE\hatch`
in PowerShell). Look at it first and take the first case that fits.

(a) The folder is absent. Clone it. Each command starts from the home
folder, so it works in a fresh terminal.

macOS and Linux:

```
cd "$HOME" && git clone {{clone-url}} hatch
```

Windows PowerShell, with the same address as the line above:

```
Set-Location $env:USERPROFILE; git clone <the same address> hatch
```

If the command above still shows two opening curly braces in a row, the page
was read unrendered. The owner and the repository are in the address the page
was fetched from (`https://<host>/<owner>/<repo>/...` or
`https://<owner>.github.io/<repo>/setup/`), so build `https://github.com/<owner>/<repo>.git` from it. If that cannot be
worked out, ask the person for the repository's address instead of guessing.

(b) The folder exists, `git -C ~/hatch remote get-url origin` succeeds and
`compose.yaml` is in it. It is already a Hatch checkout, so reuse it. Do not
clone again. Then look at what has changed in it:

```
git -C ~/hatch status --short
```

On Windows PowerShell:

```
git -C $env:USERPROFILE\hatch status --short
```

If that prints anything, the person has changes in the folder: say so, and do
not pull.

(c) Anything else, such as a folder that exists but is not a git checkout or
has no `compose.yaml`. Stop and ask the person which folder to use. Never
delete or overwrite a folder.

**Check.** Run this. It prints a short sha, a few letters and digits such as
`a1b2c3d`:

```
git -C ~/hatch rev-parse --short HEAD
```

On Windows PowerShell:

```
git -C $env:USERPROFILE\hatch rev-parse --short HEAD
```

And `compose.yaml` must exist in the folder:

```
test -f ~/hatch/compose.yaml && echo present
```

On Windows PowerShell, which prints `True`:

```
Test-Path $env:USERPROFILE\hatch\compose.yaml
```

Tell the person Hatch is downloaded and where.

**If you see** `Repository not found`, or a prompt for a username and
password, the address is wrong or the repository is private. Stop. Never type
a password at the command and never ask the person for one; say what you saw.

**If you see** `git: command not found`, step 2 did not take. Open a new
terminal and run step 2's check.

Reference: [git clone](https://git-scm.com/docs/git-clone).

### 5. Start the board

**What this is.** A port is the numbered door a program on this machine
listens at, so that a browser knows where to knock; only one program can hold
a given door at a time. Hatch's board listens at port 8080 unless told
otherwise, and `localhost` is this machine's name for itself, so the board will
be at `http://localhost:` followed by that number. A `.env` file is a small
text file of `NAME=value` lines that Docker Compose reads when it starts
Hatch, and it sits beside `compose.yaml`.

**Tell the person.** "Next I am going to start Hatch. First I will check that
nothing else on this computer is using the port it wants, and pick another if
something is. Then comes the first build, which takes several minutes
because it builds Hatch from source on your machine. There is nothing for you
to do while it runs; I will tell you when it is done."

**Do it.** This part chooses the port and writes it down. It does not start
Hatch; the part after it does. Every block starts from the `hatch` folder, so
it works in a fresh terminal. Compose publishes the board at
`"${HATCH_PORT:-8080}:8080"`, so the port is 8080 unless `HATCH_PORT` says
otherwise.

First see whether `~/hatch/.env` already sets a port. This prints the line if
it does, and nothing if it does not or if there is no `.env`:

macOS and Linux:

```
cd "$HOME/hatch" && grep '^HATCH_PORT=' .env 2>/dev/null
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE\hatch; Select-String -Path .env -Pattern '^HATCH_PORT=' -ErrorAction SilentlyContinue
```

If it printed a line, the port is the number after `=`. Use it, tell the
person you found it already set, and go to the end of this part: do not check
or write anything.

Otherwise check that 8080 is free. Read the output, never the exit status:
**empty output means free**, and any output means something is listening
there. (`lsof` exits 1 when nothing listens, which is the good case.)

macOS:

```
lsof -nP -iTCP:8080 -sTCP:LISTEN
```

Linux, where `lsof` is often missing:

```
ss -H -ltn 'sport = :8080'
```

Windows PowerShell:

```
Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
```

In Git Bash on Windows, run the PowerShell command through PowerShell:

```
powershell.exe -NoProfile -Command "Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue"
```

If 8080 is free, the port is 8080 and there is nothing to write. If it is
taken, run the same check with 8081, then 8082, and so on, and stop at the
first port whose output is empty. That is the port. Tell the person which port
you chose and that 8080 was in use.

If the port is not 8080, write it down before Hatch is first started, so that
`docker compose up -d` is the same command on every machine and every later
command finds the same port. Put `HATCH_PORT=<port>` in `.env` beside
`compose.yaml`. The line below creates the file if it is absent and adds to it
if it is not, and the check above has already shown the file has no
`HATCH_PORT=` line, so never overwrite the file and never edit a line in it.
Here the port is 8081; use the real one.

macOS and Linux:

```
cd "$HOME/hatch" && printf '\nHATCH_PORT=8081\n' >> .env
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE\hatch; Add-Content -Path .env -Value '', 'HATCH_PORT=8081'
```

The `.gitignore` in the folder already ignores `.env`, so the line is never
committed, and a port number is not a credential, so the rule against putting
credentials in files does not apply to it.

To try a port once without writing it down, put it in front of the command
itself. This lasts for that one command only; the next `docker compose`
command in a new terminal is back on 8080 unless `.env` says otherwise, which
is why the `.env` line is the one to use here:

macOS and Linux:

```
cd "$HOME/hatch" && HATCH_PORT=8081 docker compose up -d
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE\hatch; $env:HATCH_PORT=8081; docker compose up -d
```

Those two are shown for what they are and are not to be run now. This part
ends with one number, the port, which you use for every later command and
address on this page, and with `docker compose up -d` not yet run.

Now start it. The first run builds Hatch's database and its web server from
source, and that takes several minutes, so the agent's own command timeout is
the trap here: a shell tool call in Claude Code stops after 2 minutes unless
it is given more, and 10 minutes is the most it can be given. Run the start
with the longest timeout your tool allows, or in the background, and do not
treat a timeout as a failed start. If the command was cut off, run it again:
it is safe to repeat, and Docker reuses what it has already built.

macOS and Linux:

```
cd "$HOME/hatch" && docker compose up -d
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE\hatch; docker compose up -d
```

If the command has returned, the containers have been created, but the board
is not necessarily answering yet. The database starts, then a one-off step
sets it up and exits by design, then the web server starts. Wait for the board itself to answer, in slices rather than one long
loop, so that no single command outruns your timeout. Each slice below tries
up to 18 times, 5 seconds apart (about 90 seconds), and prints `up` the first
time the board answers with HTTP 200, and `waiting` if it never did. In each,
`<port>` is the port chosen above, such as 8080.

macOS and Linux, and Git Bash on Windows:

```
up=0; for i in $(seq 1 18); do code=$(curl -s -o /dev/null -w '%{http_code}' http://localhost:<port>/apps/hatch/); [ "$code" = 200 ] && { up=1; break; }; sleep 5; done; [ "$up" = 1 ] && echo up || echo waiting
```

Windows PowerShell:

```
$up = $false; for ($i = 0; $i -lt 18; $i++) { $c = try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 http://localhost:<port>/apps/hatch/).StatusCode } catch { 0 }; if ($c -eq 200) { $up = $true; break }; Start-Sleep 5 }; if ($up) { 'up' } else { 'waiting' }
```

If a slice prints `waiting`, run another, until ten minutes have passed in
all. Between slices, about once a minute and in one sentence, tell the person
what it is waiting for: first the database image downloading, then the web
server image downloading and building, then the database being set up, and
last the web server starting. Do not say more than that, and do not guess at
how long is left.

**Check.** The poll printed `up`. That is the evidence. If the person asks
for more, `docker compose ps` run in the `hatch` folder lists `db` and `api`
as running and `migrate` as exited, which is right: it sets the database up
and then stops. Tell the person the board is running and give them the
address, `http://localhost:` followed by the port, and say that the next step
opens it.

If ten minutes have passed and no slice printed `up`, stop. Run
`docker compose ps` and `docker compose logs --tail 100` from the `hatch`
folder, say in plain words what they showed, and stop there. Do not start it
again, and do not edit `compose.yaml` or any other file in the folder.

**If you see** `port is already allocated`, `address already in use`, or
`bind` followed by `5432`, a database is already running on this computer and
holds the port Hatch's own database needs. Unlike the board's port, this one
cannot be moved: `compose.yaml` has no setting for it. Stop. Find what holds
5432 with the same kind of check as for 8080 above, with `5432` in place of
`8080`, tell the person what it is, and ask them to stop that service or to
ask whoever looks after their computer. Never edit `compose.yaml`. Say plainly
that this guide has no fix for this yet. That is a gap in Hatch, not in what
the person did.

**If you see** `Cannot connect to the Docker daemon` or `error during
connect`, Docker is not running. Go back to step 3 and run its check, then
start this step again from the start command.

**If you see** `no matching manifest` or `exec format error`, the images do
not suit this machine's processor. Stop and say what the machine is, from
step 1.

Reference: [docker compose up](https://docs.docker.com/reference/cli/docker/compose/up/)
and [variable interpolation in Compose](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/).

### 6. Open the board

**What this is.** A browser is the program the person uses to look at web
pages, such as Safari, Chrome, Edge or Firefox. The board is a web page that
Hatch serves from this machine, so opening it means pointing a browser at the
address from step 5: `http://localhost:` followed by the port, then
`/apps/hatch/`. A bookmark is a saved address, so the person can get back to
the board without being told it again.

**Tell the person.** "Hatch is running, so now I am going to open the board in
your browser. It takes a second. It will be empty, with no project on it yet;
that is expected, because nobody has made one. When it opens, tell me you can
see it, and please bookmark the page so you can come back to it."

**Do it.** In each command `<port>` is the port step 5 settled on, such as
8080. Open the address in the person's default browser.

macOS:

```
open http://localhost:<port>/apps/hatch/
```

Windows PowerShell:

```
Start-Process http://localhost:<port>/apps/hatch/
```

In Git Bash on Windows:

```
cmd.exe //c start "" http://localhost:<port>/apps/hatch/
```

Linux:

```
xdg-open http://localhost:<port>/apps/hatch/
```

If `xdg-open` is missing, or this machine has no desktop, do not install
anything: give the person the address and ask them to open it in a browser on a
machine that can reach this one.

> **Hand this to the person.** "The board should now be open in your browser.
> Please bookmark it so you can find it again. Tell me when you can see it."

**Check.** Step 5's poll printed `up`, and the person says they can see the
board. An empty board with no project on it is correct. What goes on it is
covered by the board's own Docs page, which is linked from the gear inside the
board.

**Restarts.** If the computer restarts or Docker stops, Hatch comes back by
itself once Docker is running again, because the database and the web server
are set to restart. On macOS and Windows that means the person starting Docker
Desktop, which can be set to start when they log in, in its settings; tell
them so and do not change the setting yourself. On Linux the Docker service
normally starts at boot; if it does not, hand the person
`sudo systemctl enable --now docker`. Then the person opens their bookmark.
There is nothing else to run.

**Their data.** The board's data lives in a Docker volume called
`hatch-local_pgdata`, so it survives restarts and `docker compose down`.
`docker compose down -v` deletes it with no undo. Never run it, and never
suggest it, unless the person asks to start again from an empty board.

**If you see** the browser opens but says it cannot connect, run step 5's poll
slice again. If it does not print `up`, go back to step 5; do not start Hatch a
second time here.

**If you see** a different page from the board, or a program that is not
Hatch, something else is using that port. Go back to step 5 and check the port
again.

Reference: [Start-Process](https://learn.microsoft.com/powershell/module/microsoft.powershell.management/start-process).

### 7. The Claude Code CLI

**What this is.** `claude` is the program that Hatch's runner starts, once
for each piece of work, to let an agent do it. It is looked for on the
terminal's search path, so it has to be installed there and signed in to the
person's Claude account. The Claude Code you are running as now may be
another copy: the Claude desktop app has Claude Code built in but puts no
`claude` in the terminal ([Desktop quickstart](https://code.claude.com/docs/en/desktop-quickstart)).
So check, and do not assume.

**Tell the person.** "Next I am going to make sure the `claude` program is
installed in the terminal and signed in to your Claude account, because Hatch
will start it to do the work. If it is not there, the install takes about two
minutes and shows no progress while it downloads, so please be patient. The
sign-in is yours to do, in your browser; I cannot do it for you. Claude Code
needs a Claude Pro, Max, Team or Enterprise plan, or a Console account; the
free claude.ai plan does not include it. Do you have one of those?" If they
have none, say so plainly and stop.

**Do it.** First run the check below. A version such as
`2.1.287 (Claude Code)` means `claude` is installed: go to the sign-in below.
On Windows, if it opens a window instead of printing a version, an old
desktop app is answering to the name; treat `claude` as not installed and see
the second **If you see** below.

```
claude --version
```

If it is not found, install it with the vendor's own installer. This is the
one install on this page that does not go through the package manager,
because the installer is the route the vendor recommends, needs no
administrator, and keeps itself up to date.

macOS and Linux:

```
curl -fsSL https://claude.ai/install.sh | bash
```

Windows PowerShell:

```
irm https://claude.ai/install.ps1 | iex
```

In Git Bash on Windows, the line below is this page's own wrapper around the
same installer, not the vendor's. If it fails, use the PowerShell line above
instead:

```
powershell.exe -NoProfile -Command "irm https://claude.ai/install.ps1 | iex"
```

The shell you are running commands in started before the install, so it
may not find the new program yet. If a bare `claude --version` is still not found, run the file
by its path:

```
"$HOME/.local/bin/claude" --version
```

On Windows PowerShell:

```
& "$env:USERPROFILE\.local\bin\claude.exe" --version
```

If that works but a new terminal cannot find `claude`, see the first
**If you see** below.

Then the sign-in, which is the person's:

> **Hand this to the person.** "Please open your own terminal window (not
> this one), run this line, and sign in with your Claude account in the
> browser it opens. Tell me when the terminal says `Login successful`."
>
> ```
> claude auth login
> ```
>
> (For a Console account, `claude auth login --console`.) If the browser
> does not open, press `c` in the terminal to copy the address and open it
> yourself. If the page shows a code, paste it at the `Paste code here if
> prompted` prompt.

**Check.** Both of these must pass. `claude --version` prints a version, and
this exits 0 and prints a `Login method` line:

```
claude auth status --text
```

Tell the person `claude` is installed and which login method it showed, such
as "Claude Max account". Quote only that line; never read out the
organization or the email address.

**If you see** `command not found` or `is not recognized` for `claude` in a
new terminal, the installer put the file in a folder that is not on the
search path. Confirm the file with `ls -la ~/.local/bin/claude`. On macOS or
Linux, the fix is two lines in the person's own shell file, so it changes
their files: say so, ask first, and tell them afterwards what you changed.
Hand them these, with `~/.zshrc` on macOS, or `~/.bashrc` for Bash on Linux
or `~/.bash_profile` for Bash on macOS:

```
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

On Windows, `%USERPROFILE%\.local\bin` goes on the user's search path; the
vendor's page has the steps, in its PATH section, so link it rather than
retyping it ([troubleshooting](https://code.claude.com/docs/en/troubleshoot-install)).

**If you see** on Windows that `claude` opens the desktop app, the desktop
app is shadowing it. Tell the person to update the desktop app, then open a
new terminal and check again.

**If you see** an error from the installer, such as `syntax error near
unexpected token '<'` or a `403`, stop and say what you saw. Do not retry in
a loop. The vendor's troubleshooting page below covers it.

**If you see** `claude auth status` exit 1 after the person said they were
done, ask what the terminal said, hand them `claude auth login` again, and
check again. Do this at most twice, then stop and say what you saw.

**If you see** that the person's plan is only through Bedrock, Vertex or
Foundry (cloud providers that can host Claude), this guide does not cover
that. Stop and say so.

Reference: [Claude Code setup](https://code.claude.com/docs/en/setup),
[authentication](https://code.claude.com/docs/en/authentication),
[CLI reference](https://code.claude.com/docs/en/cli-reference),
[troubleshooting](https://code.claude.com/docs/en/troubleshoot-install),
[Desktop quickstart](https://code.claude.com/docs/en/desktop-quickstart).

### 8. Where the code lives

**What this is.** A forge is a website that stores repositories and lets
people review changes to them: GitHub, GitLab, Bitbucket, or a server run by
a company. `gh` is GitHub's own command-line program. Hatch's runner uses it
to read whether a pull request's checks passed, to notice that one was
merged, and to merge approved ones. It only works for code on GitHub.

**Tell the person.** "Next I need to know where the code you want agents to
work on is kept: on GitHub, somewhere else, or nowhere yet? If it is on
GitHub, I will make sure the `gh` program is installed and signed in to your
account, which takes a few minutes. The sign-in is yours to do, in your own
terminal and browser; I cannot do it for you." Record the answer as **gh
signed in: yes / no / not applicable**. Keep it: step 10 and your closing
summary use it.

**Do it.** Take the first case that fits.

(a) The code is on GitHub and the person has no GitHub account. They make one
in their browser, following GitHub's own page, "Creating an account on
GitHub" (linked below). Never fill in the form for them and never set a
password. Wait for them to say it is done, then go to (b).

(b) The code is on GitHub and the person has an account. First look:

```
gh --version
gh auth status
```

If `gh auth status` already exits 0, `gh` is installed and signed in: say so
and go to the check below. Otherwise install `gh`, if `gh --version` did not
print a version.

macOS. If `brew --version` works, use Homebrew:

```
brew install gh
```

If `brew` is not found, this is the later step that needs it. Homebrew is
installed by the hand-off in step 2, which asks for the person's password;
use that one and do not write a second installer block.

Windows PowerShell:

```
winget install --id GitHub.cli --source winget
```

When it finishes, `gh` is not yet on this terminal's search path. Tell the
person to open a new terminal window, or restart Claude Code in one, and
carry on from there.

Linux. On Debian, Raspberry Pi OS and Ubuntu, the vendor's install is one
block that uses `sudo`, so do not run it yourself.

> **Hand this to the person.** "Please paste this whole block into your own
> terminal window, type your password when it asks, and tell me when it
> finishes."
>
> ```
> (type -p wget >/dev/null || (sudo apt update && sudo apt install wget -y)) \
>   && sudo mkdir -p -m 755 /etc/apt/keyrings \
>   && out=$(mktemp) && wget -nv -O$out https://cli.github.com/packages/githubcli-archive-keyring.gpg \
>   && cat $out | sudo tee /etc/apt/keyrings/githubcli-archive-keyring.gpg > /dev/null \
>   && sudo chmod go+r /etc/apt/keyrings/githubcli-archive-keyring.gpg \
>   && sudo mkdir -p -m 755 /etc/apt/sources.list.d \
>   && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | sudo tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
>   && sudo apt update \
>   && sudo apt install gh -y
> ```

On Fedora, the vendor's install is three lines that use `sudo`. Hand them
over the same way:

> **Hand this to the person.** "Please paste these three lines into your own
> terminal window, one after another, type your password when it asks, and
> tell me when they finish."
>
> ```
> sudo dnf install dnf5-plugins
> sudo dnf config-manager addrepo --from-repofile=https://cli.github.com/packages/rpm/gh-cli.repo
> sudo dnf install gh
> ```

On any other distribution, stop and ask the person how they install
programs, as step 3 does. Run `gh --version` again once it is installed.

Then the sign-in, which is the person's:

> **Hand this to the person.** "Please run this line in your own terminal
> window (not this one). When it asks, choose GitHub.com, then HTTPS, and say
> yes to signing git in with GitHub too, then choose to log in with a web
> browser. It shows a short code: copy it, press Enter, paste the code into
> the page that opens, and approve. Tell me when the terminal says you are
> logged in."
>
> ```
> gh auth login
> ```

HTTPS is the choice here on purpose: the next command is for HTTPS. When they
say it is done, run it:

```
gh auth setup-git
```

It is safe to run again. It edits `~/.gitconfig`, the person's own git
settings file, so that git uses the `gh` sign-in when it pushes to GitHub:
say so, and remember it for the closing summary. If it says there are no
authenticated hosts, the sign-in was not finished: hand them `gh auth login`
again. Record **gh signed in: yes**.

(c) The code is anywhere else: GitLab, Bitbucket, a company server, GitHub
Enterprise at the person's own address, or nowhere yet. Skip `gh`. Say
plainly what that turns off: the runner still works on tickets, but it will
not read whether a build passed, notice that a pull request was merged, or
merge approved ones. Pushing is the forge's own business: the person follows
its instructions for a token or an SSH key, in their own terminal. Record
**gh signed in: not applicable**.

**Check.** For (b), `gh auth status` must exit 0 and print a line such as
`Logged in to github.com account <name>`, with the person's account name in
place of `<name>`. Quote that line only. Do not use `gh auth status --json`,
which exits 0 whether or not anyone is signed in. Then this must list a line
that includes `gh auth git-credential`:

```
git config --global --get-regexp "credential.*helper"
```

Tell the person `gh` is signed in and that git will push with it. For (c)
there is nothing to run: the check is the person's own words and the recorded
answer, and step 10's `git push --dry-run` is the real test.

**If you see** `gh: command not found` after the install, the terminal has
not picked up the new program: on Windows open a new terminal; on macOS after
Homebrew, run the `shellenv` line from step 2. If it still fails, stop and
say what you saw.

**If you see** that the person wants SSH instead of HTTPS, that is allowed.
Skip `gh auth setup-git`; the check is `gh auth status` alone.

**If you see** the code rejected, or the sign-in timed out, hand them
`gh auth login` again and check again. Do this at most twice, then stop and
say what you saw.

**If you see** that the code is in a work or school organization that asks
for single sign-on, stop. Authorising `gh` for that organization is the
person's, or their administrator's, to do. Say so, and do not try to get
around it.

Reference: [GitHub CLI](https://cli.github.com/),
[gh auth login](https://cli.github.com/manual/gh_auth_login),
[gh auth status](https://cli.github.com/manual/gh_auth_status),
[gh auth setup-git](https://cli.github.com/manual/gh_auth_setup-git),
[Linux install](https://github.com/cli/cli/blob/trunk/docs/install_linux.md),
[Windows install](https://github.com/cli/cli/blob/trunk/docs/install_windows.md),
[creating an account on GitHub](https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github).

### 9. Who git says they are

**What this is.** Every commit (a saved change in git's history) is stamped
with a name and an email address. The commits agents make carry whatever git
is set to on this machine, and git either refuses to save or guesses when
nothing is set.

**Tell the person.** "Next I will check what name and email address git puts
on the changes it saves. It is one question and two short commands, a few
seconds. Whatever you choose will appear on the changes agents make for you,
and on GitHub it is public if the repository is."

**Do it.** Read first, and ask nothing yet:

```
git config --global user.name
git config --global user.email
```

These are the same in PowerShell. An empty output with exit status 1 means
that setting is not set.

If both print something, tell the person what they are and change nothing:
they may be deliberate. Go to the check below.

If either is empty, ask only for what is missing, one question at a time:
"What name should appear on the changes agents save?" and "What email
address?" Never take either from the machine's user name, from `claude auth
status` or from `gh`: the person decides. For the email, mention that GitHub
offers a private "noreply" address. It is shown on GitHub under **Settings →
Emails** once **Keep my email address private** is ticked, and it looks like
`ID+USERNAME@users.noreply.github.com`. You cannot read that page, so the
person reads it out to you. Then set exactly what they said, in double
quotes, leaving a setting that was already filled in alone:

```
git config --global user.name "Their Name"
git config --global user.email "their-address@example.com"
```

These are the same in PowerShell, and the address above is a placeholder, not
a real one. Say that this edits `~/.gitconfig`, the person's own git settings
file, and remember it for the closing summary.

**Check.** Run the two read commands again. Both must print the values just
set, and neither may be empty. Tell the person the two values back.

**If you see** a name with a quote or a backslash in it, pass it whole inside
the double quotes and stop only if git reports an error. If a value contains
`$` or a backtick, use single quotes instead, which both shells take
literally.

**If you see** `error: could not lock config file`, the file is read-only or
the home folder is not writable. Stop and say what you saw.

**If you see**, in a later step, a push refused with `GH007` ("push would
publish a private email address"), the email is one GitHub keeps private. Ask
the person for their noreply address as above, set it with the second command
above, and push again.

Reference: [git config](https://git-scm.com/docs/git-config),
[setting your commit email address](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address),
[email address reference](https://docs.github.com/en/account-and-profile/reference/email-addresses-reference).

### 10. The repository agents work on

**What this is.** The runner works inside a git checkout: a folder on this
machine that is a copy of a repository and is linked back to where it came
from. That link is a remote called `origin`. The trunk is the repository's
main branch, which the runner resets to before it starts each piece of work.
So the folder needs an `origin` and a trunk with at least one commit in it.

**Tell the person.** "Last for this part: which repository should agents work
on? One you already have, or shall I make a new empty private one on your
GitHub? A new one is a good first try. Agents will make branches and commits
and push them there. It goes in your home folder beside the `hatch` folder,
never inside it, because that one is the board's own source."

**Do it.** Ask which, and ask for a folder name made of letters, digits and
hyphens, other than `hatch`. Suggest `agent-playground`. The folder is
`$HOME/<folder-name>` (`$env:USERPROFILE\<folder-name>` in PowerShell),
with the person's folder name in place of `<folder-name>`. Look at it first,
and never delete or overwrite it. If `git -C <folder> remote get-url origin` succeeds, it is a checkout
already: reuse it, do not clone again, and go to the check. If the folder
exists and that fails, stop and ask for another name.

Take the first case that fits. Which of (A) and (B) to offer depends on the
answer recorded as **gh signed in** in step 8.

(A) Clone one of the person's repositories. With `gh signed in: yes`, ask for
it as `owner/name`; `gh repo list --limit 20` lists some, to jog their
memory. Then, from the home folder:

macOS and Linux:

```
cd "$HOME" && gh repo clone <owner>/<name> <folder-name>
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE; gh repo clone <owner>/<name> <folder-name>
```

Otherwise ask for the repository's address and clone it from the home
folder, as step 4 does, with `git clone <address> <folder-name>`. If the
clone asks for a password or cannot sign in, stop. Never type a credential.

(B) Make a new empty private repository. This needs `gh signed in: yes`; if
it is not, the person makes an empty repository on their forge's own site,
following its page, and you go to (A) with its address. This creates
something on their account, so say the name and that it will be private, and
wait for a yes first. If they say no, stop. Then, from the home folder:

macOS and Linux:

```
cd "$HOME" && gh repo create <name> --private --add-readme --clone
```

Windows PowerShell:

```
Set-Location $env:USERPROFILE; gh repo create <name> --private --add-readme --clone
```

`--add-readme` makes the first commit, on GitHub, and `--clone` then copies
it down, which sets up `origin` and the trunk. Leave `--add-readme` out and
`gh` only runs `git init` and `git remote add`: there is no commit and no
trunk. The folder is named after the repository, so the repository name is
the folder name. If the name is already taken, ask for another.

(C) The clone is empty: git said `You appear to have cloned an empty
repository`, or `git -C <folder> rev-parse HEAD` fails. The trunk needs a
first commit, and pushing it needs steps 8 and 9. Ask the person before
pushing, since it puts a file on their account. Then, with the real folder
and name in place of the two placeholders:

macOS and Linux:

```
cd <folder> && printf '# <name>\n' > README.md && git add README.md && git commit -m "Start the repository" && git push -u origin HEAD && git remote set-head origin -a
```

Windows PowerShell, with the folder's path in quotes:

```
Set-Location <folder>; Set-Content README.md '# <name>'; git add README.md; git commit -m "Start the repository"; git push -u origin HEAD; git remote set-head origin -a
```

If the push is refused or asks for a credential, stop.

**Check.** Run all three, each as `git -C <folder> ...`, and every one must
pass:

1. `git -C <folder> remote get-url origin` prints an address.
2. `git -C <folder> rev-parse --abbrev-ref origin/HEAD` prints something
   like `origin/main`. On `unknown revision`, run `git -C <folder> remote
   set-head origin -a` and this again. If that says `Cannot determine remote
   HEAD`, the remote is empty: go to (C). The runner needs both this and a
   commit on the remote.
3. `git -C <folder> push --dry-run origin HEAD` prints `Everything
   up-to-date`, or a list of what it would push, and no error. It contacts
   the remote, so it proves the person can push, which the first two cannot.
   If it is refused, stop and say which message it gave.

Then say, once: "The repository folder is `<path>`. Remember this folder:
later steps call it the repository folder from step 10." The path is the
output of `git -C <folder> rev-parse --show-toplevel`, in full.

**If you see** `Repository not found`, the name is wrong, or the repository
is private and this account cannot see it. Stop, and say what you saw.

**If you see** `destination path ... already exists and is not an empty
directory`, never remove it. Ask for another folder name.

**If you see** that the person wants the repository inside the `hatch`
folder, say no: it goes beside it, because that folder is the board's own
source.

**If you see** `Permission to ... denied` or a `403` on the push, the account
has no write access to that repository. Stop, and say so.

Reference: [gh repo create](https://cli.github.com/manual/gh_repo_create),
[gh repo clone](https://cli.github.com/manual/gh_repo_clone),
[git clone](https://git-scm.com/docs/git-clone),
[git remote](https://git-scm.com/docs/git-remote),
[git push](https://git-scm.com/docs/git-push).

### 11. The runner

**What this is.** The runner is the program that watches the board for work
and starts an agent on it. It is one file, `hatch`, and the person's own board
serves it: the board lists what it has at `/api/hatch/runner` and hands over
one build at `/api/hatch/runner/download/` followed by a platform name. That
name is a RID (runtime identifier), which is just the operating system and
chip joined by a hyphen, such as `osx-arm64`. The search path, or `PATH`, is
the list of folders a terminal looks in when a command is typed by name. The
runner goes in the same folder step 7 used for `claude`: `$HOME/.local/bin`
on macOS and Linux, `%USERPROFILE%\.local\bin` on Windows. It is put there
for this person only, so it needs no password.

**Tell the person.** "Next I am going to download the Hatch runner from your
own board and put it in your user folder, beside `claude`. It is one small
file and takes a few seconds. I may also need to add that folder to your
terminal's search path so that typing `hatch` finds it; if so I will ask
first. At the end I will ask you to open a new terminal window and try one
command."

**Do it.** In each command `<port>` is the port step 5 settled on, and
`<rid>` is the platform name chosen in the second part below. Do the parts in
this order.

First, is it already there? If it prints `hatch - the house tracker, from a
terminal` as its first line, say it is already installed, never replace it,
and go to the **PATH** part below.

macOS and Linux:

```
hatch --help
```

Windows PowerShell, which first asks whether the name is found at all:

```
Get-Command hatch -ErrorAction SilentlyContinue; hatch --help
```

Second, choose the RID from what step 1 printed.

- macOS: `arm64` is `osx-arm64`, and `x86_64` is `osx-x64`.
- Windows: `AMD64` is `win-x64`.
- Linux: `x86_64` is `linux-x64`.
- **Linux `aarch64`**: stop. There is no build of the runner for Linux on
  Arm. Tell the person the guide cannot finish on this machine: the board is
  up and works, and the board's name and first project can still be set up by
  hand in the browser, but the runner cannot be installed here.
- **Windows `ARM64`, with a build number under 22000** (the third number of
  the version step 1 printed, after Major and Minor): stop. Windows 10 on Arm
  can only run 32-bit x86 programs, and the runner is 64-bit. Tell the person
  the guide cannot finish on this machine and what is done so far.
- **Windows `ARM64`, with a build number of 22000 or higher**: use `win-x64`.
  Windows 11 on Arm runs x64 programs by emulation (it translates them as they
  run) with nothing to install. Tell the person the runner will run under
  emulation, which is fine for a command-line tool
  ([How emulation works on Arm](https://learn.microsoft.com/windows/arm/apps-on-arm-x86-emulation)).

Third, ask the board what it has. The reply is JSON, such as
`{"revision":"dev","downloads":[{"rid":"osx-arm64",...}]}`, and the check is
whether the chosen RID is in it. Read the output, never the exit status.

macOS and Linux:

```
curl -fsS http://localhost:<port>/api/hatch/runner | grep -q '"rid":"<rid>"' && echo offered || echo missing
```

Windows PowerShell, where the RID is always `win-x64`:

```
(Invoke-RestMethod http://localhost:<port>/api/hatch/runner).downloads.rid -contains 'win-x64'
```

`offered` or `True` means go on. `missing` or `False` means stop and say "this
board's build has no runner for your machine". If `curl` cannot connect, the
board is not answering: go back to step 5 and check it again.

Fourth, download and install. First look at what is in the way, and touch no
other folder. On macOS and Linux, `ls -l "$HOME/.local/bin/hatch"`; on
Windows, `Get-Item "$env:USERPROFILE\.local\bin\hatch.exe"`. A file there
that did not answer in the first part may be replaced: say so when you do.
The download goes to a temporary name and is moved into place only after it
has fully arrived, so a failed download never leaves a broken `hatch` behind.

macOS and Linux:

```
mkdir -p "$HOME/.local/bin" && curl -fsSL -o "$HOME/.local/bin/hatch.new" http://localhost:<port>/api/hatch/runner/download/<rid> && chmod +x "$HOME/.local/bin/hatch.new"
```

On macOS only, clear the "downloaded from the internet" mark if there is one.
`curl` never sets it, so this changes nothing in the usual case; it is for the
case under **If you see** below. It prints nothing either way:

```
xattr -d com.apple.quarantine "$HOME/.local/bin/hatch.new" 2>/dev/null; true
```

Then, on macOS and Linux, move it into place:

```
mv -f "$HOME/.local/bin/hatch.new" "$HOME/.local/bin/hatch"
```

Windows PowerShell:

```
$dir = "$env:USERPROFILE\.local\bin"; New-Item -ItemType Directory -Force $dir | Out-Null; Invoke-WebRequest -UseBasicParsing -OutFile "$dir\hatch.exe" http://localhost:<port>/api/hatch/runner/download/win-x64; Unblock-File "$dir\hatch.exe"
```

`Unblock-File` removes the downloaded-from-the-internet mark that would make
SmartScreen stop the program ([Unblock-File](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/unblock-file));
it does nothing where there is no mark. Use `curl` on macOS and never a
browser download: a browser sets that mark, and a marked runner is killed
without a message ([Gatekeeper and runtime protection](https://support.apple.com/guide/security/gatekeeper-and-runtime-protection-sec5599b66df/web)).

Fifth, run it by its path. The shell you are running commands in started
before the install, so it may not find a bare `hatch` yet.

macOS and Linux:

```
"$HOME/.local/bin/hatch" --help
```

Windows PowerShell:

```
& "$env:USERPROFILE\.local\bin\hatch.exe" --help
```

Say once: until a new terminal is opened, steps 12 to 14 call it by that path
if a bare `hatch` is not found.

Sixth, the **PATH**: can a fresh terminal find it by name?

On macOS and Linux, a login shell reads the person's profile the way a new
terminal does, so this is the honest check. If it prints the path to `hatch`,
the folder is already on the search path (perhaps step 7 did it): go on.

```
"$SHELL" -lic 'command -v hatch'
```

If it prints nothing, the folder is not on the search path. If step 7 already
added the `export PATH` line for `claude`, say so, and use that step's fix
again for the shell file the person really uses, since this shell did not read
it. Otherwise use the fix in step 7's first **If you see**: it changes the
person's own shell file, so say so, ask first, hand them the `echo` and
`source` lines there, and tell them afterwards what changed. Do not retype it
here.

On Windows, the folder goes on the user's search path, which changes only this
person's settings and needs no administrator. Say so, ask first, then:

```
$dir = "$env:USERPROFILE\.local\bin"; $u = [Environment]::GetEnvironmentVariable('Path','User'); if (($u -split ';') -notcontains $dir) { [Environment]::SetEnvironmentVariable('Path', (($u.TrimEnd(';')) + ';' + $dir).TrimStart(';'), 'User') }
```

[SetEnvironmentVariable](https://learn.microsoft.com/dotnet/api/system.environment.setenvironmentvariable)
writes the user's settings and tells Windows they changed, but a terminal
that is already open keeps its old search path. In Git Bash on Windows, run
each Windows block through `powershell.exe -NoProfile -Command "..."`.

Write down, for step 15: **changed the PATH: yes (a shell file, or the
Windows user path) / no**.

**Check.** All three must pass.

- (a) The command in the fifth part exits 0 (`echo $?` straight after it
  prints `0`; in PowerShell `$LASTEXITCODE` does) and its first line is
  `hatch - the house tracker, from a terminal`.
- (b) On macOS and Linux, the `"$SHELL" -lic 'command -v hatch'` line prints
  the path. On Windows,
  `[Environment]::GetEnvironmentVariable('Path','User')` contains the
  folder.
- (c) The person opens a **new** terminal window, runs `hatch --help`, and
  tells you it printed a list of commands. This one is theirs alone: a
  command you start inherits your shell's old search path, so only a new
  window shows the truth.

If (c) fails after (b) passed, their terminal is a different shell from the
one named by `$SHELL`. Stop, and say what they saw.

**If you see** `Killed`, exit 137, or "Apple could not verify" on macOS, the
runner carries the quarantine mark. Run
`xattr -d com.apple.quarantine "$HOME/.local/bin/hatch"` and check again.
Never advise turning Gatekeeper off or running `spctl --master-disable`. If it
is still killed after that, stop: the program's signature is missing.

**If you see** `curl: (22)` with a `404`, that build is not on this board:
stop. **If you see** `curl: (7)` or "connection refused", the board is not
answering: go back to step 5's check.

**If you see** `Permission denied` on macOS or Linux, the `chmod +x` did not
take: run it again on `"$HOME/.local/bin/hatch"` and check again.

**If you see** `command not found` or `is not recognized` for `hatch` in a
new terminal, the folder is not on the search path: do the PATH part above.

**If you see** SmartScreen or "blocked" on Windows, run `Unblock-File` on the
file again. If it persists, stop and say what you saw.

Reference: [curl manpage](https://curl.se/docs/manpage.html),
[Invoke-WebRequest](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/invoke-webrequest),
[Unblock-File](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/unblock-file),
[SetEnvironmentVariable](https://learn.microsoft.com/dotnet/api/system.environment.setenvironmentvariable),
[How emulation works on Arm](https://learn.microsoft.com/windows/arm/apps-on-arm-x86-emulation),
[Gatekeeper and runtime protection](https://support.apple.com/guide/security/gatekeeper-and-runtime-protection-sec5599b66df/web).

### 12. Point the runner at the board

**What this is.** The origin is the board's address, the part before any
path: `http://localhost:` followed by the port. The runner has to be told it
once. It keeps it in a small per-user file outside every repository, so
telling it changes nothing in the person's code and is not repeated for each
project. The board you started has its wall off, which means it asks nobody
who they are, so there is no key to give the runner yet.

**Tell the person.** "Next I am going to tell the Hatch runner where your
board is. It is one short command and takes a second. It writes the address
into a small settings file in your user folder, and then it asks the board
whether it can be reached. You will see a line about a missing key; that is
expected, because your board does not ask for one."

**Do it.** In each command `<port>` is the port step 5 settled on. Where a
bare `hatch` was not found in step 11's fifth part, use that step's path form
in its place: `"$HOME/.local/bin/hatch"` on macOS and Linux, and
`& "$env:USERPROFILE\.local\bin\hatch.exe"` on Windows PowerShell.

First look at what is already set:

```
hatch config --show
```

Read only the line that starts `HATCH_BASE:` aloud. Never read, quote or copy
a `HATCH_KEY:` line: it prints the start of a key. If `HATCH_BASE` is set to
an address other than `http://localhost:<port>`, ask the person before
changing it, since changing it points the runner at a different board.

Then write the origin. It asks nothing, and it is the same on every system.

```
hatch config --origin http://localhost:<port>
```

Expect three things, in this order. First `wrote` and the path of the
settings file. Then, on the error stream, `hatch: no key - calls will name
themselves` followed by a name in quotes, and `which only a Hatch with its
wall off reads.` **That line is correct and is not an error**: tell the
person so, and do not try to add a key. Last, `reached http://localhost:`
followed by the port, and a list of the board's columns.

**Check.** The command exited 0, and one of its lines begins `reached` and
names `http://localhost:<port>`. Quote that address to the person, not the
list of columns. Write down the path on the `wrote` line, for step 15.

**If you see** `has no scheme`, the address lost its `http://`. Run the
command again with it.

**If you see** `the file is written but that call did not go through`, the
command exited 1: the board is not answering. Go back to step 5's poll and
stop there.

**If you see** `reached` naming a different address from the one you wrote,
an exported `HATCH_BASE` in this shell is overriding the file. `hatch config
--show` names the layer it came from. Stop, and say so.

Reference: `hatch config -h` lists everything the command can set.

### 13. Their name, and a first project

**What this is.** A board with its wall off does not ask who you are, so it
signs what you do with your shell's user name, or `friend` if there is none,
on every comment and every move, for good. The person can change that now,
and it is worth doing before anything is filed. A project is a key
namespace: a short key such as `HOME` makes every issue under it numbered
from that key, `HOME-1`, `HOME-2`, and so on. One board holds as many
projects as the person makes. Both are done in the browser, by the person.

**Tell the person.** "Last for the board itself: two small things in your
browser. First, the name your work is signed with, so that the board's
history says who did what. Second, a first project, which is just a short
word in capital letters that your tasks will be numbered from. I will tell
you exactly where to click. Then I will check it from here."

**Do it.** First ask the person for two things in plain words: the name to
sign their work with, and a short key for their first project, two to six
characters starting with a letter, such as `HOME`, and a name for the
project. Then hand over the browser:

> **Hand this to the person.** "In the board, do these two things. (1) Open
> the gear menu at the right of the top bar and choose **Settings**. Find
> the card called **Your name**, type the name you want your work signed
> with in its **Name** box, and press **Save name**. (2) Press **Manage** in
> the top bar (on a narrow window it is inside the gear menu instead), then
> **Projects**. Under **New project**, type the **Key** and the **Name**,
> and press **Add project**. Tell me when both are done."

Do not bind a repository to the project. An unbound project is worked from
whichever checkout the runner is started in, and that will be the repository
folder from step 10. Tell the person that in one sentence. If they have more
than one repository, the same Projects page can bind a project to one of
them, and that can wait.

**Check.** Both parts must pass.

- (a) The person says the top bar now shows their name and the project is
  listed on the Projects page.
- (b) You confirm it from the board's own answers, which a board with its
  wall off gives to anyone. In each command `<port>` is step 5's port. The
  first output must contain `"key":"` followed by the key, and the second
  must show the name.

macOS and Linux:

```
curl -s http://localhost:<port>/api/hatch/projects
curl -s http://localhost:<port>/api/hatch/settings | grep -o '"localPersonName":"[^"]*"'
```

Windows PowerShell:

```
(Invoke-RestMethod http://localhost:<port>/api/hatch/projects).key
(Invoke-RestMethod http://localhost:<port>/api/hatch/settings).localPersonName
```

Never print the whole settings reply: it has a field for a token. Print only
the name, as above.

Write down the project's key and the name, for step 15.

**If you see** no **Your name** card on the Settings page, the board has its
wall up, which this guide did not set up. Stop, and say so.

**If you see** the key refused, with "Two to six characters" or a message
that it is already in use, ask the person for another key and have them try
again.

### 14. The proof

**What this is.** The runner asks the board what is waiting, from inside the
checkout agents will work in. If it answers, the runner, the address from step
12 and the board all agree. This proves the runner reaches the board from that
folder. It does not prove that a ticket will run, and it does not check the
folder itself.

**Tell the person.** "Now one last check that the pieces talk to each other.
I am going to ask your board, from your repository folder, what is waiting to
be worked. A new board has nothing waiting, so a short sentence saying so is
the answer I am hoping for."

**Do it.** From the repository folder from step 10, use the path that step
printed in place of `<folder>`. Where a bare `hatch` was not found in step
11's fifth part, use that step's path form in its place, as step 12 does:
`"$HOME/.local/bin/hatch"` on macOS and Linux, and
`& "$env:USERPROFILE\.local\bin\hatch.exe"` on Windows PowerShell.

macOS and Linux:

```
cd <folder> && hatch queue
```

Windows PowerShell:

```
Set-Location <folder>; hatch queue
```

**Check.** The command exited 0 and printed one of two things. Either a list
of lines like `HOME-1  [task]  Breakdown  ...`, one for each ticket the runner
would look at, or exactly `hatch: nothing on the board is on the dispatcher's
path`. A fresh board with a project and no tickets prints that sentence, and
that is a pass. Quote only the first line of a list, or the sentence, to the
person. Any other line that starts `hatch:`, or a non-zero exit, is a stop.

**If you see** `hatch: not configured - it needs an origin.`, step 12 was not
done, or this shell has no settings. Go back to step 12 and stop there.

**If you see** `hatch: ` followed by the address and `is not answering -
waiting up to 90s before giving up`, and then a connection error, the board is
down. Go back to step 5's poll and stop there.

**If you see** that `hatch` is not found, this shell predates the install in
step 11 that changed the PATH. Use the path form above, and if that fails too,
stop and say what you saw.

**If you see** `cd` or `Set-Location` say it cannot find the folder, the path
from step 10 was mistyped. Copy it again from what step 10 printed.

Reference: `hatch queue -h` lists what the command can narrow to.

### 15. The summary

**What this is.** The walkthrough is over, and the person has watched a lot
of it scroll past. This step is the one place that says, in order, where the
board is, what is now on their machine, what was changed on it, and what is
left for them to do. Nothing here is run. It only says what this walkthrough
did, so it leaves out anything it did not do.

**Tell the person.** "That is everything I can set up for you. Before we
stop, here is a plain account of what is where, what I changed, and the
three things that are yours to do next. Tell me if any of it is unclear."

**Do it.** Say the following, in the person's terms and in this order, drawn
only from what this walkthrough did. A program that was already there is
"already there", not "installed". Leave out any line for something that did
not happen.

1. **The board.** It is open at `http://localhost:<port>/apps/hatch/`, with
   `<port>` the one step 5 settled on.
2. **What was installed**, only what this run did: Git (step 2), Docker
   (step 3), Hatch's source in `~/hatch` and the running board (steps 4 and
   5), the `claude` program (step 7), `gh` (step 8), and the `hatch` runner
   (step 11).
3. **What was changed**, again only what was done:
   - `~/hatch/.env`, which holds the port (step 5, if a line was written);
   - the line in the shell file, or the Windows user `PATH`, that puts a
     program on the search path (steps 7 and 11, where each **changed the
     PATH**);
   - git's name and email in `~/.gitconfig` (step 9);
   - `gh`'s credential helper in git (step 8, if `gh auth setup-git` ran);
   - the per-user `hatch` settings file, at the path on step 12's `wrote`
     line;
   - the repository folder from step 10, and the new private repository on
     their account if step 10 made one;
   - on the board, their name and the project, with the key and name from
     step 13.
4. **What is now theirs to do**, one plain line each, in this order. None of
   it is done by Claude:
   - (a) Write a first ticket in **Draft** and drag it to **Breakdown**, the
     first column an agent owns.
   - (b) Open **Docs** in the gear menu, copy the section **The `CLAUDE.md`
     block** into a file called `CLAUDE.md` in the repository folder from
     step 10, then commit it and push it to the trunk. The runner starts
     each piece of work from the trunk, so the file has to be there.
   - (c) In their own terminal, inside that folder, run `hatch go-to-work
     --once`. It starts an agent on the ticket, and that spends their Claude
     usage.
5. **Close with this**, in your own words but with this meaning: Claude never
   runs `go-to-work` itself, here or later. It can explain (a) to (c), and
   answer questions about them, but it does not do (c).

**Check.** There is nothing to run. The check is that the summary named the
address, the three headings (what was installed, what was changed, and what
is theirs to do) and the three actions (a) to (c), and that the person says
they understood or asks a question. Answer any question, and do not start
(c) for them. There is no failure to name in this step, so it has no **If you
see** and no **Reference**.
