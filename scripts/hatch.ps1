# Hatch from a Windows terminal, in this repository.
#
# The PowerShell twin of scripts/hatch.sh, and it does the same two things for
# the same reasons; read the header there for why they are here and not in the
# program.
#
#   - The door. `.\scripts\hatch.ps1 <anything>` is every command of the `hatch`
#     program in src\Hatch.Cli - board, next, queue, show, start, move, comment,
#     pr, depends, ask, questions, answer, api, config, work, go-to-work - with
#     the arguments passed through whole and the program's own exit code handed
#     back. This file finds the program, builds it if it has to, and hands over.
#
#   - The supervisor. `go-to-work` asks to be restarted as a newer build by
#     exiting 75, and nothing else on Windows catches that: a downloaded
#     hatch.exe runs the loop it started as, all night. Here the loop is run,
#     the 75 is caught, the source is built again, and the loop comes back.
#
# Two things are here that hatch.sh has no need of, both because Windows is
# Windows:
#
#   - Windows will not overwrite a file while a process is running it, and a
#     session that works a Hatch ticket builds src\Hatch.Cli in this same
#     checkout. So `work` and `go-to-work` run a COPY of the build, made under
#     %TEMP% and removed at the end, and the loop holds no file under this
#     checkout open.
#
#   - Windows PowerShell 5.1 passes a native command's arguments wrongly when
#     they contain a double quote, so the program is launched through
#     System.Diagnostics.Process, with the arguments quoted here.
#
# Settings are the program's, as in hatch.sh: an exported variable wins, then
# scripts\.env in this checkout, then the per-user file `hatch config` writes.
# Never a key in this repository.
#
#   .\scripts\hatch.ps1 --help     every command, from the program itself
#   .\scripts\hatch.ps1 <command>  ...and running one
#
# Where the execution policy refuses scripts, which is the default on a Windows
# client, run it as:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\hatch.ps1 <command>
#
# One wart is PowerShell's and cannot be worked around here: an argument that
# starts with a dash and contains a colon (`--until=23:59`) arrives split in
# two. The program takes its flags as `--name value`, so none of them has that
# shape - but a comment body of that shape cannot be passed through this file.
#
# Windows PowerShell 5.1 and PowerShell 7 on purpose - the one a Windows machine
# ships with, and the one people install. So no `??`, no ternary, no `&&`
# between pipelines, and nothing but ASCII in this file: 5.1 reads a file with no
# BOM in the ANSI code page. There is no param() block either, because it would
# bind or refuse the program's own `-h` and `--help`; `$args` is taken whole.

$ErrorActionPreference = 'Stop'

# Where the repository is, whatever directory this was invoked from.
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$Project = [IO.Path]::Combine($Root, 'src', 'Hatch.Cli', 'Hatch.Cli.csproj')

# Function scope has its own $args, so take this one now.
$Arguments = @($args)

# What the tidy-up in the `finally` at the bottom has to remove.
$script:NightState = $null   # the file the night's totals travel in
$script:RunCopy = $null      # the copy of the build being run
$script:Making = $null       # a copy that is not finished
$script:Copies = 0

function Write-Err([string]$Text) { [Console]::Error.WriteLine($Text) }

function Remove-Quietly([string]$Path) {
  if ($Path -and (Test-Path -LiteralPath $Path)) {
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
  }
}

# The CLI as a built binary if there is one, and `dotnet run` if there is not,
# as runner_cmd does. HATCH_RUNNER_BIN names one directly, for a machine with no
# SDK. Resolved again before every launch, since a rebuild between two of them
# can put a binary where there was only the SDK.
#
# Directory is the build's own directory, which is what gets copied; it is $null
# for a pinned binary, which is run where it is, and for `dotnet run`.
function Resolve-Runner {
  if ($env:HATCH_RUNNER_BIN) {
    if (-not (Test-Path -LiteralPath $env:HATCH_RUNNER_BIN -PathType Leaf)) {
      Write-Err "hatch: HATCH_RUNNER_BIN is not a file: $env:HATCH_RUNNER_BIN"
      exit 1
    }
    return [pscustomobject]@{
      Path = $env:HATCH_RUNNER_BIN; Prefix = @(); Directory = $null; Pinned = $true }
  }

  # Plain `hatch` after `hatch.exe`, so this can be run under pwsh on macOS and
  # Linux as well.
  foreach ($configuration in 'Release', 'Debug') {
    $directory = [IO.Path]::Combine($Root, 'src', 'Hatch.Cli', 'bin', $configuration, 'net10.0')
    foreach ($name in 'hatch.exe', 'hatch') {
      $bin = Join-Path $directory $name
      if (Test-Path -LiteralPath $bin -PathType Leaf) {
        return [pscustomobject]@{
          Path = $bin; Prefix = @(); Directory = $directory; Pinned = $false }
      }
    }
  }

  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) {
    Write-Err @'
hatch: needs either a build of src\Hatch.Cli or the dotnet SDK.

    dotnet build .\src\Hatch.Cli\Hatch.Cli.csproj --configuration Release
                              builds it once, and every command after is fast
    make build-hatch          ...the same, where there is make
    make publish-hatch        ...or one self-contained binary per platform
    $env:HATCH_RUNNER_BIN = '...' names one you already have
'@
    exit 1
  }

  return [pscustomobject]@{
    Path = $dotnet.Source; Directory = $null; Pinned = $false
    Prefix = @('run', '--project', $Project, '--') }
}

