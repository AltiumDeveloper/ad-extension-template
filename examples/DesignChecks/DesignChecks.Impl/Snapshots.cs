using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesignChecks.Impl
{
    /// <summary>JSON snapshots of the design and board models: test fixtures, written by the Snapshot commands.</summary>
    public static class Snapshots
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        /// <summary>Writes <paramref name="model"/> to <paramref name="path"/>, by default %TEMP%\DesignChecks\<paramref name="name"/>.</summary>
        public static string Save(object model, string path, string name)
        {
            path ??= Path.Combine(Path.GetTempPath(), "DesignChecks", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(model, model.GetType(), Json));
            return path;
        }

        public static T Load<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
    }
}
