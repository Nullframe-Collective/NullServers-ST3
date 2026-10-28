namespace System.Runtime.CompilerServices
{
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
