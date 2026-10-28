using System.Text;
using System.Text.Json;

namespace NullServers.Additional
{
    internal sealed record AddonRequirement(string Id, string Version)
    {
        internal bool Valid =>
            Dependencies.Official.Contains(Id) &&
            Version is { Length: > 0 and <= 32 };

        internal string Name => Id;
    }

    internal sealed record AddonPack(string Id, string Name, string Path, string Status, string Hash = null, string Folder = null);

    internal enum AddonNoticeKind
    {
        Missing,
        Disabled,
        Unavailable,
        Content,
        Room
    }

    internal sealed record AddonNotice(
        AddonRequirement[] Required,
        AddonRequirement[] Missing,
        string Error = null,
        AddonNoticeKind Kind = AddonNoticeKind.Missing,
        AddonPack[] Packs = null)
    {
        internal string Title => Kind switch
        {
            AddonNoticeKind.Disabled => "ADDONS DISABLED",
            AddonNoticeKind.Unavailable => "ADDON INITIALIZATION ERROR",
            AddonNoticeKind.Room => "ROOM REQUIREMENTS CHANGED",
            _ => "REQUIRED ADDONS"
        };

        internal bool Download => Missing.Length > 0 || Kind == AddonNoticeKind.Content && Packs is { Length: > 0 };
    }

    internal static class Dependencies
    {
        internal const string Weapons = "CustomWeaponsFramework";
        internal const string NPCs = "CustomNPCFramework";
        internal const string Maps = "CustomMapsFramework";
        internal const string Modes = "CustomGameModesFramework";
        internal const string Customization = "CustomPlayerCustomization";
        internal const string DownloadUrl = "https://discord.gg/CjWXxyS2U4";
        internal static readonly string[] Official = [Weapons, Customization, NPCs, Modes, Maps];
        internal static readonly AddonRequirement[] Available =
        [
            new(Weapons, "1.3.0"),
            new(Customization, "1.3.0"),
            new(NPCs, "1.1.0"),
            new(Modes, "1.1.4"),
            new(Maps, "1.1.1")
        ];

        internal static AddonRequirement[] Missing(IEnumerable<AddonRequirement> required, IEnumerable<AddonRequirement> installed)
        {
            return required.Where(addon => !installed.Any(local => local.Id == addon.Id)).ToArray();
        }

        internal static bool TryRead(string data, out AddonRequirement[] addons)
        {
            addons = null;

            if (data == null || Encoding.UTF8.GetByteCount(data) > 1024)
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(data);

                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("Addons", out var value) ||
                    value.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                List<AddonRequirement> result = new();

                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    AddonRequirement addon = new(property.Name, property.Value.GetString());

                    if (!addon.Valid || result.Count >= Official.Length || result.Any(existing => existing.Id == addon.Id))
                    {
                        return false;
                    }

                    result.Add(addon);
                }

                addons = result.OrderBy(addon => addon.Id, StringComparer.Ordinal).ToArray();

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
