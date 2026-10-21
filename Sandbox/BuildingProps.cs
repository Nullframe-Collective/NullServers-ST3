using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace S3_NullServers.Sandbox
{
    internal sealed class BuildingProps
    {
        private readonly record struct Part(PrimitiveType Shape, Vector3 Position, Vector3 Size, Vector3 Angles = default);

        private readonly Dictionary<string, Part[]> _recipes = new();
        private readonly Dictionary<string, Mesh> _meshes = new();
        private Material _material;

        internal IEnumerable<string> Names => _recipes.Keys;

        internal BuildingProps()
        {
            Part Box(float x, float y, float z, float a, float b, float c) => new(
                PrimitiveType.Cube,
                new Vector3(x, y, z),
                new Vector3(a, b, c));

            Part Ball(float x, float y, float z, float size) => new(
                PrimitiveType.Sphere,
                new Vector3(x, y, z),
                Vector3.one * size);

            Part Cylinder(float x, float y, float z, float radius, float height) => new(
                PrimitiveType.Cylinder,
                new Vector3(x, y, z),
                new Vector3(radius, height / 2f, radius));

            _recipes["Crate"] = new[]
            {
                Box(0f, 0f, 0f, 1f, 1f, 1f),
                Box(0f, 0.46f, 0f, 1.04f, 0.08f, 1.04f),
                Box(0f, -0.46f, 0f, 1.04f, 0.08f, 1.04f)
            };
            _recipes["Barrel"] = new[]
            {
                Cylinder(0f, 0f, 0f, 0.8f, 1.2f),
                Cylinder(0f, 0.45f, 0f, 0.86f, 0.08f),
                Cylinder(0f, -0.45f, 0f, 0.86f, 0.08f)
            };
            _recipes["Plank"] = new[] { Box(0f, 0f, 0f, 0.4f, 0.12f, 3f) };
            _recipes["Platform"] = new[] { Box(0f, 0f, 0f, 4f, 0.25f, 4f) };
            _recipes["Wall"] = new[] { Box(0f, 0f, 0f, 4f, 3f, 0.25f) };
            _recipes["Window wall"] = new[]
            {
                Box(-1.5f, 0f, 0f, 1f, 3f, 0.25f),
                Box(1.5f, 0f, 0f, 1f, 3f, 0.25f),
                Box(0f, 1.1f, 0f, 2f, 0.8f, 0.25f),
                Box(0f, -1.1f, 0f, 2f, 0.8f, 0.25f)
            };
            _recipes["Door frame"] = new[]
            {
                Box(-1.1f, 0f, 0f, 0.3f, 3f, 0.35f),
                Box(1.1f, 0f, 0f, 0.3f, 3f, 0.35f),
                Box(0f, 1.35f, 0f, 2f, 0.3f, 0.35f)
            };
            _recipes["Stairs"] = Enumerable.Range(0, 6).Select(i => Box(
                    0f,
                    (i + 1) * 0.15f - 0.525f,
                    i * 0.4f - 1f,
                    2f,
                    (i + 1) * 0.3f,
                    0.4f)).ToArray();
            _recipes["Fence"] = new[]
            {
                Box(-1.8f, 0f, 0f, 0.16f, 2f, 0.16f),
                Box(1.8f, 0f, 0f, 0.16f, 2f, 0.16f),
                Box(0f, 0.6f, 0f, 3.6f, 0.16f, 0.12f),
                Box(0f, -0.3f, 0f, 3.6f, 0.16f, 0.12f)
            };
            _recipes["Table"] = new[]
            {
                Box(0f, 0.6f, 0f, 2f, 0.15f, 1.2f),
                Box(-0.8f, 0f, -0.4f, 0.15f, 1.2f, 0.15f),
                Box(0.8f, 0f, -0.4f, 0.15f, 1.2f, 0.15f),
                Box(-0.8f, 0f, 0.4f, 0.15f, 1.2f, 0.15f),
                Box(0.8f, 0f, 0.4f, 0.15f, 1.2f, 0.15f)
            };
            _recipes["Chair"] = new[]
            {
                Box(0f, 0f, 0f, 0.8f, 0.12f, 0.8f),
                Box(0f, 0.45f, 0.34f, 0.8f, 0.9f, 0.12f),
                Box(-0.3f, -0.35f, -0.3f, 0.1f, 0.6f, 0.1f),
                Box(0.3f, -0.35f, -0.3f, 0.1f, 0.6f, 0.1f),
                Box(-0.3f, -0.35f, 0.3f, 0.1f, 0.6f, 0.1f),
                Box(0.3f, -0.35f, 0.3f, 0.1f, 0.6f, 0.1f)
            };
            _recipes["Bench"] = new[]
            {
                Box(0f, 0.25f, 0f, 2.5f, 0.15f, 0.65f),
                Box(-0.9f, -0.15f, 0f, 0.2f, 0.65f, 0.55f),
                Box(0.9f, -0.15f, 0f, 0.2f, 0.65f, 0.55f)
            };
            _recipes["Domino"] = new[] { Box(0f, 0f, 0f, 0.65f, 1.3f, 0.18f) };
            _recipes["Bowling pin"] = new[]
            {
                Cylinder(0f, -0.25f, 0f, 0.38f, 0.5f),
                Ball(0f, 0f, 0f, 0.42f),
                Cylinder(0f, 0.3f, 0f, 0.15f, 0.4f),
                Ball(0f, 0.55f, 0f, 0.25f)
            };
            _recipes["Bowling ball"] = new[] { Ball(0f, 0f, 0f, 0.6f) };
            _recipes["Beach ball"] = new[] { Ball(0f, 0f, 0f, 1.4f) };
            _recipes["Dumbbell"] = new[]
            {
                new Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.1f, 0.6f, 0.1f), new Vector3(0f, 0f, 90f)),
                Ball(-0.6f, 0f, 0f, 0.5f),
                Ball(0.6f, 0f, 0f, 0.5f)
            };
            _recipes["Cross obstacle"] = new[]
            {
                Box(0f, 0f, 0f, 3f, 0.3f, 0.3f),
                Box(0f, 0f, 0f, 0.3f, 3f, 0.3f),
                Box(0f, 0f, 0f, 0.3f, 0.3f, 3f)
            };
            _recipes["Goal"] = new[]
            {
                Box(-2, 0f, 0f, 0.15f, 3f, 0.15f),
                Box(2f, 0f, 0f, 0.15f, 3f, 0.15f),
                Box(0f, 1.45f, 0f, 4f, 0.15f, 0.15f)
            };
            _recipes["Balance beam"] = new[]
            {
                Box(0f, 0.4f, 0f, 0.3f, 0.2f, 5f),
                Box(0f, -0.1f, -1.8f, 1.4f, 0.8f, 0.25f),
                Box(0f, -0.1f, 1.8f, 1.4f, 0.8f, 0.25f)
            };
            _recipes["Pallet"] = Enumerable.Range(0, 5).Select(i => Box(i * 0.3f - 0.6f, 0.1f, 0f, 0.22f, 0.1f, 1.3f))
                .Concat(new[] { Box(-0.5f, 0f, 0f, 0.18f, 0.15f, 1.3f), Box(0.5f, 0f, 0f, 0.18f, 0.15f, 1.3f) }).ToArray();
            _recipes["Bridge"] = new[]
            {
                Box(0f, 0f, 0f, 2.5f, 0.2f, 6f),
                Box(-1.15f, 0.65f, 0f, 0.1f, 1.2f, 0.1f),
                Box(1.15f, 0.65f, 0f, 0.1f, 1.2f, 0.1f),
                Box(-1.15f, 1.2f, 0f, 0.1f, 0.1f, 6f),
                Box(1.15f, 1.2f, 0f, 0.1f, 0.1f, 6f)
            };
            _recipes["Barricade"] = new[]
            {
                Box(-1, 0f, 0f, 0.2f, 1.4f, 0.8f),
                Box(1f, 0f, 0f, 0.2f, 1.4f, 0.8f),
                Box(0f, 0.5f, 0f, 2.5f, 0.3f, 0.15f),
                Box(0f, -0.15f, 0f, 2.5f, 0.3f, 0.15f)
            };
            _recipes["Hoop"] = Enumerable.Range(0, 12).Select(i =>
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                return new Part(
                        PrimitiveType.Cube,
                        new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f),
                        new Vector3(0.56f, 0.12f, 0.2f),
                        new Vector3(0f, 0f, i * 30f + 90f));
            }).ToArray();
        }

        internal GameObject Create(string name, bool collision)
        {
            if (!_recipes.TryGetValue(name, out var parts))
            {
                return null;
            }

            if (!_meshes.TryGetValue(name, out var mesh))
            {
                List<Vector3> vertices = new();
                List<Vector3> normals = new();
                List<Vector2> uv = new();
                List<int> triangles = new();
                for (int index = 0; index < parts.Length; index++)
                {
                    Part part = parts[index];
                    GameObject primitive = GameObject.CreatePrimitive(part.Shape);
                    primitive.SetActive(false);
                    Mesh source = primitive.GetComponent<MeshFilter>().sharedMesh;
                    int offset = vertices.Count;
                    Quaternion rotation = Quaternion.Euler(part.Angles);
                    vertices.AddRange(source.vertices.Select(point => rotation * Vector3.Scale(point, part.Size) + part.Position));
                    normals.AddRange(source.normals.Select(normal => (rotation * new Vector3(
                                normal.x / part.Size.x,
                                normal.y / part.Size.y,
                                normal.z / part.Size.z)).normalized));
                    uv.AddRange(source.uv);
                    triangles.AddRange(source.triangles.Select(value => value + offset));
                    Object.Destroy(primitive);
                }

                mesh = new Mesh

                {
                    name = "NullFrameProp_" + name,
                    vertices = new Il2CppStructArray<Vector3>(vertices.ToArray()),
                    normals = new Il2CppStructArray<Vector3>(normals.ToArray()),
                    uv = new Il2CppStructArray<Vector2>(uv.ToArray()),
                    triangles = new Il2CppStructArray<int>(triangles.ToArray())
                };
                mesh.RecalculateBounds();
                _meshes[name] = mesh;
            }

            GameObject root = new("NullFrameBuild");
            root.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = mesh;
            _material ??= new Material(Shader.Find("Standard"));
            root.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>().sharedMaterial = _material;
            if (collision)
            {
                foreach (Part part in parts)
                {
                    GameObject child = new("Collider");
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = part.Position;
                    child.transform.localRotation = Quaternion.Euler(part.Angles);
                    if (part.Shape == PrimitiveType.Sphere)
                    {
                        SphereCollider collider = child.AddComponent(Il2CppType.Of<SphereCollider>()).Cast<SphereCollider>();
                        collider.radius = part.Size.x * 0.5f;
                    }
                    else
                    {
                        BoxCollider collider = child.AddComponent(Il2CppType.Of<BoxCollider>()).Cast<BoxCollider>();
                        collider.size = part.Shape == PrimitiveType.Cylinder ? new Vector3(part.Size.x, part.Size.y * 2f, part.Size.z) : part.Size;
                    }
                }
            }

            return root;
        }
    }
}
