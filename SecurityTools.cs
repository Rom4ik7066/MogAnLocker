using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MogLocker
{
    internal enum WindowsUnlockFeature
    {
        TaskManager,
        RegistryEditor,
        CommandPrompt,
        ControlPanel
    }

    internal sealed class WindowsUnlockStatus
    {
        public bool CurrentUserRestricted { get; set; }
        public bool MachineRestricted { get; set; }
    }

    internal static class WindowsUnlockTools
    {
        private sealed class PolicyValue
        {
            public readonly string Path;
            public readonly string Name;

            public PolicyValue(string path, string name)
            {
                Path = path;
                Name = name;
            }
        }

        private const string CurrentSystemPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private static readonly string ExplorerPolicy =
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private static readonly string ManagedExplorerPolicy =
            @"Software\Policies\Microsoft\Windows\Explorer";

        public static WindowsUnlockStatus GetStatus(WindowsUnlockFeature feature)
        {
            PolicyValue[] values = GetValues(feature);
            var status = new WindowsUnlockStatus();
            using (RegistryKey machine = GetMachineRegistry())
            {
                foreach (PolicyValue value in values)
                {
                    if (HasEnabledValue(Registry.CurrentUser, value.Path, value.Name))
                        status.CurrentUserRestricted = true;
                    if (HasEnabledValue(machine, value.Path, value.Name))
                        status.MachineRestricted = true;
                }
            }
            return status;
        }

        public static void RemoveCurrentUserRestrictions(WindowsUnlockFeature feature)
        {
            WindowsUnlockStatus status = GetStatus(feature);
            if (status.MachineRestricted)
                throw new InvalidOperationException(
                    "A machine-wide Windows policy is active. MogLocker does not change machine or organization policies.");
            if (!status.CurrentUserRestricted)
                return;

            foreach (PolicyValue value in GetValues(feature))
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(value.Path, true))
                {
                    if (key != null && HasEnabledValue(key, value.Name))
                        key.DeleteValue(value.Name, false);
                }
            }

            WindowsUnlockStatus remaining = GetStatus(feature);
            if (remaining.CurrentUserRestricted || remaining.MachineRestricted)
                throw new InvalidOperationException(
                    "The restriction is still active. Windows policy may be managed by an administrator.");
        }

        private static PolicyValue[] GetValues(WindowsUnlockFeature feature)
        {
            switch (feature)
            {
                case WindowsUnlockFeature.TaskManager:
                    return new[] { new PolicyValue(CurrentSystemPolicy, "DisableTaskMgr") };
                case WindowsUnlockFeature.RegistryEditor:
                    return new[] { new PolicyValue(CurrentSystemPolicy, "DisableRegistryTools") };
                case WindowsUnlockFeature.CommandPrompt:
                    return new[] { new PolicyValue(@"Software\Policies\Microsoft\Windows\System", "DisableCMD") };
                case WindowsUnlockFeature.ControlPanel:
                    return new[]
                    {
                        new PolicyValue(ExplorerPolicy, "NoControlPanel"),
                        new PolicyValue(ManagedExplorerPolicy, "NoControlPanel")
                    };
                default:
                    throw new ArgumentOutOfRangeException("feature");
            }
        }

        private static bool HasEnabledValue(RegistryKey root, string path, string name)
        {
            using (RegistryKey key = root.OpenSubKey(path, false))
                return key != null && HasEnabledValue(key, name);
        }

        private static bool HasEnabledValue(RegistryKey key, string name)
        {
            object value = key.GetValue(name, null);
            return value != null &&
                Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
        }

        private static RegistryKey GetMachineRegistry()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }
    }

    internal sealed class DefenderExclusion
    {
        public string Kind { get; private set; }
        public string Value { get; private set; }
        internal string Parameter { get; private set; }

        public DefenderExclusion(string kind, string value, string parameter)
        {
            Kind = kind;
            Value = value;
            Parameter = parameter;
        }
    }

    internal static class DefenderExclusionManager
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static IList<DefenderExclusion> Read()
        {
            return Read(false);
        }

        public static IList<DefenderExclusion> ReadElevated()
        {
            return Read(true);
        }

        private static IList<DefenderExclusion> Read(bool elevate)
        {
            const string script =
                "$ErrorActionPreference='Stop';" +
                "$p=Get-MpPreference;" +
                "$items=@();" +
                "foreach($v in $p.ExclusionPath){if($null -ne $v){if([string]$v -like 'N/A:*administrator*'){throw 'Administrator approval is required to view Defender exclusions.'};$items+=@{Value=[string]$v;Parameter='ExclusionPath'}}};" +
                "foreach($v in $p.ExclusionProcess){if($null -ne $v){if([string]$v -like 'N/A:*administrator*'){throw 'Administrator approval is required to view Defender exclusions.'};$items+=@{Value=[string]$v;Parameter='ExclusionProcess'}}};" +
                "foreach($v in $p.ExclusionExtension){if($null -ne $v){if([string]$v -like 'N/A:*administrator*'){throw 'Administrator approval is required to view Defender exclusions.'};$items+=@{Value=[string]$v;Parameter='ExclusionExtension'}}};" +
                "foreach($v in $p.ExclusionIpAddress){if($null -ne $v){if([string]$v -like 'N/A:*administrator*'){throw 'Administrator approval is required to view Defender exclusions.'};$items+=@{Value=[string]$v;Parameter='ExclusionIpAddress'}}};" +
                "$json=ConvertTo-Json -InputObject @($items) -Compress;" +
                "$payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json));" +
                "[Console]::Out.Write($payload)";

            string output;
            string elevatedOutputPath = null;
            if (elevate)
            {
                elevatedOutputPath = Path.Combine(Path.GetTempPath(),
                    "MogLocker-Defender-" + Guid.NewGuid().ToString("N") + ".tmp");
                string escapedPath = elevatedOutputPath.Replace("'", "''");
                RunPowerShell(script + ";[IO.File]::WriteAllText('" + escapedPath +
                    "',$payload,[Text.Encoding]::ASCII)", true);
                try
                {
                    output = File.ReadAllText(elevatedOutputPath, Encoding.ASCII);
                }
                finally
                {
                    if (File.Exists(elevatedOutputPath))
                        File.Delete(elevatedOutputPath);
                }
            }
            else
            {
                output = RunPowerShell(script, false);
            }

            string json;
            try
            {
                json = Encoding.UTF8.GetString(Convert.FromBase64String(output.Trim()));
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("PowerShell returned invalid Defender exclusion data.", ex);
            }

            object[] entries = Serializer.DeserializeObject(json) as object[];
            var result = new List<DefenderExclusion>();
            if (entries == null)
                return result;

            foreach (object entry in entries)
            {
                var values = entry as Dictionary<string, object>;
                if (values == null)
                    throw new InvalidDataException("PowerShell returned an invalid exclusion entry.");
                string value = Convert.ToString(values["Value"]);
                string parameter = Convert.ToString(values["Parameter"]);
                if (String.IsNullOrWhiteSpace(value) || !IsAllowedParameter(parameter))
                    throw new InvalidDataException("PowerShell returned an invalid exclusion value.");
                result.Add(new DefenderExclusion(GetLocalizedKind(parameter), value, parameter));
            }
            return result;
        }

        public static void Remove(IEnumerable<DefenderExclusion> exclusions)
        {
            if (exclusions == null)
                throw new ArgumentNullException("exclusions");

            var grouped = exclusions
                .Where(item => item != null && IsAllowedParameter(item.Parameter))
                .GroupBy(item => item.Parameter)
                .ToList();
            if (grouped.Count == 0)
                throw new ArgumentException("No valid Defender exclusions were selected.", "exclusions");

            var script = new StringBuilder("$ErrorActionPreference='Stop';try{");
            foreach (var group in grouped)
            {
                string values = String.Join(",", group.Select(item =>
                    "'" + item.Value.Replace("'", "''") + "'").ToArray());
                script.Append("Remove-MpPreference -").Append(group.Key)
                    .Append(" @(").Append(values).Append(") -ErrorAction Stop;");
            }
            script.Append("exit 0}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}");

            RunPowerShell(script.ToString(), true);
        }

        private static bool IsAllowedParameter(string parameter)
        {
            return parameter == "ExclusionPath" || parameter == "ExclusionProcess" ||
                parameter == "ExclusionExtension" || parameter == "ExclusionIpAddress";
        }

        private static string GetLocalizedKind(string parameter)
        {
            switch (parameter)
            {
                case "ExclusionPath": return "Путь";
                case "ExclusionProcess": return "Процесс";
                case "ExclusionExtension": return "Расширение";
                case "ExclusionIpAddress": return "IP-адрес";
                default: throw new ArgumentOutOfRangeException("parameter");
            }
        }

        private static string RunPowerShell(string script, bool elevate)
        {
            string executable = FindPowerShell();
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded,
                UseShellExecute = elevate,
                Verb = elevate ? "runas" : String.Empty,
                CreateNoWindow = !elevate,
                RedirectStandardOutput = !elevate,
                RedirectStandardError = !elevate
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                    throw new InvalidOperationException("PowerShell did not start.");
                Task<string> output = elevate
                    ? Task.FromResult(String.Empty)
                    : process.StandardOutput.ReadToEndAsync();
                Task<string> errorOutput = elevate
                    ? Task.FromResult(String.Empty)
                    : process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    throw new TimeoutException("PowerShell timed out while accessing Microsoft Defender.");
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string error = elevate
                        ? "Elevated PowerShell could not complete the Microsoft Defender operation."
                        : errorOutput.Result;
                    throw new InvalidOperationException(String.IsNullOrWhiteSpace(error)
                        ? "PowerShell could not complete the Microsoft Defender operation."
                        : error.Trim());
                }
                return output.Result;
            }
        }

        private static string FindPowerShell()
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string sysnativePath = Path.Combine(windowsDirectory,
                    @"Sysnative\WindowsPowerShell\v1.0\powershell.exe");
                if (File.Exists(sysnativePath))
                    return sysnativePath;
            }

            string nativePath = Path.Combine(systemDirectory,
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (File.Exists(nativePath))
                return nativePath;

            string windowsDirectoryFallback = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sysnativePathFallback = Path.Combine(windowsDirectoryFallback,
                @"Sysnative\WindowsPowerShell\v1.0\powershell.exe");
            if (File.Exists(sysnativePathFallback))
                return sysnativePathFallback;
            throw new FileNotFoundException("Windows PowerShell 5.1 was not found.");
        }
    }

    internal sealed class DefenderExclusionsForm : Form
    {
        private readonly ListView exclusionsList;
        private readonly Button refreshButton;
        private readonly Button removeButton;
        private readonly Label emptyStateLabel;
        private readonly bool english;

        public DefenderExclusionsForm(bool english)
        {
            this.english = english;
            Text = T("Исключения Microsoft Defender");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            MinimumSize = new System.Drawing.Size(600, 360);
            Size = new System.Drawing.Size(760, 500);
            BackColor = System.Drawing.Color.FromArgb(18, 26, 45);
            ForeColor = System.Drawing.Color.FromArgb(226, 232, 244);
            Font = new System.Drawing.Font("Segoe UI", 9F);

            var description = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(12, 8, 12, 4),
                Text = T("Отметьте исключения, которые хотите удалить. Для изменения настроек Windows потребуется подтверждение UAC."),
                ForeColor = ForeColor
            };
            exclusionsList = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = System.Drawing.Color.FromArgb(14, 21, 36),
                ForeColor = ForeColor
            };
            exclusionsList.Columns.Add(T("Тип"), 120);
            exclusionsList.Columns.Add(T("Значение"), 560);
            emptyStateLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.FromArgb(143, 153, 171),
                Padding = new Padding(24),
                Visible = true
            };
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            listHost.Controls.Add(exclusionsList);
            listHost.Controls.Add(emptyStateLabel);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                WrapContents = false
            };
            removeButton = new Button { Text = T("Удалить выбранные"), Width = 150, Height = 30 };
            refreshButton = new Button { Text = T("Обновить список"), Width = 130, Height = 30 };
            var closeButton = new Button { Text = T("Закрыть"), Width = 90, Height = 30 };
            removeButton.Click += RemoveSelected;
            refreshButton.Click += RefreshList;
            closeButton.Click += delegate { Close(); };
            footer.Controls.Add(closeButton);
            footer.Controls.Add(removeButton);
            footer.Controls.Add(refreshButton);
            Controls.Add(listHost);
            Controls.Add(footer);
            Controls.Add(description);
            Shown += RefreshList;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!refreshButton.Enabled)
                    e.Cancel = true;
            };
        }

        private async void RefreshList(object sender, EventArgs e)
        {
            SetBusy(true);
            exclusionsList.Items.Clear();
            emptyStateLabel.Text = T("Ожидание подтверждения UAC для чтения списка исключений…");
            emptyStateLabel.Visible = true;
            try
            {
                IList<DefenderExclusion> exclusions =
                    await Task.Run(() => DefenderExclusionManager.ReadElevated());
                exclusionsList.Items.Clear();
                foreach (DefenderExclusion exclusion in exclusions)
                {
                    var item = new ListViewItem(T(exclusion.Kind));
                    item.SubItems.Add(exclusion.Value);
                    item.Tag = exclusion;
                    exclusionsList.Items.Add(item);
                }
                emptyStateLabel.Text = exclusions.Count == 0
                    ? T("Исключения Microsoft Defender не найдены.")
                    : String.Empty;
                emptyStateLabel.Visible = exclusions.Count == 0;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    emptyStateLabel.Text = T("Запрос UAC отменён. Данные об исключениях не загружены.");
                    emptyStateLabel.Visible = true;
                }
                else
                {
                    emptyStateLabel.Text = T("Не удалось получить исключения Microsoft Defender.");
                    emptyStateLabel.Visible = true;
                    MessageBox.Show(this, T("Не удалось получить исключения Microsoft Defender:\r\n\r\n") + ex.Message,
                        T("Ошибка PowerShell"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                emptyStateLabel.Text = T("Не удалось получить исключения Microsoft Defender.");
                emptyStateLabel.Visible = true;
                MessageBox.Show(this, T("Не удалось получить исключения Microsoft Defender:\r\n\r\n") + ex.Message,
                    T("Ошибка PowerShell"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void RemoveSelected(object sender, EventArgs e)
        {
            List<DefenderExclusion> selected = exclusionsList.Items
                .Cast<ListViewItem>()
                .Where(item => item.Checked)
                .Select(item => item.Tag as DefenderExclusion)
                .Where(item => item != null)
                .ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, T("Сначала отметьте исключения для удаления."),
                    T("Ничего не выбрано"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string preview = String.Join("\r\n", selected.Take(8).Select(item =>
                T(item.Kind) + ": " + item.Value).ToArray());
            if (selected.Count > 8)
                preview += T("\r\n… и ещё ") + (selected.Count - 8) + ".";
            if (MessageBox.Show(this,
                T("Удалить выбранные исключения Microsoft Defender?\r\n\r\n") + preview +
                T("\r\n\r\nWindows покажет запрос контроля учётных записей."),
                T("Подтвердите удаление"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            SetBusy(true);
            try
            {
                await Task.Run(() => DefenderExclusionManager.Remove(selected));
                exclusionsList.Items.Clear();
                emptyStateLabel.Text = T("Команда удаления выполнена. Список не перечитан; нажмите «Обновить список» для проверки.");
                emptyStateLabel.Visible = true;
                MessageBox.Show(this,
                    T("Команда удаления выполнена. Список не перечитан; нажмите «Обновить список» для проверки."),
                    "Microsoft Defender", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, T(ex.NativeErrorCode == 1223
                    ? "Запрос UAC был отменён. Исключения не изменены."
                    : "Не удалось запустить PowerShell:\r\n\r\n") + (ex.NativeErrorCode == 1223 ? String.Empty : ex.Message),
                    T("Операция отменена или не выполнена"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Не удалось удалить исключения Microsoft Defender:\r\n\r\n") + ex.Message,
                    T("Ошибка PowerShell"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string T(string text)
        {
            return UiLocalization.Translate(text, english);
        }

        private void SetBusy(bool busy)
        {
            refreshButton.Enabled = !busy;
            removeButton.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
    }

    internal sealed class GitHubRelease
    {
        public string TagName { get; set; }
        public string Url { get; set; }
    }

    internal static class GitHubUpdateService
    {
        public const string ReleasesUrl = "https://github.com/Rom4ik7066/MogAnLocker/releases";
        private const string LatestReleaseApi =
            "https://api.github.com/repos/Rom4ik7066/MogAnLocker/releases?per_page=1";

        public static GitHubRelease CheckLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);
            request.Method = "GET";
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "MogLocker-UpdateChecker/" +
                Assembly.GetExecutingAssembly().GetName().Version;
            request.Timeout = 10000;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                object[] releases = new JavaScriptSerializer()
                    .DeserializeObject(reader.ReadToEnd()) as object[];
                if (releases == null || releases.Length == 0)
                    throw new FileNotFoundException("GitHub has no published releases.");
                var values = releases[0] as Dictionary<string, object>;
                if (values == null || !values.ContainsKey("tag_name") ||
                    !values.ContainsKey("html_url"))
                    throw new InvalidDataException("GitHub returned release data in an unexpected format.");

                string tagName = Convert.ToString(values["tag_name"]);
                Uri releaseUri;
                string releaseUrl = Convert.ToString(values["html_url"]);
                if (String.IsNullOrWhiteSpace(tagName) ||
                    !Uri.TryCreate(releaseUrl, UriKind.Absolute, out releaseUri) ||
                    releaseUri.Scheme != Uri.UriSchemeHttps ||
                    !String.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GitHub returned an invalid release URL.");

                return new GitHubRelease
                {
                    TagName = tagName,
                    Url = releaseUrl
                };
            }
        }
    }
}
