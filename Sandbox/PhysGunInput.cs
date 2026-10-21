namespace S3_NullServers.Sandbox
{
    internal readonly record struct PhysGunInput(
        bool Allowed = false,
        bool Down = false,
        bool Held = false,
        bool Up = false,
        bool Secondary = false,
        bool Throw = false,
        bool Cancel = false,
        bool Toggle = false,
        bool SwitchWeapon = false,
        bool Rotate = false,
        bool Roll = false,
        float Wheel = 0f,
        float MouseX = 0f,
        float MouseY = 0f);
}
