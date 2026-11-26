using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Storage
{
    public interface IPreferences
    {
        bool ContainsKey(string key, string? sharedName = null);
        void Remove(string key, string? sharedName = null);
        void Clear(string? sharedName = null);
        void Set<T>(string key, T value, string? sharedName = null);
        T Get<T>(string key, T defaultValue, string? sharedName = null);
    }

    /// <summary>MAUI's preferences. MauiLite keeps them in one JSON file (see <see cref="PKForge.MauiLite.LitePlatform"/>).</summary>
    public static class Preferences
    {
        static IPreferences? s_default;

        public static IPreferences Default => s_default ?? throw new InvalidOperationException("MauiLite's preferences are not configured: call LitePlatform.Configure first.");

        internal static void SetDefault(IPreferences preferences) => s_default = preferences;
    }
}

namespace PKForge.MauiLite
{
    /// <summary>
    /// Preferences in one JSON file, rewritten whole on each change. Values are stored as Android's SharedPreferences
    /// stores them: numbers, strings and booleans, with DateTime as its binary long.
    /// </summary>
    sealed class JsonPreferences : IPreferences
    {
        readonly string _file;
        readonly Dictionary<string, Dictionary<string, object>> _stores = new(StringComparer.Ordinal);
        readonly Lock _gate = new();

        public JsonPreferences(string file)
        {
            _file = file;
            Load();
        }

        Dictionary<string, object> Store(string? sharedName, bool create)
        {
            var name = sharedName ?? "";
            if (!_stores.TryGetValue(name, out var store) && create)
                _stores[name] = store = new(StringComparer.Ordinal);
            return store ?? [];
        }

        public bool ContainsKey(string key, string? sharedName = null)
        {
            lock (_gate)
                return Store(sharedName, create: false).ContainsKey(key);
        }

        public void Remove(string key, string? sharedName = null)
        {
            lock (_gate)
                if (Store(sharedName, create: false).Remove(key))
                    Save();
        }

        public void Clear(string? sharedName = null)
        {
            lock (_gate)
                if (_stores.Remove(sharedName ?? ""))
                    Save();
        }

        public void Set<T>(string key, T value, string? sharedName = null)
        {
            object? stored = value switch
            {
                null => null,
                bool b => b,
                int i => (long)i,
                long l => l,
                double d => d,
                float f => (double)f,
                string s => s,
                DateTime date => date.ToBinary(),
                DateTimeOffset offset => offset.ToString("o", CultureInfo.InvariantCulture),
                _ => throw new NotSupportedException($"Preferences cannot store a {typeof(T).Name}."),
            };
            lock (_gate)
            {
                if (stored is null)
                    Store(sharedName, create: false).Remove(key);
                else
                    Store(sharedName, create: true)[key] = stored;
                Save();
            }
        }

        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        {
            object? raw;
            lock (_gate)
                if (!Store(sharedName, create: false).TryGetValue(key, out raw))
                    return defaultValue;
            object? value = typeof(T) switch
            {
                var t when t == typeof(bool) => raw as bool?,
                var t when t == typeof(int) => raw switch { long l => (int)l, double d => (int)d, _ => null },
                var t when t == typeof(long) => raw switch { long l => l, double d => (long)d, _ => null },
                var t when t == typeof(double) => raw switch { long l => (double)l, double d => d, _ => null },
                var t when t == typeof(float) => raw switch { long l => (float)l, double d => (float)d, _ => null },
                var t when t == typeof(string) => raw as string,
                var t when t == typeof(DateTime) => raw is long binary ? DateTime.FromBinary(binary) : null,
                var t when t == typeof(DateTimeOffset) => raw is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null,
                _ => null,
            };
            return value is T typed ? typed : defaultValue;
        }

        void Load()
        {
            if (!File.Exists(_file))
                return;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(_file));
                foreach (var store in document.RootElement.EnumerateObject())
                {
                    var values = Store(store.Name, create: true);
                    foreach (var entry in store.Value.EnumerateObject())
                        if (Read(entry.Value) is { } value)
                            values[entry.Name] = value;
                }
            }
            catch (JsonException)
            {
                // A damaged file starts empty, as Android does with a corrupt preferences file.
            }
        }

        static object? Read(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            // Boxed apart: a ?: would widen the long to double and lose a DateTime's binary form.
            JsonValueKind.Number => element.TryGetInt64(out var whole) ? (object)whole : element.GetDouble(),
            _ => null,
        };

        void Save()
        {
            var folder = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            var temporary = _file + ".new";
            using (var stream = File.Create(temporary))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var (name, values) in _stores)
                {
                    writer.WriteStartObject(name);
                    foreach (var (key, value) in values)
                    {
                        switch (value)
                        {
                            case bool b: writer.WriteBoolean(key, b); break;
                            case long l: writer.WriteNumber(key, l); break;
                            case double d: writer.WriteNumber(key, d); break;
                            case string s: writer.WriteString(key, s); break;
                        }
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            Replace(temporary, _file);
        }

        // The console's SD card may refuse to rename over an existing file, so that case deletes first.
        static void Replace(string source, string target)
        {
            try
            {
                File.Move(source, target, overwrite: true);
            }
            catch (IOException)
            {
                File.Delete(target);
                File.Move(source, target);
            }
        }
    }
}
