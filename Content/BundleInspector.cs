namespace NullServers.Content
{
    internal enum PackKind
    {
        Weapon,
        Cosmetic,
        NPC
    }

    internal sealed class BundleReport
    {
        internal List<string> Problems { get; } = new();
        internal SortedDictionary<string, int> Classes { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> Scripts { get; } = new(StringComparer.Ordinal);
        internal List<string> Externals { get; } = new();
        internal long UnpackedSize { get; set; }
        internal bool Passed => Problems.Count == 0;

        internal void Reject(string problem)
        {
            if (Problems.Count < 32 && !Problems.Contains(problem))
            {
                Problems.Add(problem);
            }
        }
    }

    internal static class BundleInspector
    {
        private static readonly Dictionary<int, string> ClassNames = new()
        {
            [1] = "GameObject", [4] = "Transform", [20] = "Camera", [21] = "Material", [23] = "MeshRenderer",
            [28] = "Texture2D", [33] = "MeshFilter", [43] = "Mesh", [48] = "Shader", [49] = "TextAsset",
            [54] = "Rigidbody", [64] = "MeshCollider", [65] = "BoxCollider", [74] = "AnimationClip",
            [82] = "AudioSource", [83] = "AudioClip", [89] = "Cubemap", [90] = "Avatar", [91] = "AnimatorController",
            [95] = "Animator", [96] = "TrailRenderer", [108] = "Light", [111] = "Animation", [114] = "MonoBehaviour",
            [115] = "MonoScript", [117] = "Texture3D", [120] = "LineRenderer", [135] = "SphereCollider",
            [136] = "CapsuleCollider", [137] = "SkinnedMeshRenderer", [142] = "AssetBundle", [144] = "CharacterJoint",
            [153] = "ConfigurableJoint", [198] = "ParticleSystem", [199] = "ParticleSystemRenderer",
            [212] = "SpriteRenderer", [213] = "Sprite", [221] = "AnimatorOverrideController", [328] = "VideoPlayer",
            [329] = "VideoClip", [1101] = "AnimatorStateTransition", [1102] = "AnimatorState",
            [1107] = "AnimatorStateMachine", [1120] = "LightProbes"
        };

        private static readonly HashSet<int> Shared =
        [
            1, 4, 21, 23, 28, 33, 43, 48, 54, 64, 65, 74, 82, 83, 96, 108, 111, 120, 135, 136, 137, 142,
            144, 153, 198, 199, 212, 213
        ];

        private static readonly HashSet<int> AnimatorTypes = [90, 91, 95, 221, 1101, 1102, 1107];

        private static readonly HashSet<string> BuiltinExternals = new(StringComparer.OrdinalIgnoreCase)
        {
            "library/unity default resources",
            "resources/unity_builtin_extra"
        };

        internal static string ClassName(int id) => ClassNames.GetValueOrDefault(id) ?? "class " + id;

        private static bool Allowed(int classId, PackKind kind) =>
            Shared.Contains(classId) || kind == PackKind.NPC && AnimatorTypes.Contains(classId);

        internal static BundleReport Inspect(byte[] file, PackKind kind, BundleLimits limits = null)
        {
            limits ??= new BundleLimits();
            BundleReport report = new();

            try
            {
                UnityBundle bundle = UnityBundle.Read(file, limits);
                report.UnpackedSize = bundle.Data.LongLength;

                foreach (BundleNode node in bundle.Nodes)
                {
                    if (!node.Path.StartsWith("CAB-", StringComparison.Ordinal) || node.Path.Contains('/') || node.Path.Contains('\\'))
                    {
                        report.Reject("Unexpected file inside the bundle: " + node.Path);
                        continue;
                    }

                    if (node.Path.EndsWith(".resS", StringComparison.Ordinal) || node.Path.EndsWith(".resource", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Inspect(SerializedFile.Read(bundle.Data, checked((int)node.Offset), checked((int)node.Size), limits), kind, limits, report);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException or IndexOutOfRangeException)
            {
                report.Reject("Malformed bundle: " + exception.Message);
            }

            return report;
        }

        private static void Inspect(SerializedFile file, PackKind kind, BundleLimits limits, BundleReport report)
        {
            if (!file.HasTypeTree)
            {
                report.Reject("The bundle was built without type information and cannot be checked.");
                return;
            }

            foreach (SerializedExternal external in file.Externals)
            {
                report.Externals.Add(external.Path);
                if (!BuiltinExternals.Contains(external.Path))
                {
                    report.Reject("The bundle depends on another file: " + external.Path);
                }
            }

            foreach (SerializedObject item in file.Objects)
            {
                int classId = item.Type.ClassId;
                string name = ClassName(classId);
                report.Classes[name] = report.Classes.GetValueOrDefault(name) + 1;

                if (!Allowed(classId, kind) && classId != 114 && classId != 115)
                {
                    report.Reject("Not allowed in a " + kind + " pack: " + name);
                    continue;
                }

                if (classId is 74 or 115 or 114 or 28 or 43)
                {
                    InspectObject(file, item, limits, report);
                }
            }
        }

        private static void InspectObject(SerializedFile file, SerializedObject item, BundleLimits limits, BundleReport report)
        {
            TypeTreeValue value = file.ReadObject(item, limits);

            switch (item.Type.ClassId)
            {
                case 115:
                    string script = string.Join(".", new[] { value.Field("m_AssemblyName")?.Text, value.Field("m_Namespace")?.Text, value.Field("m_ClassName")?.Text }
                        .Where(part => !string.IsNullOrEmpty(part)));
                    report.Scripts.Add(script);
                    report.Reject("Scripts are not allowed in downloadable packs: " + script);
                    break;
                case 114:
                    report.Reject("Script components are not allowed in downloadable packs.");
                    if (value.Descendants().Any(node => node.Name == "m_PersistentCalls"))
                    {
                        report.Reject("UnityEvent callbacks are not allowed.");
                    }

                    break;
                case 74:
                    if (value.Field("m_Events")?.Count > 0)
                    {
                        report.Reject("Animation events are not allowed: " + value.Field("m_Name")?.Text);
                    }

                    break;
                case 28:
                    long width = value.Field("m_Width")?.Number ?? 0;
                    long height = value.Field("m_Height")?.Number ?? 0;

                    if (width > 4096 || height > 4096)
                    {
                        report.Reject($"Texture {value.Field("m_Name")?.Text} is larger than 4096 × 4096.");
                    }

                    break;
                case 43:
                    long vertices = value.Field("m_VertexData")?.Field("m_VertexCount")?.Number ?? 0;
                    if (vertices > 500_000)
                    {
                        report.Reject($"Mesh {value.Field("m_Name")?.Text} has more than 500 000 vertices.");
                    }

                    break;
            }
        }
    }
}
