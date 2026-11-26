<#
Runs a command on the NAS build host (the pkforge-switch2 Docker Compose stack) and prints the end of its log.
  switch2\toolchain\nas-job.ps1 "make -C switch2/hello"
  switch2\toolchain\nas-job.ps1 "sh switch2/runtime/build-mono.sh" -Detach   # long builds: frees the runner at once
The repo is mirrored to the NAS first, so the build sees the PC's files. Exits with the command's exit code.
With -Detach the command runs in the background on the NAS, so the runner can take other jobs meanwhile;
this script still waits for it and prints its log, unless -NoWait is given.
-Stack defaults to the PKF_NAS_STACK environment variable: the stack's folder on the NAS share.
#>
param(
    [Parameter(Mandatory)][string]$Command,
    [string]$Stack = $env:PKF_NAS_STACK,
    [int]$TimeoutMinutes = 720,
    [int]$Tail = 40,
    [switch]$Detach,
    [switch]$NoWait
)
$ErrorActionPreference = 'Stop'
if (-not $Stack) { throw 'Set PKF_NAS_STACK, or pass -Stack, to the stack folder on the NAS share.' }
$repo =(Resolve-Path "$PSScriptRoot\..\..").Path
$jobs = Join-Path $Stack 'work\jobs'

# Build outputs made on the NAS are excluded, so the mirror neither overwrites nor deletes them.
# The container must not create other folders in the mirrored tree: the SMB user cannot write into them.
$mirror = robocopy $repo (Join-Path $Stack 'work\pkforge') /MIR /XD .git .superpowers build obj bin output findings romfs-big `
    /XF *.nro *.elf *.nacp *.nso *.map /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
# robocopy can report an access error and still exit 0, so its error lines are checked too.
$errors = $mirror | Where-Object { $_ -match ' ERROR \d+ ' }
if ($LASTEXITCODE -ge 8 -or $errors) { throw "robocopy failed ($LASTEXITCODE): $($errors -join ' | ')" }

$name = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + (Get-Random -Maximum 10000)
$script = "set -e`n$Command`n"   # LF line endings for sh
if ($Detach) {
    # The command goes in <name>.cmd; the job only starts it in the background and returns.
    [IO.File]::WriteAllText((Join-Path $jobs "$name.cmd"), $script)
    $script = "nohup sh -c 'cd /work/pkforge && sh /work/jobs/$name.cmd > /work/jobs/$name.log 2>&1; echo `$? > /work/jobs/$name.exit' > /dev/null 2>&1 &`n"
    $name = "$name-start"
}
$tmp = Join-Path $jobs "$name.tmp"
[IO.File]::WriteAllText($tmp, $script)
Rename-Item $tmp "$name.sh"                            # the runner only picks up whole .sh files
if ($Detach) { $name = $name -replace '-start$', '' }
if ($NoWait) { "job $name queued (log: $jobs\$name.log)"; exit 0 }

$exitFile = Join-Path $jobs "$name.exit"
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while (-not (Test-Path $exitFile)) {
    if ((Get-Date) -gt $deadline) { throw "job $name timed out; log: $jobs\$name.log" }
    Start-Sleep -Seconds 5
}
Get-Content (Join-Path $jobs "$name.log") -Tail $Tail
$code = [int]((Get-Content $exitFile -Raw).Trim())
"job $name exited $code (log: $jobs\$name.log)"
exit $code
