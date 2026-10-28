using System.Text;
using System.Text.Json;

namespace NullServers.Network
{
    internal static class JsonPacket
    {
        internal static T Read<T>(string json, int maximum) where T : class
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > maximum)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
