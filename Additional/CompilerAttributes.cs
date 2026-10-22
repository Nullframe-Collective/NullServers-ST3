namespace System.Runtime.CompilerServices
{
    // The generated game assemblies expose an IL2CPP placeholder which cannot annotate managed records.
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class NullableAttribute : Attribute
    {
        public readonly byte[] NullableFlags;

        public NullableAttribute(byte value)
        {
            NullableFlags = [value];
        }

        public NullableAttribute(byte[] values)
        {
            NullableFlags = values;
        }
    }
}
