namespace NullServers.Customization
{
    internal sealed record RagdollSurface(string Name, string Mesh, int Part);

    internal sealed partial record RagdollSkin(Appearance Appearance, RagdollSurface[] Surfaces, string Hat, string[] ExtraHats = null, string[] CustomHats = null)
    {
        internal bool Valid =>
            Appearance != null &&
            Appearance.Validate(256, out _) &&
            Hat is { Length: <= 128 } &&
            (ExtraHats == null || ExtraHats is { Length: <= Network.HatEntry.ExtraSlots } &&
            ExtraHats.All(hat => hat is { Length: <= 128 })) &&
            (CustomHats == null || CustomHats is { Length: <= 4 } &&
            CustomHats.All(hat => hat is { Length: > 0 } && Network.HatEntry.ValidKey(hat) && !hat.StartsWith("base:")) &&
            CustomHats.Distinct(StringComparer.Ordinal).Count() == CustomHats.Length) &&
            Surfaces is { Length: > 0 and <= 16 } &&
            Surfaces.All(surface => surface != null &&
            surface.Name is { Length: <= 128 } &&
            surface.Mesh is { Length: <= 128 } &&
            surface.Part is >= -1 and <= 3);
    }
}
