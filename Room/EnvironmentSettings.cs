namespace NullServers.Room
{
    internal sealed record RenderOptions(
        bool Fog,
        int FogMode,
        string FogColor,
        float FogDensity,
        float FogStart,
        float FogEnd,
        bool Skybox,
        string SkyboxName,
        float Exposure,
        string AmbientColor,
        float AmbientIntensity,
        float ReflectionIntensity,
        float SunIntensity,
        string SkyboxUrl = "")
    {
        internal bool Valid =>
            FogMode is >= 1 and <= 3 &&
            RoomOptions.IsValidColor(FogColor) &&
            RoomOptions.IsValidColor(AmbientColor) &&
            SkyboxName is { Length: <= 128 } &&
            Customization.Appearance.ValidUrl(SkyboxUrl) &&
            new[] { FogDensity, FogStart, FogEnd, Exposure, AmbientIntensity, ReflectionIntensity, SunIntensity }.All(float.IsFinite) &&
            FogDensity is >= 0f and <= 1f &&
            FogStart is >= 0f and <= 50000f &&
            FogEnd >= FogStart && FogEnd <= 50000f &&
            Exposure is >= 0f and <= 8f &&
            AmbientIntensity is >= 0f and <= 8f &&
            ReflectionIntensity is >= 0f and <= 2f &&
            SunIntensity is >= 0f and <= 8f;
    }

    internal sealed record MapPart(
        string Path,
        Network.Point3 Position,
        Network.Rotation4 Rotation,
        Network.Point3 Scale,
        string Color = "#FFFFFF",
        bool Visible = true,
        bool Solid = true)
    {
        internal bool Valid => Path is { Length: > 0 and <= 256 } && Path.Split('/').All(part => int.TryParse(part, out int index) && index >= 0) &&
            Position?.Valid() == true && Rotation?.Valid == true && Scale?.Valid(100000f) == true &&
            Scale.X >= 0.05f && Scale.Y >= 0.05f && Scale.Z >= 0.05f && RoomOptions.IsValidColor(Color);
    }

    internal sealed record WorldSettings(
        Network.Point3 Scale,
        Network.Point3 Rotation,
        Network.Point3 Gravity,
        MapPart[] Parts = null)
    {
        internal static readonly WorldSettings Default = new(new(1f, 1f, 1f), new(0f, 0f, 0f), new(0f, -9.81f, 0f));
        internal bool Valid => Scale?.Valid(10f) == true && Scale.X >= 0.1f && Scale.Y >= 0.1f && Scale.Z >= 0.1f &&
            Rotation?.Valid(360f) == true && Gravity?.Valid(100f) == true &&
            (Parts == null || Parts.Length <= 64 && Parts.All(part => part?.Valid == true) && Parts.Select(part => part.Path).Distinct().Count() == Parts.Length);
    }

    internal sealed record EnvironmentSettings(
        bool AllowFlight = false,
        RenderOptions Rendering = null,
        bool KeepRagdolls = false,
        bool ThrowAnyone = false,
        WorldSettings World = null,
        bool AllowProne = false,
        float FlightSpeed = 7f,
        bool FlightNoclip = true)
    {
        internal const float MinFlightSpeed = 1f, MaxFlightSpeed = 60f;

        internal bool Valid => (Rendering == null || Rendering.Valid) && (World == null || World.Valid) &&
            float.IsFinite(FlightSpeed) && FlightSpeed is >= MinFlightSpeed and <= MaxFlightSpeed;
    }
}
