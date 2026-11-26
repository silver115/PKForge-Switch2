using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using PKForge.MauiLite;

namespace Microsoft.Maui
{
    /// <summary>MAUI's marker for an application.</summary>
    public interface IApplication
    {
    }

    /// <summary>MAUI's handle on the running app: its services and its application object.</summary>
    public interface IPlatformApplication
    {
        static IPlatformApplication? Current { get; set; }

        IServiceProvider Services { get; }

        IApplication Application { get; }
    }

    sealed class LitePlatformApplication(IServiceProvider services) : IPlatformApplication
    {
        public IServiceProvider Services => services;

        public IApplication Application => services.GetRequiredService<IApplication>();
    }
}

namespace Microsoft.Maui.Hosting
{
    public sealed class MauiApp : IDisposable
    {
        internal MauiApp(ServiceProvider services) => Services = services;

        public IServiceProvider Services { get; }

        public static MauiAppBuilder CreateBuilder(bool useDefaults = true) => new();

        public void Dispose() => (Services as IDisposable)?.Dispose();
    }

    public interface IFontCollection
    {
        IFontCollection AddFont(string filename, string? alias = null);
    }

    public sealed class MauiAppBuilder : IFontCollection
    {
        readonly List<(string File, string? Alias)> _fonts = [];

        internal MauiAppBuilder()
        {
        }

        public IServiceCollection Services { get; } = new ServiceCollection();

        IFontCollection IFontCollection.AddFont(string filename, string? alias)
        {
            _fonts.Add((filename, alias));
            return this;
        }

        internal void ConfigureFonts(Action<IFontCollection>? configure) => configure?.Invoke(this);

        public MauiApp Build()
        {
            LitePlatform.PlatformServices?.Invoke(Services);
            var provider = Services.BuildServiceProvider();
            // Fonts are package files, opened when first drawn.
            foreach (var (file, alias) in _fonts)
                FontRegistry.Register(file, alias ?? file, LitePackage.Open);
            IPlatformApplication.Current = new LitePlatformApplication(provider);
            return new MauiApp(provider);
        }
    }

    public static class FontsMauiAppBuilderExtensions
    {
        public static MauiAppBuilder ConfigureFonts(this MauiAppBuilder builder, Action<IFontCollection>? configureDelegate)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureFonts(configureDelegate);
            return builder;
        }
    }
}

namespace Microsoft.Maui.Controls.Hosting
{
    public static class AppHostBuilderExtensions
    {
        public static MauiAppBuilder UseMauiApp<TApp>(this MauiAppBuilder builder) where TApp : class, IApplication
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.AddSingleton<IApplication, TApp>();
            return builder;
        }
    }
}

namespace SkiaSharp.Views.Maui.Controls.Hosting
{
    public static class AppHostBuilderExtensions
    {
        /// <summary>SkiaSharp's MAUI handlers. MauiLite draws SkiaSharp views itself, so there is nothing to register.</summary>
        public static MauiAppBuilder UseSkiaSharp(this MauiAppBuilder builder) => builder;
    }
}
