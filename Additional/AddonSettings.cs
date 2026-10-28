using System.Text.Json;

namespace NullServers.Additional
{
    internal sealed class AddonSettings
    {
        private readonly string _path;
        private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

        internal string LoadError { get; private set; }

        internal int Revision { get; private set; }

        internal AddonSettings(string path)
        {
            _path = path;

            try
            {
                if (!File.Exists(path)) return;

                string[] disabled = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));

                if (disabled == null || disabled.Any(id => !Dependencies.Official.Contains(id)))
                {
                    throw new JsonException("Invalid official addon settings.");
                }

                _disabled.UnionWith(disabled);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                LoadError = "Cannot read addon settings: " + exception.Message;
            }
        }

        internal bool Enabled(string id) => Dependencies.Official.Contains(id) && !_disabled.Contains(id);

        internal AddonRequirement[] Select(IEnumerable<AddonRequirement> installed) =>
            installed.Where(addon => Enabled(addon.Id)).ToArray();

        internal bool SetEnabled(string id, bool enabled, out string error)
        {
            error = LoadError;

            if (error != null) return false;

            if (!Dependencies.Official.Contains(id))
            {
                error = "Only official addons can be managed here.";
                return false;
            }

            HashSet<string> disabled = new(_disabled, StringComparer.Ordinal);

            if (enabled)
            {
                disabled.Remove(id);
            }
            else
            {
                disabled.Add(id);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(disabled.OrderBy(value => value, StringComparer.Ordinal)));
                File.Move(_path + ".tmp", _path, overwrite: true);
                _disabled.Clear();
                _disabled.UnionWith(disabled);
                Revision++;

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = "Cannot save addon settings: " + exception.Message;
                return false;
            }
        }
    }
}
