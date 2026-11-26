using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

// MAUI's platform features that have nothing behind them on the Switch 2 (design section 7.7): no system share sheet,
// no browser, no vibration in the app, and the app behaves offline until HTTPS arrives. Each answers the way MAUI does
// when a feature is not available.

namespace Microsoft.Maui
{
    public enum ScrollBarVisibility { Default, Always, Never }

    /// <summary>MAUI's software-keyboard helpers. The Switch's keyboard comes in phase 2; until then none is showing.</summary>
    public static class SoftInputExtensions
    {
        public static bool IsSoftInputShowing(this InputView targetView) => false;

        public static Task<bool> HideSoftInputAsync(this InputView targetView, CancellationToken token) => Task.FromResult(true);

        public static Task<bool> ShowSoftInputAsync(this InputView targetView, CancellationToken token) => Task.FromResult(false);
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public class FeatureNotSupportedException : NotSupportedException
    {
        public FeatureNotSupportedException()
        {
        }

        public FeatureNotSupportedException(string? message) : base(message)
        {
        }

        public FeatureNotSupportedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    public interface ILauncher
    {
        Task<bool> CanOpenAsync(Uri uri);
        Task<bool> OpenAsync(Uri uri);
        Task<bool> TryOpenAsync(Uri uri);
    }

    /// <summary>MAUI's launcher. Nothing can be opened on the Switch 2, so every call answers false.</summary>
    public static class Launcher
    {
        public static ILauncher Default { get; } = new NoLauncher();

        public static Task<bool> CanOpenAsync(string uri) => Task.FromResult(false);
        public static Task<bool> CanOpenAsync(Uri uri) => Default.CanOpenAsync(uri);
        public static Task<bool> OpenAsync(string uri) => Task.FromResult(false);
        public static Task<bool> OpenAsync(Uri uri) => Default.OpenAsync(uri);
        public static Task<bool> TryOpenAsync(string uri) => Task.FromResult(false);
        public static Task<bool> TryOpenAsync(Uri uri) => Default.TryOpenAsync(uri);

        sealed class NoLauncher : ILauncher
        {
            public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(false);
            public Task<bool> OpenAsync(Uri uri) => Task.FromResult(false);
            public Task<bool> TryOpenAsync(Uri uri) => Task.FromResult(false);
        }
    }
}

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
    public interface IClipboard
    {
        bool HasText { get; }
        Task SetTextAsync(string? text);
        Task<string?> GetTextAsync();
    }

    /// <summary>MAUI's clipboard, kept in memory for the app's own copy and paste.</summary>
    public static class Clipboard
    {
        public static IClipboard Default { get; } = new MemoryClipboard();

        public static bool HasText => Default.HasText;
        public static Task SetTextAsync(string? text) => Default.SetTextAsync(text);
        public static Task<string?> GetTextAsync() => Default.GetTextAsync();

        sealed class MemoryClipboard : IClipboard
        {
            string? _text;

            public bool HasText => !string.IsNullOrEmpty(_text);

            public Task SetTextAsync(string? text)
            {
                _text = text;
                return Task.CompletedTask;
            }

            public Task<string?> GetTextAsync() => Task.FromResult(_text);
        }
    }

    public class ShareFile : FileBase
    {
        public ShareFile(string fullPath) : base(fullPath)
        {
        }

        public ShareFile(string fullPath, string? contentType) : base(fullPath) => ContentType = contentType;

        public ShareFile(FileBase file) : base(file?.FullPath ?? throw new ArgumentNullException(nameof(file))) => ContentType = file.ContentType;
    }

    public class ShareRequestBase
    {
        public string? Title { get; set; }
        public Rect PresentationSourceBounds { get; set; }
    }

    public class ShareTextRequest : ShareRequestBase
    {
        public ShareTextRequest()
        {
        }

        public ShareTextRequest(string text) => Text = text;

        public ShareTextRequest(string text, string title) : this(text) => Title = title;

        public string? Subject { get; set; }
        public string? Text { get; set; }
        public string? Uri { get; set; }
    }

