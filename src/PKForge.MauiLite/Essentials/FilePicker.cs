namespace Microsoft.Maui.Devices
{
    public readonly struct DevicePlatform : IEquatable<DevicePlatform>
    {
        readonly string _name;

        DevicePlatform(string name) => _name = name;

        public static DevicePlatform Android { get; } = new(nameof(Android));
        public static DevicePlatform iOS { get; } = new(nameof(iOS));
        public static DevicePlatform MacCatalyst { get; } = new(nameof(MacCatalyst));
        public static DevicePlatform WinUI { get; } = new(nameof(WinUI));
        public static DevicePlatform Unknown { get; } = new(nameof(Unknown));

        public static DevicePlatform Create(string devicePlatform) => new(devicePlatform);

        public bool Equals(DevicePlatform other) => string.Equals(_name, other._name, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is DevicePlatform other && Equals(other);
        public override int GetHashCode() => _name?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => _name ?? "";
        public static bool operator ==(DevicePlatform left, DevicePlatform right) => left.Equals(right);
        public static bool operator !=(DevicePlatform left, DevicePlatform right) => !left.Equals(right);
    }
}

namespace Microsoft.Maui.Storage
{
    using Microsoft.Maui.Devices;

    public class FileBase
    {
        public FileBase(string fullPath)
        {
            FullPath = fullPath;
            FileName = Path.GetFileName(fullPath);
        }

        public string FullPath { get; }
        public string FileName { get; set; }
        public string? ContentType { get; set; }

        public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(File.OpenRead(FullPath));
    }

    public class FileResult(string fullPath) : FileBase(fullPath)
    {
    }

    public class FilePickerFileType(IDictionary<DevicePlatform, IEnumerable<string>> fileTypes)
    {
        public IEnumerable<string> Value => fileTypes.Values.SelectMany(types => types);

        public static FilePickerFileType Images { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>());
        public static FilePickerFileType Png { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>());
        public static FilePickerFileType Jpeg { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>());
    }

    public class PickOptions
    {
        public static PickOptions Default => new();
        public static PickOptions Images => new() { FileTypes = FilePickerFileType.Images };
        public string? PickerTitle { get; set; }
        public FilePickerFileType? FileTypes { get; set; }
    }

    public interface IFilePicker
    {
        Task<FileResult?> PickAsync(PickOptions? options = null);
        Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null);
    }

    /// <summary>MAUI's file picker. The Switch 2 has no system file picker, so a pick returns nothing (as if cancelled).</summary>
    public static class FilePicker
    {
        public static IFilePicker Default { get; } = new NoPicker();

        public static Task<FileResult?> PickAsync(PickOptions? options = null) => Default.PickAsync(options);

        public static Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) => Default.PickMultipleAsync(options);

        sealed class NoPicker : IFilePicker
        {
            public Task<FileResult?> PickAsync(PickOptions? options = null) => Task.FromResult<FileResult?>(null);

            public Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) => Task.FromResult<IEnumerable<FileResult?>>([]);
        }
    }
}
