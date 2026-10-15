using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MogLocker.Launcher
{
    internal sealed class PackageForm : Form
    {
        private readonly string workingDirectory;
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;
        private Process bootstrapProcess;

        internal PackageForm()
        {
            Text = "Моглокер";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(430, 148);
            BackColor = Color.FromArgb(18, 26, 45);
            ForeColor = Color.FromArgb(226, 232, 244);
            Font = new Font("Segoe UI", 9F);

            workingDirectory = Path.Combine(Path.GetTempPath(), "MogLocker-" + Guid.NewGuid().ToString("N"));
            statusLabel = new Label
            {
                AutoSize = false,
                Text = "Подготовка приложения и проверка компонентов Windows…",
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(22, 24),
                Size = new Size(385, 44),
                ForeColor = ForeColor
            };
            progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28,
                Location = new Point(22, 88),
                Size = new Size(385, 18)
            };
            Controls.Add(statusLabel);
            Controls.Add(progressBar);
            Shown += StartBootstrap;
        }

        private void StartBootstrap(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(workingDirectory);
                ExtractResource("MogLockerApp.exe", Path.Combine(workingDirectory, "MogLockerApp.exe"));
                ExtractResource("bootstrap.ps1", Path.Combine(workingDirectory, "bootstrap.ps1"));
                ExtractResource("browser-extension.zip", Path.Combine(workingDirectory, "browser-extension.zip"));
                ExtractResource("soundpad.zip", Path.Combine(workingDirectory, "soundpad.zip"));

                string powershell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");
                string script = Path.Combine(workingDirectory, "bootstrap.ps1");
                bootstrapProcess = new Process();
                bootstrapProcess.StartInfo = new ProcessStartInfo
                {
                    FileName = powershell,
                    Arguments = "-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + script + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };
                bootstrapProcess.EnableRaisingEvents = true;
                bootstrapProcess.Exited += BootstrapExited;
                if (!bootstrapProcess.Start())
                    throw new InvalidOperationException("Не удалось запустить встроенный установщик приложения.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось подготовить Моглокер:\r\n\r\n" + ex.Message,
                    "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private static void ExtractResource(string resourceName, string destination)
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                    throw new FileNotFoundException("Не найден встроенный компонент: " + resourceName);
                using (FileStream output = File.Create(destination))
                    resource.CopyTo(output);
            }
        }

        private void BootstrapExited(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            BeginInvoke(new Action(delegate
            {
                int exitCode = bootstrapProcess.ExitCode;
                if (exitCode != 0)
                    MessageBox.Show(this, "Установщик завершился с ошибкой (код " + exitCode + ").",
                        "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (bootstrapProcess != null)
                bootstrapProcess.Dispose();
            Exception cleanupError = null;
            for (int attempt = 0; attempt < 5 && Directory.Exists(workingDirectory); attempt++)
            {
                try
                {
                    Directory.Delete(workingDirectory, true);
                    cleanupError = null;
                }
                catch (IOException ex)
                {
                    cleanupError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    cleanupError = ex;
                }
                if (Directory.Exists(workingDirectory))
                    Thread.Sleep(200);
            }
            if (cleanupError != null && Directory.Exists(workingDirectory))
                MessageBox.Show("Не удалось удалить временные файлы запуска: " + cleanupError.Message,
                    "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            base.OnFormClosed(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PackageForm());
        }
    }
}
