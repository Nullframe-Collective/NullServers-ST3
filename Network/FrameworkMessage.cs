using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NullServers.Additional;

namespace NullServers.Network
{
    internal sealed record FrameworkAsset(
        string Id,
        string Hash,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Name = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Folder = null)
    {
        internal bool Valid =>
            Id is { Length: > 2 and <= 128 } &&
            Id.Contains(':') &&
            Id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or ':' or '-' or '_' or '.') &&
            Hash is { Length: 64 } &&
            Hash.All(Uri.IsHexDigit) &&
            (Name == null || Name is { Length: > 0 and <= 128 } && !Name.Any(char.IsControl)) &&
            (Folder == null || Folder is { Length: > 0 and <= 128 } && Folder is not "." and not ".." &&
                !Folder.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)));

        internal string DisplayName => Name ?? Id;
    }

    internal sealed record FrameworkManifest(
        int Version,
        int WeaponsApi,
        int CustomizationApi,
        FrameworkAsset[] Weapons,
        FrameworkAsset[] Customization)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int NPCsApi { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FrameworkAsset[] NPCs { get; init; }
        internal FrameworkAsset[] NPCAssets => NPCs ?? Array.Empty<FrameworkAsset>();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int MapsApi { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FrameworkAsset[] Maps { get; init; }
        internal FrameworkAsset[] MapAssets => Maps ?? Array.Empty<FrameworkAsset>();

        internal bool Valid =>
            Version == 1 &&
            WeaponsApi is 0 or 1 &&
            CustomizationApi is 0 or 1 &&
            NPCsApi is 0 or 1 &&
            MapsApi is 0 or 1 &&
            Weapons != null &&
            Customization != null &&
            (WeaponsApi != 0 || Weapons.Length == 0) &&
            (CustomizationApi != 0 || Customization.Length == 0) &&
            (NPCsApi != 0 || NPCAssets.Length == 0) &&
            (MapsApi != 0 || MapAssets.Length == 0) &&
            Weapons.Length + Customization.Length + NPCAssets.Length + MapAssets.Length <= 256 &&
            ValidAssets(MapAssets) &&
            ValidAssets(NPCAssets) &&
            ValidAssets(Weapons) &&
            ValidAssets(Customization);

        private static bool ValidAssets(FrameworkAsset[] assets) =>
            assets.All(asset => asset?.Valid == true) &&
            assets.Select(asset => asset.Id).Distinct(StringComparer.Ordinal).Count() == assets.Length &&
            assets.Select(asset => asset.Id).SequenceEqual(assets.Select(asset => asset.Id).OrderBy(id => id, StringComparer.Ordinal));

        internal string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this with
        {
            Weapons = Weapons.Select(asset => new FrameworkAsset(asset.Id, asset.Hash)).ToArray(),
            Customization = Customization.Select(asset => new FrameworkAsset(asset.Id, asset.Hash)).ToArray(),
            NPCs = NPCs?.Select(asset => new FrameworkAsset(asset.Id, asset.Hash)).ToArray(),
            Maps = Maps?.Select(asset => new FrameworkAsset(asset.Id, asset.Hash)).ToArray()
        }))));

        internal AddonPack[] PackDifferences(FrameworkManifest local)
        {
            List<AddonPack> result = new();

            foreach (var group in new[]
            {
                (Weapons, local.Weapons, "CustomWeapons"),
                (Customization, local.Customization, "CustomCustomization"),
                (NPCAssets, local.NPCAssets, "CustomNPCs"),
                (MapAssets, local.MapAssets, "CustomMaps")
            })
            {
                foreach (FrameworkAsset asset in group.Item1)
                {
                    FrameworkAsset found = group.Item2.FirstOrDefault(value => value.Id == asset.Id);

                    if (found == null || found.Hash != asset.Hash)
                    {
                        Add(asset, found == null ? "Missing pack" : "Different bundle or JSON");
                    }
                }

                void Add(FrameworkAsset asset, string status)
                {
                    string path = "UserData/" + group.Item3 + "/" + (asset.Folder ?? "[" + asset.Id + "]");
                    string files = group.Item3 switch
                    {
                        "CustomNPCs" => " (npc.json / bundle)",
                        "CustomMaps" => " (map.json / bundle / waves.json / triggers.json)",
                        _ => "/bundle"
                    };
                    result.Add(new AddonPack(asset.Id, asset.DisplayName, path + files, status, asset.Hash, group.Item3));
                }
            }

            return result.ToArray();
        }

        internal FrameworkManifest For(bool weapons, bool customization, bool npcs = false, bool maps = false) => new(
            Version,
            weapons ? WeaponsApi : 0,
            customization ? CustomizationApi : 0,
            weapons ? Weapons : Array.Empty<FrameworkAsset>(),
            customization ? Customization : Array.Empty<FrameworkAsset>())
        {
            NPCsApi = npcs ? NPCsApi : 0,
            NPCs = npcs ? NPCs : null,
            MapsApi = maps ? MapsApi : 0,
            Maps = maps ? Maps : null
        };

        internal FrameworkManifest For(FrameworkManifest host) => For(host.WeaponsApi != 0, host.CustomizationApi != 0, host.NPCsApi != 0, host.MapsApi != 0);

        internal FrameworkManifest Select(FrameworkManifest host) => new(
            Version,
            host.WeaponsApi == 0 ? 0 : WeaponsApi,
            host.CustomizationApi == 0 ? 0 : CustomizationApi,
            host.Weapons.Select(required => Weapons.FirstOrDefault(asset => asset.Id == required.Id)).Where(asset => asset != null).ToArray(),
            host.Customization.Select(required => Customization.FirstOrDefault(asset => asset.Id == required.Id)).Where(asset => asset != null).ToArray())
        {
            NPCsApi = host.NPCsApi == 0 ? 0 : NPCsApi,
            NPCs = host.NPCsApi == 0 ? null : host.NPCAssets.Select(required =>
                NPCAssets.FirstOrDefault(asset => asset.Id == required.Id)).Where(asset => asset != null).ToArray(),
            MapsApi = host.MapsApi == 0 ? 0 : MapsApi,
            Maps = host.MapsApi == 0 ? null : host.MapAssets.Select(required =>
                MapAssets.FirstOrDefault(asset => asset.Id == required.Id)).Where(asset => asset != null).ToArray()
        };

        internal string Difference(FrameworkManifest other)
        {
            if (other?.Valid != true)
            {
                return "Unsupported framework API or invalid content manifest.";
            }

            if (!Valid)
            {
                return "The local framework manifest is invalid.";
            }

            if (WeaponsApi != other.WeaponsApi || CustomizationApi != other.CustomizationApi || NPCsApi != other.NPCsApi || MapsApi != other.MapsApi)
            {
                return "The required framework APIs are unavailable.";
            }

            foreach (var group in new[] { (Weapons, other.Weapons), (Customization, other.Customization), (NPCAssets, other.NPCAssets), (MapAssets, other.MapAssets) })
            {
                foreach (FrameworkAsset asset in group.Item1)
                {
                    FrameworkAsset found = group.Item2.FirstOrDefault(value => value.Id == asset.Id);

                    if (found == null)
                    {
                        return "Missing content: " + asset.Id;
                    }

                    if (found.Hash != asset.Hash)
                    {
                        return "Different content: " + asset.Id;
                    }
                }

                FrameworkAsset extra = group.Item2.FirstOrDefault(asset => !group.Item1.Any(value => value.Id == asset.Id));

                if (extra != null)
                {
                    return "Unexpected content: " + extra.Id;
                }
            }

            return null;
        }
    }

    internal enum FrameworkStage
    {
        Offer,
        Request,
        Accepted,
        Rejected
    }

    internal sealed record FrameworkMessage(
        int Version,
        string Room,
        int RoomObject,
        string Revision,
        FrameworkStage Stage,
        FrameworkManifest Manifest = null,
        string Error = null)
    {
        internal const ushort BroadcastId = 0x4E4A;
        internal const int MaxBytes = 524288;
        public int InteractionApi { get; init; }

        internal static bool TryParse(string json, out FrameworkMessage message)
        {
            message = JsonPacket.Read<FrameworkMessage>(json, MaxBytes);

            if (message == null ||
                message.Version != 1 ||
                message.InteractionApi is not (0 or 2 or 3) ||
                message.Room is not { Length: > 0 and <= 64 } ||
                message.Room.Any(char.IsControl) ||
                message.RoomObject < 0 ||
                !Guid.TryParseExact(message.Revision, "N", out _) ||
                !Enum.IsDefined(typeof(FrameworkStage), message.Stage) ||
                message.Error?.Length > 512)
            {
                message = null;
                return false;
            }

            bool valid = message.Stage switch
            {
                FrameworkStage.Offer or FrameworkStage.Request =>
                    message.Manifest?.Valid == true &&
                    message.Error == null,
                FrameworkStage.Accepted => message.Manifest == null && message.Error == null,
                FrameworkStage.Rejected => message.Manifest == null && !string.IsNullOrWhiteSpace(message.Error),
                _ => false
            };

            if (!valid)
            {
                message = null;
            }

            return valid;
        }
    }
}
