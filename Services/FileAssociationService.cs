using System;
using System.IO;
using Microsoft.Win32;

namespace ModernPlayer.Services
{
    public static class FileAssociationService
    {
        public static bool RegisterOpenWith()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                string exeName = Path.GetFileName(exePath);

                // Register under HKCU\Software\Classes\Applications\ModernPlayer.exe
                using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{exeName}"))
                {
                    if (appKey != null)
                    {
                        appKey.SetValue("FriendlyAppName", "Modern Audio Player");

                        using (var shellKey = appKey.CreateSubKey(@"shell\open\command"))
                        {
                            shellKey?.SetValue("", $"\"{exePath}\" \"%1\"");
                        }

                        using (var supportedTypesKey = appKey.CreateSubKey("SupportedTypes"))
                        {
                            if (supportedTypesKey != null)
                            {
                                foreach (var ext in PlaylistService.SupportedExtensions)
                                {
                                    supportedTypesKey.SetValue(ext, string.Empty);
                                }
                            }
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
