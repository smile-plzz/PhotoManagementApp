using System;
using System.IO;

namespace PhotoManagementApp
{
    public static class Logger
    {
        private static string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "application.log");

        public static void LogError(string message, Exception? ex = null)
        {
            Log("ERROR", message, ex);
        }

        public static void LogInfo(string message)
        {
            Log("INFO", message);
        }

        private static void Log(string level, string message, Exception? ex = null)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_logFilePath, true))
                {
                    sw.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
                    if (ex != null)
                    {
                        sw.WriteLine($"Exception: {ex.Message}");
                        sw.WriteLine($"Stack Trace: {ex.StackTrace}");
                    }
                }
            }
            catch (Exception logEx)
            {
                // Fallback: write to console if logging to file fails
                Console.WriteLine($"Error writing to log file: {logEx.Message}");
                Console.WriteLine($"Original Log Message: [{level}] {message}");
            }
        }
    }
}