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

namespace MogLocker
{
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
            const string script =
                "$ErrorActionPreference='Stop';" +
                "$p=Get-MpPreference;" +
                "$items=@();" +
                "foreach($v in $p.ExclusionPath){if($null -ne $v){$items+=@{Value=[string]$v;Parameter='ExclusionPath'}}};" +
                "foreach($v in $p.ExclusionProcess){if($null -ne $v){$items+=@{Value=[string]$v;Parameter='ExclusionProcess'}}};" +
                "foreach($v in $p.ExclusionExtension){if($null -ne $v){$items+=@{Value=[string]$v;Parameter='ExclusionExtension'}}};" +
                "foreach($v in $p.ExclusionIpAddress){if($null -ne $v){$items+=@{Value=[string]$v;Parameter='ExclusionIpAddress'}}};" +
                "$json=ConvertTo-Json -InputObject @($items) -Compress;" +
                "[Console]::Out.Write([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))";

            string output = RunPowerShell(script, false);
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
                        ? "Elevated PowerShell could not update Microsoft Defender exclusions."
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

        public DefenderExclusionsForm()
        {
            Text = "Исключения Microsoft Defender";
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
                Text = "Отметьте исключения, которые хотите удалить. Для изменения настроек Windows потребуется подтверждение UAC.",
                ForeColor = ForeColor
            };
            exclusionsList = new ListView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = System.Drawing.Color.FromArgb(14, 21, 36),
                ForeColor = ForeColor
            };
            exclusionsList.Columns.Add("Тип", 120);
            exclusionsList.Columns.Add("Значение", 560);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                WrapContents = false
            };
            removeButton = new Button { Text = "Удалить выбранные", Width = 150, Height = 30 };
            refreshButton = new Button { Text = "Обновить список", Width = 130, Height = 30 };
            var closeButton = new Button { Text = "Закрыть", Width = 90, Height = 30 };
            removeButton.Click += RemoveSelected;
            refreshButton.Click += RefreshList;
            closeButton.Click += delegate { Close(); };
            footer.Controls.Add(closeButton);
            footer.Controls.Add(removeButton);
            footer.Controls.Add(refreshButton);
            Controls.Add(exclusionsList);
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
            try
            {
                IList<DefenderExclusion> exclusions =
                    await Task.Run(() => DefenderExclusionManager.Read());
                exclusionsList.Items.Clear();
                foreach (DefenderExclusion exclusion in exclusions)
                {
                    var item = new ListViewItem(exclusion.Kind);
                    item.SubItems.Add(exclusion.Value);
                    item.Tag = exclusion;
                    exclusionsList.Items.Add(item);
                }
                if (exclusions.Count == 0)
                    MessageBox.Show(this, "Исключения Microsoft Defender не найдены.",
                        "Список исключений", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось получить исключения Microsoft Defender:\r\n\r\n" + ex.Message,
                    "Ошибка PowerShell", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(this, "Сначала отметьте исключения для удаления.",
                    "Ничего не выбрано", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string preview = String.Join("\r\n", selected.Take(8).Select(item =>
                item.Kind + ": " + item.Value).ToArray());
            if (selected.Count > 8)
                preview += "\r\n… и ещё " + (selected.Count - 8) + ".";
            if (MessageBox.Show(this,
                "Удалить выбранные исключения Microsoft Defender?\r\n\r\n" + preview +
                "\r\n\r\nWindows покажет запрос контроля учётных записей.",
                "Подтвердите удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            SetBusy(true);
            try
            {
                await Task.Run(() => DefenderExclusionManager.Remove(selected));
                IList<DefenderExclusion> remaining = await ReloadList();
                int remainingCount = selected.Count(selectedItem => remaining.Any(item =>
                    String.Equals(item.Kind, selectedItem.Kind, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(item.Value, selectedItem.Value, StringComparison.OrdinalIgnoreCase)));
                MessageBox.Show(this, remainingCount == 0
                    ? "Выбранные исключения удалены."
                    : "После команды в списке всё ещё присутствуют " + remainingCount +
                        " выбранных исключений. Проверьте состояние Microsoft Defender.",
                    "Microsoft Defender", MessageBoxButtons.OK,
                    remainingCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, ex.NativeErrorCode == 1223
                    ? "Запрос UAC был отменён. Исключения не изменены."
                    : "Не удалось запустить PowerShell:\r\n\r\n" + ex.Message,
                    "Операция отменена или не выполнена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось удалить исключения Microsoft Defender:\r\n\r\n" + ex.Message,
                    "Ошибка PowerShell", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<IList<DefenderExclusion>> ReloadList()
        {
            IList<DefenderExclusion> exclusions =
                await Task.Run(() => DefenderExclusionManager.Read());
            exclusionsList.Items.Clear();
            foreach (DefenderExclusion exclusion in exclusions)
            {
                var item = new ListViewItem(exclusion.Kind);
                item.SubItems.Add(exclusion.Value);
                item.Tag = exclusion;
                exclusionsList.Items.Add(item);
            }
            return exclusions;
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
            "https://api.github.com/repos/Rom4ik7066/MogAnLocker/releases/latest";

        public static GitHubRelease CheckLatest()
        {
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
                var values = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
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
