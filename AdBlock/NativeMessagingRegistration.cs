using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MogLocker.AdBlock
{
    public static class NativeMessagingRegistration
    {
        public const string HostName = "com.moglocker.adblock";

        public static string ExtensionId
        {
            get { return ResolveExtensionId(); }
        }

        public static string ExtensionDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser-extension"); }
        }

        private static string ResolveExtensionId()
        {
            string manifestPath = Path.Combine(ExtensionDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new DirectoryNotFoundException(
                    "Расширение браузера не установлено. Повторно запустите bin\\MogLocker.exe.");

            string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            Match match = Regex.Match(manifestJson, "\"key\"\\s*:\\s*\"([^\"]+)\"");
            if (!match.Success)
                throw new InvalidOperationException("В manifest.json отсутствует стабильный ключ расширения (key).");

            return ComputeExtensionIdFromKey(match.Groups[1].Value);
        }

        private static string ComputeExtensionIdFromKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Пустой ключ расширения в manifest.json.");

            byte[] publicKey = Convert.FromBase64String(key);
            byte[] hash = SHA256.Create().ComputeHash(publicKey);
            StringBuilder builder = new StringBuilder();
            const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
            int value = 0;
            int bits = 0;

            foreach (byte b in hash)
            {
                value = (value << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    builder.Append(alphabet[(value >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            if (bits > 0)
                builder.Append(alphabet[(value << (5 - bits)) & 31]);

            return builder.ToString().Substring(0, 32).ToLowerInvariant();
        }

        public static void RegisterForCurrentUser()
        {
            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("Не найден исполняемый файл приложения.", executablePath);
            if (!File.Exists(Path.Combine(ExtensionDirectory, "manifest.json")))
                throw new DirectoryNotFoundException(
                    "Расширение браузера не установлено. Повторно запустите bin\\MogLocker.exe.");

            string manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MogLocker", "AdBlock", "native-host.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));

            var manifest = new Dictionary<string, object>
            {
                { "name", HostName },
                { "description", "Локальная связь браузерного расширения Моглокер AdBlock с пользовательскими правилами и статистикой." },
                { "path", executablePath },
                { "type", "stdio" },
                { "allowed_origins", new[] { "chrome-extension://" + ExtensionId + "/" } }
            };
            var serializer = new JavaScriptSerializer();
            string temporaryPath = manifestPath + ".tmp";
            File.WriteAllText(temporaryPath, serializer.Serialize(manifest), new UTF8Encoding(false));
            if (File.Exists(manifestPath))
                File.Replace(temporaryPath, manifestPath, null);
            else
                File.Move(temporaryPath, manifestPath);

            RegisterBrowser("Software\\Google\\Chrome\\NativeMessagingHosts\\" + HostName, manifestPath);
            RegisterBrowser("Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + HostName, manifestPath);
        }

        public static bool IsRegisteredForCurrentUser()
        {
            string expectedPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            return IsBrowserRegistered(
                "Software\\Google\\Chrome\\NativeMessagingHosts\\" + HostName, expectedPath) ||
                IsBrowserRegistered(
                    "Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + HostName, expectedPath);
        }

        private static bool IsBrowserRegistered(string keyPath, string expectedExecutable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                if (key == null)
                    return false;
                string registeredPath = key.GetValue(null) as string;
                string manifestPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MogLocker", "AdBlock", "native-host.json");
                if (!String.Equals(registeredPath, manifestPath, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(manifestPath))
                    return false;
                var serializer = new JavaScriptSerializer();
                Dictionary<string, object> manifest = serializer.DeserializeObject(
                    File.ReadAllText(manifestPath)) as Dictionary<string, object>;
                object executable;
                return manifest != null && manifest.TryGetValue("path", out executable) &&
                    String.Equals(Convert.ToString(executable), expectedExecutable,
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void RegisterBrowser(string keyPath, string manifestPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (key == null)
                    throw new IOException("Не удалось зарегистрировать native messaging host: " + keyPath);
                key.SetValue(null, manifestPath, RegistryValueKind.String);
            }
        }
    }
}
