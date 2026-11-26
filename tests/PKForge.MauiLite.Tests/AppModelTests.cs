using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using PKForge.MauiLite;

namespace PKForge.MauiLite.Tests;

public sealed class AppModelTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("mauilite-").FullName;

    public void Dispose()
    {
        Application.Current = null;
        Directory.Delete(_folder, recursive: true);
    }

    LitePaths Paths => new(Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), Path.Combine(_folder, "data", "preferences.json"));

    static void RunUntil(UiLoop loop, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            loop.RunOnce(clock.Elapsed);
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void Await_ResumesOnUiThread()
    {
        var loop = new UiLoop();
        int? resumedOn = null;
        loop.Post(async () =>
        {
            await Task.Delay(1);
            resumedOn = Environment.CurrentManagedThreadId;
        });

        RunUntil(loop, () => resumedOn is not null);

        Assert.Equal(Environment.CurrentManagedThreadId, resumedOn);
    }

    [Fact]
    public void Post_Throwing_IsReportedAndLoopContinues()
    {
        var loop = new UiLoop();
        var reported = 0;
        var ran = false;
        void Record(Exception error, string where) => reported++;
        UiErrors.Reported += Record;
        try
        {
            loop.Post(() => throw new InvalidOperationException("handler bug"));
            loop.Post(() => ran = true);
            loop.RunOnce(TimeSpan.Zero);
        }
        finally
        {
            UiErrors.Reported -= Record;
        }

        Assert.Equal(1, reported);
        Assert.True(ran);
    }

    [Fact]
    public void DispatcherTimer_TicksByLoopTime()
    {
        var loop = new UiLoop();
        var ticks = 0;
        var timer = loop.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) => ticks++;
        timer.Start();

        loop.RunOnce(TimeSpan.Zero);
        loop.RunOnce(TimeSpan.FromMilliseconds(99));
        Assert.Equal(0, ticks);
        loop.RunOnce(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, ticks);
        loop.RunOnce(TimeSpan.FromMilliseconds(350));
        Assert.Equal(2, ticks);
    }

    sealed class NamedPage(string name, List<string> events) : ContentPage
    {
        public bool HandlesBack { get; set; }

        protected override void OnAppearing() => events.Add($"{name}.Appearing");

        protected override void OnDisappearing() => events.Add($"{name}.Disappearing");

        protected override bool OnBackButtonPressed() => HandlesBack;
    }

    sealed class TestApp(Page root) : Application
    {
        protected override Window CreateWindow(IActivationState? activationState) => new(root);
    }

    [Fact]
    public async Task Navigation_PushPop_Lifecycle()
    {
        var events = new List<string>();
        var home = new NamedPage("Home", events);
        new TestApp(new NavigationPage(home)).OpenWindow();

        await home.Navigation.PushAsync(new NamedPage("Next", events));
        await home.Navigation.PopAsync();

        Assert.Equal(["Home.Appearing", "Home.Disappearing", "Next.Appearing", "Next.Disappearing", "Home.Appearing"], events);
    }

    [Fact]
    public async Task Navigation_PopOnRoot_DoesNothing()
    {
        var events = new List<string>();
        var home = new NamedPage("Home", events);
        new TestApp(new NavigationPage(home)).OpenWindow();

        var popped = await home.Navigation.PopAsync();

        Assert.Null(popped);
        Assert.Equal(["Home.Appearing"], events);
    }

    [Fact]
    public async Task BackButton_PopsUnlessPageHandles()
    {
        var events = new List<string>();
        var home = new NamedPage("Home", events);
        var next = new NamedPage("Next", events) { HandlesBack = true };
        var navigation = new NavigationPage(home);
        new TestApp(navigation).OpenWindow();
        await home.Navigation.PushAsync(next);

        Assert.True(navigation.SendBackButtonPressed());
        Assert.Same(next, navigation.CurrentPage);

        next.HandlesBack = false;
        Assert.True(navigation.SendBackButtonPressed());
        Assert.Same(home, navigation.CurrentPage);
        Assert.False(navigation.SendBackButtonPressed());
    }

    [Fact]
    public void Preferences_RoundTripThroughJsonFile()
    {
        var file = Path.Combine(_folder, "prefs.json");
        var stamp = new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc);
        var first = new JsonPreferences(file);
        first.Set("flag", true);
        first.Set("count", 42);
        first.Set("big", 1L << 40);
        first.Set("ratio", 0.25);
        first.Set("name", "Ash");
        first.Set("when", stamp);
        first.Set("theme", "dark", "shared");

        var second = new JsonPreferences(file);

        Assert.True(second.Get("flag", false));
        Assert.Equal(42, second.Get("count", 0));
        Assert.Equal(1L << 40, second.Get("big", 0L));
        Assert.Equal(0.25, second.Get("ratio", 0d));
        Assert.Equal("Ash", second.Get("name", ""));
        Assert.Equal(stamp, second.Get("when", DateTime.MinValue));
        Assert.Equal("dark", second.Get("theme", "", "shared"));
        Assert.False(second.ContainsKey("theme"));
        second.Remove("count");
        Assert.Equal(7, new JsonPreferences(file).Get("count", 7));
    }

    // As MAUI: a setter's value of another type is converted to the property's type (PKForge's visual states set
    // Border.Stroke, a Brush, to a Color; and numeric setters often give an int for a double).
    [Fact]
    public void SetValue_ConvertsToThePropertyType()
    {
        var border = new Border();
        border.SetValue(Border.StrokeProperty, Colors.Red);
        border.SetValue(Border.StrokeThicknessProperty, 2);

        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(border.Stroke).Color);
        Assert.Equal(2d, border.StrokeThickness);
        Assert.Throws<ArgumentException>(() => border.SetValue(Border.StrokeThicknessProperty, new object()));
    }

    [Fact]
    public void ImplicitStyle_SetsFontFamilyOfNewLabels()
    {
        var app = new Application();
        app.Resources.Add(new Style(typeof(Label)) { Setters = { new Setter { Property = Label.FontFamilyProperty, Value = "PixelUI" } } });

        Assert.Equal("PixelUI", new Label().FontFamily);
        Assert.Equal("Rounded", new Label { FontFamily = "Rounded" }.FontFamily);
    }

    interface IThing;

    sealed class AppThing : IThing;

    sealed class PlatformThing : IThing;

    [Fact]
    public void Build_PlatformServicesWinOverAppRegistrations()
    {
        LitePlatform.Configure(Paths, _ => Stream.Null, services => services.AddSingleton<IThing, PlatformThing>());
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton<IThing, AppThing>();

        var app = builder.Build();

        Assert.IsType<PlatformThing>(app.Services.GetRequiredService<IThing>());
        Assert.Same(app.Services, IPlatformApplication.Current?.Services);
    }

    [Fact]
    public async Task OpenAppPackageFile_UsesTheConfiguredOpener()
    {
        LitePlatform.Configure(Paths, name => new MemoryStream([(byte)name.Length]));

        await using var stream = await FileSystem.OpenAppPackageFileAsync("ui/logo.png");

        Assert.Equal("ui/logo.png".Length, stream.ReadByte());
        Assert.Equal(Paths.AppData, FileSystem.AppDataDirectory);
    }

    [Fact]
    public void DeviceDisplay_Is1920x1080AtTheRendererDensity()
    {
        _ = new Renderer(2.25f);

        var info = DeviceDisplay.MainDisplayInfo;

        Assert.Equal((1920d, 1080d, 2.25d), (info.Width, info.Height, info.Density));
    }

    sealed class Row : INotifyPropertyChanged
    {
        string _title = "";

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                Changed();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    sealed class Model : INotifyPropertyChanged
    {
        string _status = "";
        Row _row = new();

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                Changed();
            }
        }

        public Row Row
        {
            get => _row;
            set
            {
                _row = value;
                Changed();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    sealed class Shout : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => $"{value}!";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString()?.TrimEnd('!');
    }

    [Fact]
    public void Binding_OneWayFollowsPropertyChanged()
    {
        var model = new Model { Status = "one" };
        var label = new Label { BindingContext = model };
        label.SetBinding(Label.TextProperty, nameof(Model.Status));
        Assert.Equal("one", label.Text);

        model.Status = "two";

        Assert.Equal("two", label.Text);
    }

    [Fact]
    public void Binding_DottedPathFollowsEachLink()
    {
        var model = new Model();
        model.Row.Title = "first";
        var label = new Label { BindingContext = model };
        label.SetBinding(Label.TextProperty, "Row.Title");
        Assert.Equal("first", label.Text);

        model.Row.Title = "edited";
        Assert.Equal("edited", label.Text);

        model.Row = new Row { Title = "replaced" };
        Assert.Equal("replaced", label.Text);
    }

    [Fact]
    public void Binding_TwoWayWritesBackThroughConverter()
    {
        var model = new Model { Status = "ok" };
        var label = new Label { BindingContext = model };
        label.SetBinding(Label.TextProperty, new Binding(nameof(Model.Status), BindingMode.TwoWay, new Shout()));
        Assert.Equal("ok!", label.Text);

        label.Text = "changed!";

        Assert.Equal("changed", model.Status);
    }

    [Fact]
    public void Binding_ContextInheritedFromParent()
    {
        var outer = new Model { Status = "outer" };
        var inner = new Model { Status = "inner" };
        var inherits = new Label();
        var own = new Label { BindingContext = inner };
        inherits.SetBinding(Label.TextProperty, nameof(Model.Status));
        own.SetBinding(Label.TextProperty, nameof(Model.Status));
        var grid = new Grid { inherits, own };

        grid.BindingContext = outer;

        Assert.Equal("outer", inherits.Text);
        Assert.Equal("inner", own.Text);
    }
}
