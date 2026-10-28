using NullServers.Additional;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NullServers.Network
{
    internal sealed record RoomRequirements(AddonRequirement[] Addons, string Fingerprint, FrameworkManifest Packs = null, FrameworkAsset Map = null)
    {
        internal bool UsesWeapons => Addons.Any(addon => addon.Id == Dependencies.Weapons);

        internal bool UsesMaps => Addons.Any(addon => addon.Id == Dependencies.Maps);

        internal bool UsesNPCs => Addons.Any(addon => addon.Id == Dependencies.NPCs);

        internal bool UsesCustomization => Addons.Any(addon => addon.Id == Dependencies.Customization);

        internal bool TryRoundContent(FrameworkManifest current, out FrameworkManifest required)
        {
            required = Packs ?? (current == null ? null : current with
            {
                MapsApi = UsesMaps ? 1 : 0,
                Maps = UsesMaps && Map != null ? new[] { Map } : null
            });
            if (current?.Valid != true || required?.Valid != true || required.Fingerprint != Fingerprint ||
                (current with { MapsApi = 0, Maps = null }).Difference(required with { MapsApi = 0, Maps = null }) != null)
            {
                required = null;
                return false;
            }

            return true;
        }

        internal AddonNotice Check(
            AddonRequirement[] installed,
            FrameworkManifest content,
            AddonSettings settings = null,
            Func<string, string> problem = null,
            bool deferContent = false)
        {
            FrameworkManifest local = Packs == null ? content.For(UsesWeapons, UsesCustomization, UsesNPCs, UsesMaps) : content.Select(Packs);
            AddonPack[] packs = Packs?.PackDifferences(local);

            if (Packs == null && Map != null)
            {
                FrameworkManifest map = new(1, 0, 0, Array.Empty<FrameworkAsset>(), Array.Empty<FrameworkAsset>())
                {
                    MapsApi = 1,
                    Maps = new[] { Map }
                };
                packs = map.PackDifferences(content);
            }

            AddonRequirement[] missing = Dependencies.Missing(Addons, installed);

            if (missing.Length > 0)
            {
                return new AddonNotice(Addons, missing, Packs: packs);
            }

            AddonRequirement[] disabled = Addons.Where(addon => settings?.Enabled(addon.Id) == false).ToArray();

            if (disabled.Length > 0)
            {
                return new AddonNotice(disabled, Array.Empty<AddonRequirement>(),
                    "A required addon is disabled. Enable it in ADDONS before joining this server.", AddonNoticeKind.Disabled);
            }

            string error = Addons.Select(addon => (addon, error: problem?.Invoke(addon.Id)))
                .Where(entry => entry.error != null)
                .Select(entry => entry.addon.Name + ": " + entry.error)
                .FirstOrDefault();

            if (packs is { Length: > 0 })
            {
                return new AddonNotice(Addons, Array.Empty<AddonRequirement>(),
                    "The following content packs do not match this server." + (error == null ? "" : "\n" + error),
                    AddonNoticeKind.Content, packs);
            }

            if (error != null)
            {
                return new AddonNotice(Addons, Array.Empty<AddonRequirement>(),
                    "The addon DLL is installed, but its content could not initialize.\n" + error, AddonNoticeKind.Unavailable);
            }

            if (!(deferContent && Packs == null) && (!local.Valid || local.Fingerprint != Fingerprint))
            {
                return new AddonNotice(Addons, Array.Empty<AddonRequirement>(),
                    "The addon packs differ from the host's packs. The host must update NullServers to publish pack details.",
                    AddonNoticeKind.Content);
            }

            return null;
        }

        internal static void Publish(JsonObject properties, AddonRequirement[] addons, FrameworkManifest content)
        {
            addons = addons.Where(addon => (addon.Id != Dependencies.Modes || properties.ContainsKey("CustomMode")) &&
                (addon.Id != Dependencies.Maps || content.MapsApi != 0)).ToArray();
            if (content.MapAssets.Length == 1 && properties["CustomMap"] is JsonObject map)
            {
                map["Hash"] = content.MapAssets[0].Hash;
                map["Folder"] = content.MapAssets[0].Folder;
            }

            properties["Content"] = content.Fingerprint;
            properties["Addons"] = JsonSerializer.SerializeToNode(addons.ToDictionary(addon => addon.Id, addon => addon.Version));
            properties.Remove("Packs");

            using MemoryStream bytes = new();

            using (BrotliStream compressed = new(bytes, CompressionLevel.SmallestSize, true))
            {
                JsonSerializer.Serialize(compressed, content);
            }

            properties["Packs"] = Convert.ToBase64String(bytes.ToArray());

            if (Encoding.UTF8.GetByteCount(properties.ToJsonString()) > 1024)
            {
                properties.Remove("Packs");
            }
        }

        private static FrameworkManifest ReadPacks(string encoded)
        {
            if (encoded is not { Length: > 0 and <= 1024 }) return null;

            try
            {
                using MemoryStream bytes = new(Convert.FromBase64String(encoded));
                using BrotliStream compressed = new(bytes, CompressionMode.Decompress);
                using MemoryStream decoded = new();
                byte[] buffer = new byte[4096];
                int count;

                while ((count = compressed.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (decoded.Length + count > FrameworkMessage.MaxBytes) return null;

                    decoded.Write(buffer, 0, count);
                }

                FrameworkManifest manifest = JsonPacket.Read<FrameworkManifest>(Encoding.UTF8.GetString(decoded.ToArray()), FrameworkMessage.MaxBytes);

                return manifest?.Valid == true ? manifest : null;
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException or IOException or InvalidOperationException)
            {
                return null;
            }
        }

        internal static bool TryRead(string data, out RoomRequirements requirements)
        {
            requirements = null;

            if (!Dependencies.TryRead(data, out var addons))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(data);

            if (!document.RootElement.TryGetProperty("Content", out var content) ||
                content.ValueKind != JsonValueKind.String ||
                content.GetString() is not { Length: 64 } fingerprint ||
                !fingerprint.All(Uri.IsHexDigit))
            {
                return false;
            }

            FrameworkManifest packs = null;

            if (document.RootElement.TryGetProperty("Packs", out var value))
            {
                packs = value.ValueKind == JsonValueKind.String ? ReadPacks(value.GetString()) : null;

                if (packs == null || packs.Fingerprint != fingerprint ||
                    (packs.WeaponsApi != 0) != addons.Any(addon => addon.Id == Dependencies.Weapons) ||
                    (packs.CustomizationApi != 0) != addons.Any(addon => addon.Id == Dependencies.Customization) ||
                    (packs.NPCsApi != 0) != addons.Any(addon => addon.Id == Dependencies.NPCs) ||
                    (packs.MapsApi != 0) != addons.Any(addon => addon.Id == Dependencies.Maps))
                {
                    return false;
                }
            }

            FrameworkAsset map = null;
            if (addons.Any(addon => addon.Id == Dependencies.Maps))
            {
                if (!document.RootElement.TryGetProperty("CustomMap", out var custom) || custom.ValueKind != JsonValueKind.Object ||
                    !custom.TryGetProperty("Protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out int version) || version != 1 ||
                    !custom.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String ||
                    !custom.TryGetProperty("Hash", out var hash) || hash.ValueKind != JsonValueKind.String ||
                    !custom.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String ||
                    !document.RootElement.TryGetProperty("MapName", out var scene) || scene.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(scene.GetString()) || scene.GetString().Length > 128 || scene.GetString().Any(char.IsControl)) return false;

                string folder = null;
                if (custom.TryGetProperty("Folder", out var folderValue) && folderValue.ValueKind != JsonValueKind.Null)
                {
                    if (folderValue.ValueKind != JsonValueKind.String) return false;

                    folder = folderValue.GetString();
                }

                map = new FrameworkAsset(id.GetString(), hash.GetString(), name.GetString(), folder);
                if (!map.Valid || packs != null && (packs.MapAssets.Length != 1 || packs.MapAssets[0] != map)) return false;

                string sceneName = scene.GetString();
                if (sceneName != map.Name && sceneName != map.Name + " (Day)" && sceneName != map.Name + " (Dusk)" &&
                    sceneName != map.Name + " (Night)") return false;
            }
            else if (document.RootElement.TryGetProperty("CustomMap", out _)) return false;

            requirements = new RoomRequirements(addons, fingerprint, packs, map);

            return true;
        }
    }
}
