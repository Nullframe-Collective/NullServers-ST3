using NullServers.Room;

namespace NullServers.Customization
{
    internal sealed record Appearance
    (
        int Hat,
        string FurColor,
        bool UseSkin,
        string HeadUrl,
        string BodyUrl,
        string ArmsUrl,
        string LegsUrl
    )
    {
        internal const int UrlLimit = 512;

        internal string[] Urls =>
        [
            HeadUrl ?? "",
            BodyUrl ?? "",
            ArmsUrl ?? "",
            LegsUrl ?? ""
        ];

        internal string SkinData => UseSkin && Urls.Any(url => url.Length > 0) ? string.Join("|", Urls) : "";

        internal bool SameLook(Appearance other)
        {
            return 
                other != null &&
                Hat == other.Hat &&
                FurColor == other.FurColor &&
                UseSkin == other.UseSkin &&
                (!UseSkin ||
                (HeadUrl == other.HeadUrl &&
                BodyUrl == other.BodyUrl &&
                ArmsUrl == other.ArmsUrl &&
                LegsUrl == other.LegsUrl));
        }

        internal bool Validate(int hatCount, out string error)
        {
            error = "Choose an available hat and a valid fur color.";

            if (Hat < 0 || Hat >= hatCount || !RoomOptions.IsValidColor(FurColor))
            {
                return false;
            }

            error = "Skin URLs must be HTTP or HTTPS image links, without spaces or the | character.";

            if (Urls.Any(url => !ValidUrl(url)))
            {
                return false;
            }

            error = null;

            return true;
        }

        internal static bool ValidUrl(string url)
        {
            return 
                string.IsNullOrEmpty(url) ||
                (url.Length <= UrlLimit &&
                !url.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '|') &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps) &&
                string.IsNullOrEmpty(uri.UserInfo));
        }
    }
}
