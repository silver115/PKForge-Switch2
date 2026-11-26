// MauiLite keeps process-wide state, as MAUI does (the application, the UI loop, fonts, the display density),
// so its tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
