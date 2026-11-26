# Mystery-gift packs

The Switch build ships these packs instead of the Android app's (`src/PKForge.App/Resources/Events`, packed 2026-08-29), so it offers newer events. They hold projectpokemon's EventsGallery released wondercards, packed by `tools/EventGen/pack.py`.

- Source: EventsGallery commit `f1f3095183ee54dca63216164adb1f278714ec55` (2026-11-13, "Add HOME Celebi (#606)").
- Gen 9: 267 gifts (Android's pack: 244), including `1613 ZA - Kalos Shiny Xerneas.wa9`.

To refresh them, pack the latest EventsGallery on the NAS:

```powershell
switch2\toolchain\nas-job.ps1 "rm -rf output/eventsgallery output/events && git clone -q --depth 1 https://github.com/projectpokemon/EventsGallery output/eventsgallery && git -C output/eventsgallery log -1 --format='%H %cs %s' && python3 -I tools/EventGen/pack.py output/eventsgallery output/events"
```

Then copy `output/events/*.bin.gz` from the NAS share into this folder and update the source line above. The checkout is untrusted data: only `pack.py` reads it, as files. On the console, the app clears the events an older pack unpacked (`AppHost.ClearStaleEvents`).
