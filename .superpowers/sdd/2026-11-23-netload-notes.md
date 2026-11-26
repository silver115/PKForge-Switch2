# Netload test runs: what breaks a run started after another in the same process (2026-11-23)

The loader (ounce-hbloader) starts a netloaded build in the process of the run before it. Heap addresses per run
(mono.txt "libnx heap"): every run that crashed at "Loading assembly" had exactly the heap address of the run before
it (runs 49, 51, 53, 56: the same build sent twice in a row); runs with a different address worked (47: another
build; 55). Sockets were a false lead (run 55 used none and run 56 still died). Likely cause: the same build in the
same layout, so the old run's sleeping Mono threads are woken through the addresses they wait on.

Tried: pausing every thread the run started (a --wrap=pthread_create record, svcSetThreadActivity) before going back
to the loader: run 57 passed its script, then the console showed "an error occurred" on the way back; reverted.
Workaround: start the loader again from HOME before sending the same build a second time.

A report run (sockets after Mono, then back to the loader) passed its script and sent its report, then showed "an error
occurred" on the black screen and went to HOME (2026-11-23 03:16 UTC); run 52 had gone back fine. So report runs close
the whole application again; plain runs go back to the loader.

Second pause attempt (2026-11-23 06:4x UTC, run 60): pausing at the last exit handler (atexit registered first in main)
instead of right after Mono's cleanup. Run 60 passed its script, then "an error occurred" on the way back, as run 57.
Pausing the old threads breaks the way back however late it runs; reverted. The loop stays at one netloader press per
run, with the loader started again from HOME before the same build is sent twice.
