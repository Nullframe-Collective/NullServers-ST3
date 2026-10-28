using NullServers.Additional;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NullServers.Content
{
    internal static class PackFiles
    {
        internal const long BundleLimit = 256L * 1024 * 1024;
        internal const long DefinitionLimit = 256 * 1024;
        internal const long ImageLimit = 4 * 1024 * 1024;
        internal const long DownloadLimit = 1024L * 1024 * 1024;

        private static readonly string[] Images = ["icon.png", "crosshair.png", "scope.png"];

        internal static PackKind? KindOf(string folder) => folder switch
        {
            "CustomWeapons" => PackKind.Weapon,
            "CustomCustomization" => PackKind.Cosmetic,
            "CustomNPCs" => PackKind.NPC,
            _ => null
        };

        internal static string Definition(PackKind kind) => kind switch
        {
            PackKind.Weapon => "weapon.json",
            PackKind.Cosmetic => "customization.json",
            _ => "npc.json"
        };

        internal static string[] Allowed(PackKind kind) => kind switch
        {
            PackKind.Weapon => ["bundle", "weapon.json", "icon.png", "crosshair.png", "scope.png"],
            PackKind.Cosmetic => ["bundle", "customization.json"],
            _ => ["bundle", "npc.json", "icon.png"]
        };

        internal static bool KnownName(string name) =>
            name is "bundle" or "weapon.json" or "customization.json" or "npc.json" || Images.Contains(name);

        internal static long Limit(string name) =>
            name == "bundle" ? BundleLimit : name.EndsWith(".json", StringComparison.Ordinal) ? DefinitionLimit : ImageLimit;

        internal static string Sha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using SHA256 algorithm = SHA256.Create();
            return Convert.ToHexString(algorithm.ComputeHash(stream));
        }

        internal static string Check(PackKind kind, string directory, string expectedHash)
        {
            string[] allowed = Allowed(kind);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                string name = Path.GetFileName(path);
                if (!allowed.Contains(name) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    return "Unexpected file in the pack: " + name;
                }

                if (new FileInfo(path).Length > Limit(name))
                {
                    return name + " is larger than allowed.";
                }
            }

            string definition = Path.Combine(directory, Definition(kind));
            if (!File.Exists(definition))
            {
                return "The pack has no " + Definition(kind) + ".";
            }

            string json = File.ReadAllText(definition);
            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Definition(kind) + " is not a JSON object.";
                }
            }
            catch (JsonException)
            {
                return Definition(kind) + " is not valid JSON.";
            }

            string bundle = Path.Combine(directory, "bundle");
            bool hasBundle = File.Exists(bundle);

            if (!ContentHashMatches(kind, json, hasBundle ? bundle : null, expectedHash, out bool usesBundle))
            {
                return "The files do not match the pack the host advertised.";
            }

            if (!usesBundle && hasBundle)
            {
                return "The pack contains a bundle it does not use.";
            }

            if (usesBundle)
            {
                BundleReport report = BundleInspector.Inspect(File.ReadAllBytes(bundle), kind);
                if (!report.Passed)
                {
                    return report.Problems[0];
                }
            }

            foreach (string image in Images.Select(name => Path.Combine(directory, name)).Where(File.Exists))
            {
                if (!ImageDimensions.Allowed(File.ReadAllBytes(image)))
                {
                    return Path.GetFileName(image) + " is not a PNG/JPEG up to 2048 × 2048.";
                }
            }

            return null;
        }

        private static bool ContentHashMatches(PackKind kind, string json, string bundle, string expected, out bool usesBundle)
        {
            usesBundle = bundle != null;
            if (kind == PackKind.NPC)
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(Encoding.UTF8.GetBytes(json));

                if (Convert.ToHexString(hash.GetCurrentHash()) == expected)
                {
                    usesBundle = false;
                    return true;
                }

                if (bundle == null) return false;

                hash.AppendData(File.ReadAllBytes(bundle));
                return Convert.ToHexString(hash.GetHashAndReset()) == expected;
            }

            if (bundle == null) return false;

            string bundleHash = Sha256(bundle);
            string content = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bundleHash + "\n" + json.Replace("\r\n", "\n"))));
            return content == expected;
        }
    }
}
