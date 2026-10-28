using NullServers.Additional;
using System.Collections;
using System.Text.Json.Nodes;

namespace NullServers.Network
{
    internal static class MapRooms
    {
        internal static object Find(string scene) => Frameworks.Maps?.Installed.FirstOrDefault(item =>
            Frameworks.Maps.Read<bool>(item.GetType(), "IsAvailable", item) &&
            Frameworks.Maps.Read<IEnumerable>(item.GetType(), "Scenes", item)?.Cast<string>().Contains(scene, StringComparer.Ordinal) == true);

        internal static IEnumerable<string> Scenes => Frameworks.Maps?.Enabled == true ? Frameworks.Maps.Installed
            .Where(item => Frameworks.Maps.Read<bool>(item.GetType(), "IsAvailable", item))
            .SelectMany(item => Frameworks.Maps.Read<IEnumerable>(item.GetType(), "Scenes", item)?.Cast<string>() ?? Enumerable.Empty<string>())
            : Enumerable.Empty<string>();

        internal static bool Prepare(JsonObject properties, FrameworkManifest content, bool existing,
            out FrameworkManifest selected, out string error)
        {
            selected = content;
            error = null;
            object map = Find(Room.RoomData.Text(properties, "MapName"));
            string id = map == null ? null : Frameworks.Maps.Text(map, "Id");
            FrameworkAsset asset = content.MapAssets.FirstOrDefault(value => value.Id == id);

            if (map != null && asset == null)
            {
                asset = new FrameworkAsset(id, Frameworks.Maps.Text(map, "ContentHash"),
                    Frameworks.Maps.Text(map, "Name"), Path.GetFileName(Frameworks.Maps.Text(map, "Path")?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (!asset.Valid)
                {
                    error = "The selected custom map has an invalid content descriptor.";
                    return false;
                }
            }

            if (map != null)
            {
                if (asset == null || !Frameworks.Settings.Enabled(Dependencies.Maps) || Frameworks.Problem(Dependencies.Maps) != null)
                {
                    error = "The selected custom map is unavailable. Enable CustomMapsFramework in ADDONS and check its map files.";
                    return false;
                }

                IEnumerable<string> dependencies = Frameworks.Maps.Read<IEnumerable>(map.GetType(), "Dependencies", map)?.Cast<string>() ?? Enumerable.Empty<string>();
                foreach (string dependency in dependencies)
                {
                    object npc = Frameworks.NPCs?.Items.FirstOrDefault(item =>
                        (Frameworks.NPCs.Text(item, "Id") == dependency || Frameworks.NPCs.Text(item, "Name") == dependency) &&
                        Frameworks.NPCs.Read<bool>(item.GetType(), "IsAvailable", item));
                    if (npc == null || !Frameworks.Settings.Enabled(Dependencies.NPCs) || content.NPCsApi == 0)
                    {
                        error = "This map requires an enabled CustomNPCFramework pack: " + dependency;
                        return false;
                    }
                }
            }

            selected = content with { MapsApi = map == null ? 0 : 1, Maps = map == null ? null : new[] { asset } };
            properties.Remove("CustomMap");

            if (map != null)
            {
                properties["CustomMap"] = new JsonObject
                {
                    ["Protocol"] = 1,
                    ["Id"] = id,
                    ["Name"] = Frameworks.Maps.Text(map, "Name")
                };
            }

            return true;
        }
    }
}
