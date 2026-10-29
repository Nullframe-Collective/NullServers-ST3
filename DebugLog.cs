using MelonLoader;

namespace NullServers
{
    // Technical messages that only help when looking into a bug. Players see them only in a Debug build
    // or when the game is started with --melonloader.debug; a normal Release build keeps the log short.
    internal static class DebugLog
    {
#if DEBUG
        internal static bool Enabled => true;
#else
        internal static bool Enabled => MelonDebug.IsEnabled();
#endif

        internal static void Debug(this MelonLogger.Instance logger, string message)
        {
            if (Enabled)
            {
                logger.Msg(message);
            }
        }
    }
}