    public class ShareFileRequest : ShareRequestBase
    {
        public ShareFileRequest()
        {
        }

        public ShareFileRequest(string title, ShareFile file)
        {
            Title = title;
            File = file;
        }

        public ShareFileRequest(string title, FileBase file) : this(title, new ShareFile(file))
        {
        }

        public ShareFileRequest(ShareFile file) => File = file;

        public ShareFileRequest(FileBase file) => File = new ShareFile(file);

        public ShareFile? File { get; set; }
    }

    public class ShareMultipleFilesRequest : ShareRequestBase
    {
        public ShareMultipleFilesRequest()
        {
        }

        public ShareMultipleFilesRequest(IEnumerable<ShareFile> files) => Files = [.. files];

        public ShareMultipleFilesRequest(IEnumerable<FileBase> files) : this(files.Select(file => new ShareFile(file)))
        {
        }

        public ShareMultipleFilesRequest(string title, IEnumerable<ShareFile> files) : this(files) => Title = title;

        public ShareMultipleFilesRequest(string title, IEnumerable<FileBase> files) : this(files) => Title = title;

        public List<ShareFile> Files { get; set; } = [];
    }

    public interface IShare
    {
        Task RequestAsync(ShareTextRequest request);
        Task RequestAsync(ShareFileRequest request);
        Task RequestAsync(ShareMultipleFilesRequest request);
    }

    /// <summary>MAUI's share sheet. The Switch 2 has none, so a share request does nothing.</summary>
    public static class Share
    {
        public static IShare Default { get; } = new NoShare();

        public static Task RequestAsync(string text) => Default.RequestAsync(new ShareTextRequest(text));
        public static Task RequestAsync(string text, string title) => Default.RequestAsync(new ShareTextRequest(text, title));
        public static Task RequestAsync(ShareTextRequest request) => Default.RequestAsync(request);
        public static Task RequestAsync(ShareFileRequest request) => Default.RequestAsync(request);
        public static Task RequestAsync(ShareMultipleFilesRequest request) => Default.RequestAsync(request);

        sealed class NoShare : IShare
        {
            public Task RequestAsync(ShareTextRequest request) => Task.CompletedTask;
            public Task RequestAsync(ShareFileRequest request) => Task.CompletedTask;
            public Task RequestAsync(ShareMultipleFilesRequest request) => Task.CompletedTask;
        }
    }
}

namespace Microsoft.Maui.Networking
{
    public enum NetworkAccess { Unknown, None, Local, ConstrainedInternet, Internet }

    public enum ConnectionProfile { Unknown, Bluetooth, Cellular, Ethernet, WiFi }

    public interface IConnectivity
    {
        NetworkAccess NetworkAccess { get; }
        IEnumerable<ConnectionProfile> ConnectionProfiles { get; }
    }

    /// <summary>MAUI's connectivity. The port behaves offline until HTTPS support arrives (design 7.7), so there is no network.</summary>
    public static class Connectivity
    {
        public static IConnectivity Current { get; } = new Offline();

        public static NetworkAccess NetworkAccess => Current.NetworkAccess;
        public static IEnumerable<ConnectionProfile> ConnectionProfiles => Current.ConnectionProfiles;

        sealed class Offline : IConnectivity
        {
            public NetworkAccess NetworkAccess => NetworkAccess.None;
            public IEnumerable<ConnectionProfile> ConnectionProfiles => [];
        }
    }
}

namespace Microsoft.Maui.Devices
{
    public enum HapticFeedbackType { Click, LongPress }

    public interface IHapticFeedback
    {
        bool IsSupported { get; }
        void Perform(HapticFeedbackType type);
    }

    /// <summary>MAUI's haptic feedback. The app does not vibrate the Joy-Con, so this does nothing.</summary>
    public static class HapticFeedback
    {
        public static IHapticFeedback Default { get; } = new NoHaptics();

        public static void Perform(HapticFeedbackType type = HapticFeedbackType.Click) => Default.Perform(type);

        sealed class NoHaptics : IHapticFeedback
        {
            public bool IsSupported => false;
            public void Perform(HapticFeedbackType type) { }
        }
    }
}
