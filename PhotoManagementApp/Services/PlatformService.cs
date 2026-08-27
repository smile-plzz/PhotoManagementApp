using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoManagementApp.Services
{
    /// <summary>
    /// The handful of operations that have to be done differently per OS:
    /// showing a file in the system file manager and opening it in the default viewer.
    /// </summary>
    public static class PlatformService
    {
        /// <summary>
        /// Opens the system file manager with the file selected. Falls back to
        /// opening the containing folder where selecting is not supported.
        /// Returns false if nothing could be launched.
        /// </summary>
        public static bool RevealInFileManager(string filePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Start("explorer.exe", $"/select,\"{filePath}\"");
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Start("open", $"-R \"{filePath}\"");
                    return true;
                }

                // Linux desktops vary; the freedesktop file-manager interface is
                // the closest thing to a standard, with the folder as a fallback.
                var folder = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(folder))
                    return false;

                try
                {
                    Start("dbus-send",
                        "--session --print-reply --dest=org.freedesktop.FileManager1 " +
                        "--type=method_call /org/freedesktop/FileManager1 " +
                        $"org.freedesktop.FileManager1.ShowItems array:string:\"file://{filePath}\" string:\"\"");
                    return true;
                }
                catch (Exception)
                {
                    Start("xdg-open", $"\"{folder}\"");
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Opens a file with whatever the OS considers its default application.</summary>
        public static bool OpenWithDefaultApplication(string filePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Start("open", $"\"{filePath}\"");
                }
                else
                {
                    Start("xdg-open", $"\"{filePath}\"");
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Start(string fileName, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        /// <summary>
        /// Copies a file into a destination folder, adding " (2)", " (3)" and so on
        /// rather than overwriting when the name is already taken.
        /// Returns the path actually written.
        /// </summary>
        public static string CopyToFolder(string sourcePath, string destinationFolder)
        {
            System.IO.Directory.CreateDirectory(destinationFolder);

            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);
            var target = Path.Combine(destinationFolder, name + extension);

            var counter = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(destinationFolder, $"{name} ({counter}){extension}");
                counter++;
            }

            File.Copy(sourcePath, target);
            return target;
        }
    }
}
