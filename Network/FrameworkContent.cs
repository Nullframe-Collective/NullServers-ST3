using NullServers.Additional;
using System.Reflection;

namespace NullServers.Network
{
    internal static class FrameworkContent
    {
        internal static bool Ready(FrameworkManifest required) =>
            required != null &&
            (required.WeaponsApi == 0 || Frameworks.Weapons?.Ready == true) &&
            (required.CustomizationApi == 0 || Frameworks.Customization?.Ready == true) &&
            (required.NPCsApi == 0 || Frameworks.NPCs?.Ready == true) &&
            (required.MapsApi == 0 || Frameworks.Maps?.Ready == true);

        internal static string Waiting(FrameworkManifest required)
        {
            if (required == null) return null;

            foreach (var entry in new[]
            {
                (required.WeaponsApi, Dependencies.Weapons, Frameworks.Weapons),
                (required.CustomizationApi, Dependencies.Customization, Frameworks.Customization),
                (required.NPCsApi, Dependencies.NPCs, Frameworks.NPCs),
                (required.MapsApi, Dependencies.Maps, Frameworks.Maps)
            })
            {
                if (entry.Item1 == 0 || entry.Item3?.Ready == true) continue;

                return entry.Item2 + ": " + (Frameworks.Problem(entry.Item2) ?? "The addon assets did not finish loading.");
            }

            return null;
        }

        private static FrameworkAsset[] Assets(FrameworkAddon addon, bool customization = false, FrameworkAsset[] required = null)
        {
            if (addon == null)
            {
                return Array.Empty<FrameworkAsset>();
            }

            addon.ContentError = null;

            try
            {
                object[] items = (required != null ? addon.Installed : addon.Items).ToArray();

                if (required != null)
                {
                    items = items.Where(item => item != null && required.Any(asset => addon.Text(item, "Id") == asset.Id)).ToArray();
                }

                if (items.Any(item => item == null))
                {
                    throw new InvalidOperationException("The content registry contains an empty entry.");
                }

                FrameworkAsset[] assets = items
                    .Where(item => !customization || addon.Read<bool>(item.GetType(), "IsAvailable", item))
                    .Select(item => new FrameworkAsset(
                        addon.Text(item, "Id"),
                        addon.Text(item, "ContentHash"),
                        addon.Text(item, "Name"),
                        Path.GetFileName(addon.Text(item, "Path")?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))))
                    .ToArray();
                FrameworkManifest probe = new(1, 1, 0, assets, Array.Empty<FrameworkAsset>());

                if (!probe.Valid)
                {
                    throw new InvalidOperationException("Invalid content registry: check duplicate IDs, ID order, hashes and the 256 item limit.");
                }

                return assets;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or TargetInvocationException)
            {
                addon.ContentError = exception.InnerException?.Message ?? exception.Message;
                return Array.Empty<FrameworkAsset>();
            }
        }

        internal static FrameworkManifest Create(FrameworkManifest required = null)
        {
            FrameworkAsset[] weapons = Assets(Frameworks.Weapons, required: required?.Weapons);
            FrameworkAsset[] customization = Assets(Frameworks.Customization, true, required?.Customization);

            FrameworkAsset[] npcs = Assets(Frameworks.NPCs, true, required?.NPCAssets);
            FrameworkAsset[] maps = Assets(Frameworks.Maps, true, required?.MapAssets);

            return new FrameworkManifest(
                1,
                Frameworks.Weapons == null || Frameworks.Weapons.ContentError != null ? 0 : 1,
                Frameworks.Customization == null || Frameworks.Customization.ContentError != null ? 0 : 1,
                weapons,
                customization)
            {
                NPCsApi = Frameworks.NPCs == null || Frameworks.NPCs.ContentError != null ? 0 : 1,
                NPCs = Frameworks.NPCs == null ? null : npcs,
                MapsApi = Frameworks.Maps == null || Frameworks.Maps.ContentError != null ? 0 : 1,
                Maps = Frameworks.Maps == null ? null : maps
            };
        }
    }
}
