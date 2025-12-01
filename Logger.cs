using System;
using System.IO;

namespace RScreenRec
{
    internal static class Logger
    {
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "screenrec.log");
        private static readonly object Sync = new object();

        public static void Log(string message, Exception ex = null)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{(ex != null ? " | " + ex : string.Empty)}{Environment.NewLine}"
                    );
                }
            }
            catch
            {
                // Best effort logging; ignore failures.
            }
        }

        public static string GetLogPath() => LogPath;
    }
}
