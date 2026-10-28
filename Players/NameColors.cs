using System.Text;

namespace NullServers.Players
{
    internal enum NameColorMode
    {
        Static,
        Rgb,
        Gradient
    }

    internal static class NameColors
    {
        internal static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        internal static string Color(PlayerProfile profile, double time, double offset = 0)
        {
            if (profile.ColorMode == NameColorMode.Static) return profile.ColorHex;

            double phase = time * profile.ColorSpeed * 0.2 + offset;
            if (profile.ColorMode == NameColorMode.Gradient)
            {
                return Gradient(Convert.ToInt32(profile.ColorHex[1..], 16), Convert.ToInt32(profile.GradientColor[1..], 16), phase);
            }

            double hue = (phase - Math.Floor(phase)) * 6;
            int sector = (int)hue;
            int rising = (int)Math.Round((hue - sector) * 255);
            int falling = 255 - rising;
            return sector switch
            {
                0 => $"#FF{rising:X2}00",
                1 => $"#{falling:X2}FF00",
                2 => $"#00FF{rising:X2}",
                3 => $"#00{falling:X2}FF",
                4 => $"#{rising:X2}00FF",
                _ => $"#FF00{falling:X2}"
            };
        }

        internal static string RichName(PlayerProfile profile, double time)
        {
            if (profile.ColorMode != NameColorMode.Gradient)
            {
                return $"<color={Color(profile, time)}>{profile.Nickname}</color>";
            }

            Rune[] letters = profile.Nickname.EnumerateRunes().ToArray();
            if (letters.Length == 0) return "";

            int first = Convert.ToInt32(profile.ColorHex[1..], 16);
            int second = Convert.ToInt32(profile.GradientColor[1..], 16);
            double phase = time * profile.ColorSpeed * 0.2;
            StringBuilder result = new();

            for (int index = 0; index < letters.Length; index++)
            {
                result.Append("<color=").Append(Gradient(first, second, phase + index / (double)letters.Length))
                    .Append('>').Append(letters[index]).Append("</color>");
            }

            return result.ToString();
        }

        private static string Gradient(int first, int second, double phase)
        {
            double amount = (Math.Sin(phase * Math.PI * 2) + 1) * 0.5;
            int Channel(int shift) => (int)Math.Round(((first >> shift) & 255) * (1 - amount) + ((second >> shift) & 255) * amount);
            return $"#{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}";
        }
    }
}
