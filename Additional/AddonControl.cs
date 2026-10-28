using HarmonyLib;
using System.Reflection;

namespace NullServers.Additional
{
    internal sealed class AddonControl : IDisposable
    {
        private static readonly Dictionary<MethodBase, AddonControl> Controls = new();
        private static readonly MethodInfo Prefix = typeof(AddonControl).GetMethod(nameof(BeforeContent), BindingFlags.Static | BindingFlags.NonPublic);
        private readonly HarmonyLib.Harmony _harmony;
        private readonly PropertyInfo _property;
        private readonly List<MethodInfo> _methods = new();

        internal bool Enabled { get; private set; } = true;
        internal Action<object[]> Disabled { get; set; }

        internal AddonControl(HarmonyLib.Harmony harmony, PropertyInfo property, params MethodInfo[] methods)
        {
            _harmony = harmony;
            _property = property;

            if (property?.CanWrite == true && property.PropertyType == typeof(bool) && property.SetMethod.IsStatic)
            {
                return;
            }

            if (property != null || methods.Length == 0 || methods.Any(method => method == null))
            {
                throw new InvalidOperationException("The addon does not expose content controls or supported registration hooks.");
            }

            try
            {
                foreach (MethodInfo method in methods.Distinct())
                {
                    Controls.Add(method, this);
                    _methods.Add(method);
                    harmony.Patch(method, prefix: new HarmonyMethod(Prefix));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static bool BeforeContent(MethodBase __originalMethod, object[] __args)
        {
            AddonControl control = Controls[__originalMethod];

            if (!control.Enabled)
            {
                control.Disabled?.Invoke(__args);
            }

            return control.Enabled;
        }

        internal void Set(bool enabled)
        {
            _property?.SetValue(null, enabled);
            Enabled = enabled;
        }

        public void Dispose()
        {
            foreach (MethodInfo method in _methods)
            {
                _harmony.Unpatch(method, Prefix);
                Controls.Remove(method);
            }

            _methods.Clear();
        }
    }
}