# One argument, quoted the way CommandLineToArgvW reads it back: in quotes when
# it is empty or holds whitespace or a double quote, each double quote escaped,
# and the backslashes that come before a double quote - or before the closing
# one - doubled. Only used where ProcessStartInfo has no ArgumentList (5.1).
function ConvertTo-CommandLineArgument([string]$Argument) {
  if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') { return $Argument }

  $quoted = New-Object System.Text.StringBuilder
  [void]$quoted.Append('"')
  $backslashes = 0
  foreach ($character in $Argument.ToCharArray()) {
    if ($character -eq [char]'\') {
      $backslashes++
      continue
    }
    if ($character -eq [char]'"') {
      [void]$quoted.Append([char]'\', $backslashes * 2 + 1).Append('"')
    } else {
      [void]$quoted.Append([char]'\', $backslashes).Append($character)
    }
    $backslashes = 0
  }
  [void]$quoted.Append([char]'\', $backslashes * 2).Append('"')
  return $quoted.ToString()
}

# Launch the runner and wait for it, returning its exit code. Nothing is
# redirected, so the console is the program's own and `config` can read a key
# without echoing it. Waiting here, and not handing the prompt back, is also what
# lets the program finish letting go of its ticket after a Ctrl+C.
function Invoke-Runner($Runner, [string[]]$Rest, [hashtable]$Environment) {
  $info = New-Object System.Diagnostics.ProcessStartInfo
  $info.FileName = $Runner.Path
  $info.UseShellExecute = $false
  $everything = @($Runner.Prefix) + @($Rest)

  if ($info.PSObject.Properties['ArgumentList']) {
    foreach ($argument in $everything) { $info.ArgumentList.Add($argument) }
  } else {
    $info.Arguments = (@($everything | ForEach-Object { ConvertTo-CommandLineArgument $_ })) -join ' '
  }

  foreach ($name in $Environment.Keys) { $info.EnvironmentVariables[$name] = $Environment[$name] }

  $process = [System.Diagnostics.Process]::Start($info)
  try {
    $process.WaitForExit()
    return [int]$process.ExitCode
  } finally {
    $process.Dispose()
  }
}

# The build in a fresh directory of its own under the temp path, named for this
# process. The whole directory: hatch.exe is an apphost that needs the files
# beside it.
function New-RunCopy([string]$Source) {
  $script:Copies++
  $destination = Join-Path ([IO.Path]::GetTempPath()) "hatch-run-$PID-$($script:Copies)"
  $script:Making = $destination
  try {
    [void](New-Item -ItemType Directory -Path $destination -Force)
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $destination -Recurse -Force
  } catch {
    Remove-Quietly $destination
    throw
  } finally {
    $script:Making = $null
  }
  return $destination
}

# The same runner, from a copy of its build - for `work` and `go-to-work`, which
# run for a long time, and not for the commands that finish in a second. A pinned
# binary is run where it is.
function Use-RunCopy($Runner) {
  if ($Runner.Pinned) { return $Runner }

  if (-not $Runner.Directory) {
    # Only the SDK was found, and there is nothing to copy. Building here leaves a
    # Release binary in the checkout that the next launch prefers; hatch.sh does
    # not, because it has nothing to copy.
    $dotnet = $Runner.Path
    & $dotnet build $Project --configuration Release | Out-Host
    if ($LASTEXITCODE -ne 0) {
      Write-Err 'hatch: could not build src\Hatch.Cli, so there is nothing to run'
      exit 1
    }
    $Runner = Resolve-Runner
    if (-not $Runner.Directory) {
      Write-Err 'hatch: the build of src\Hatch.Cli is not where it was expected'
      exit 1
    }
  }

  $script:RunCopy = New-RunCopy $Runner.Directory
  return [pscustomobject]@{
    Path = Join-Path $script:RunCopy (Split-Path -Leaf $Runner.Path)
    Prefix = @(); Directory = $script:RunCopy; Pinned = $false }
}

# The new source, compiled and copied - the one place in this file that builds
# anything after the first launch. Returns the runner to go round with.
#
# A build that fails is not the end of a night: the loop comes back on the copy
# that is there, and having taken its baseline at startup it will not ask again
# for the same change. So this says what went wrong and returns, always. Nothing
# is copied from a failed build, whose output may be half-written.
function Restart-Runner($Runner) {
  if ($Runner.Pinned) {
    # A machine with no SDK on it. Restart anyway: hatch.ps1 itself may be what
    # changed.
    Write-Err 'hatch: HATCH_RUNNER_BIN names the binary, so there is nothing here to rebuild'
    return $Runner
  }

  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) {
    Write-Err 'hatch: dotnet is not here - carrying on with the binary that is there'
    return $Runner
  }

  Write-Err 'hatch: rebuilding'
  & $dotnet.Source build $Project --configuration Release | Out-Host
  if ($LASTEXITCODE -ne 0) {
    Write-Err 'hatch: the rebuild failed - carrying on with the binary that is there'
    return $Runner
  }

  try {
    $fresh = Resolve-Runner
    if (-not $fresh.Directory) { throw 'the build is not where it was expected' }
    $old = $script:RunCopy
    $script:RunCopy = New-RunCopy $fresh.Directory
    # The runner that was in the old copy has exited, so it can go.
    Remove-Quietly $old
    return [pscustomobject]@{
      Path = Join-Path $script:RunCopy (Split-Path -Leaf $fresh.Path)
      Prefix = @(); Directory = $script:RunCopy; Pinned = $false }
  } catch {
    Write-Err "hatch: the new build could not be copied ($($_.Exception.Message)) - carrying on with the binary that is there"
    return $Runner
  }
}

# `go-to-work` is run rather than exec'd, for the reason hatch.sh gives: the
# runner asks to come back by exiting 75, and everything interesting - deciding
# to restart, letting go of the claim, carrying the night's totals - stays in the
# runner. The totals travel in a file this names once per night, and an empty
# one reads as a fresh night.
#
# Anything but 75 ends the night with that code. This function is not run again
# from inside itself, so a loop that restarts every half hour has no depth to run
# out of.
function Invoke-GoToWork([string[]]$Rest) {
  $script:NightState = [IO.Path]::GetTempFileName()
  $runner = Use-RunCopy (Resolve-Runner)

  while ($true) {
    $code = Invoke-Runner $runner (@('go-to-work') + @($Rest)) @{
      HATCH_ROOT = $Root; HATCH_NIGHT_STATE = $script:NightState }
    if ($code -ne 75) { return $code }

    $runner = Restart-Runner $runner
    Write-Err 'hatch: restarting'
  }
}

# `go-to-work` is the one command that is watched rather than replaced, and
# `work` the one other that runs from a copy. Everything else - including a name
# that is not a command at all, which the program refuses better than this file
# could - goes straight through, in place.
#
# The `finally` runs on `exit` and on Ctrl+C alike, which is what leaves nothing
# of this file's in %TEMP%.
$code = 0
try {
  $command = if ($Arguments.Count -gt 0) { $Arguments[0] } else { '' }
  $rest = if ($Arguments.Count -gt 1) { @($Arguments[1..($Arguments.Count - 1)]) } else { @() }

  if ($command -ceq 'go-to-work') {
    $code = Invoke-GoToWork $rest
  } elseif ($command -ceq 'work') {
    $runner = Use-RunCopy (Resolve-Runner)
    $code = Invoke-Runner $runner $Arguments @{ HATCH_ROOT = $Root }
  } else {
    $code = Invoke-Runner (Resolve-Runner) $Arguments @{ HATCH_ROOT = $Root }
  }
  exit $code
} finally {
  Remove-Quietly $script:NightState
  Remove-Quietly $script:Making
  Remove-Quietly $script:RunCopy
}
