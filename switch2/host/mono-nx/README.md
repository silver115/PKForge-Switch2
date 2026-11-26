# mono-nx host sources (vendored)

From [exelix11/mono-nx](https://github.com/exelix11/mono-nx) `native/shared`, at `285b277d` plus PKForge's patch
`switch2/runtime/patches/0001-mono-nx-unbuffered-file-log.patch` (the local fork's `ounce` head, `aa693e6`).
MIT, see `LICENSE`. `third_party/ini` is [inih](https://github.com/benhoyt/inih), BSD-3-Clause (see its header).

Local changes, each marked `PKForge` in the code:

- `heap.c` registers Mono's half of the heap with `pkf_register_mono_heap`, so the host's memory figures count it.
  It needs `mono_nx_fakemmap_used()`, which the `ounce-net10` fork branch adds.
- `core.c` skips ICU when `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is set, so no ICU data file has to ship.
- `dl_shim_dotnet.c` resolves `libSystem.Native`, `libSystem.Globalization.Native` and
  `libSystem.IO.Compression.Native` through the runtime's own tables (`*ResolveDllImport`, from each static
  library's `entrypoints.c`) instead of mono-nx's hand-written .NET 9 lists, which name functions .NET 10 dropped.
  The `SystemNative_Socket` hook stays.
- `system_native_stubs.c` (new) answers "not supported" for the three functions that table names but the libnx
  build leaves out.
