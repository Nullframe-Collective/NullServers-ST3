using UnityEngine;

namespace NullServers.Voice
{
    public static class VoiceIcons
    {
        private static Texture2D _speaker;
        private static Texture2D _microphone;
        public static Texture2D Speaker => _speaker ? _speaker : _speaker = Create(false);
        public static Texture2D Microphone => _microphone ? _microphone : _microphone = Create(true);

        private static Texture2D Create(bool microphone)
        {
            Texture2D texture = new(48, 48, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 48; x++)
                {
                    float dx = x - 23.5f;
                    float dy = y - 27f;
                    bool ink;

                    if (microphone)
                    {
                        float cap = Math.Max(0f, Math.Abs(dy) - 7f);
                        float ring = dx * dx + (y - 22f) * (y - 22f);
                        ink = dx * dx + cap * cap < 30f ||
                            y >= 15 && y <= 28 && Math.Abs(dx) is >= 10f and <= 13f ||
                            y < 22 && ring is >= 100f and <= 169f ||
                            Math.Abs(dx) <= 1.5f && y is >= 5 and <= 12 || y is >= 3 and <= 5 && Math.Abs(dx) <= 9f;
                    }
                    else
                    {
                        float radius = MathF.Sqrt((x - 18f) * (x - 18f) + (y - 24f) * (y - 24f));
                        ink = x is >= 6 and <= 14 && y is >= 17 and <= 31 ||
                            x is >= 14 and <= 24 && Math.Abs(y - 24) <= x - 6 ||
                            x > 27 && Math.Abs(y - 24) < 17 &&
                            (radius is >= 14f and <= 17f || radius is >= 23f and <= 26f);
                    }

                    texture.SetPixel(x, y, ink ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
