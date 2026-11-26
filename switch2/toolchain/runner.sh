#!/bin/sh
# Build-host loop for the NAS stack: runs each /work/jobs/<name>.sh once, in name order, from
# /work/pkforge, writing <name>.log and then <name>.exit. nas-job.ps1 on the PC writes the jobs.
# Anyone who can write to the jobs folder can run commands in this container (not on the NAS itself).
mkdir -p /work/jobs
cd /work/jobs || exit 1
date -u +"runner up %Y-%m-%dT%H:%M:%SZ" > runner.status
while true; do
  for job in *.sh; do
    [ -e "$job" ] || continue
    name=${job%.sh}
    [ -e "$name.exit" ] && continue
    (cd /work/pkforge && sh "/work/jobs/$job") > "$name.log" 2>&1
    echo $? > "$name.exit"
  done
  sleep 3
done
