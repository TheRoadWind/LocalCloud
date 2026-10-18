using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace LocalCloud
{
    internal static class Program
    {
        // 单实例互斥锁名称
        private const string MutexName = "LocalCloud_SingleInstance_Mutex";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            // 全局异常捕获：任何未处理异常都不让进程直接崩溃（服务需持续运行）
            Application.ThreadException += (sender, e) =>
            {
                try
                {
                    File.AppendAllText(Path.Combine(Application.StartupPath, "crash_log.txt"),
                        $"[{DateTime.Now}] UI线程未处理异常: {e.Exception}{Environment.NewLine}");
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    File.AppendAllText(Path.Combine(Application.StartupPath, "crash_log.txt"),
                        $"[{DateTime.Now}] 进程级未处理异常: {e.ExceptionObject}{Environment.NewLine}");
                }
                catch { }
                // 不强制退出，保持服务运行
            };

            // 非管理员时自动提权重启（调试器附加时跳过，便于调试，避免空白窗体）
            if (!IsRunningAsAdministrator() && !Debugger.IsAttached)
            {
                RestartAsAdministrator();
                return;
            }

            // 单实例：若已存在实例，则将其窗口前置并返回
            using (Mutex mutex = new Mutex(true, MutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    BringExistingWindowToFront();
                    return;
                }

                try
                {
                    Application.Run(new MainForm());
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>检查是否以管理员权限运行</summary>
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>以管理员身份重新启动当前程序（当前实例直接退出，避免空白窗体）</summary>
        private static void RestartAsAdministrator()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                string exePath = Application.ExecutablePath;
                ProcessStartInfo psi = new ProcessStartInfo()
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id " + currentPid.ToString() +
                                 " -ErrorAction SilentlyContinue; Start-Process -FilePath '" +
                                 exePath.Replace("'", "''") + "' -Verb RunAs\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception)
            {
                MessageBox.Show("自动提权失败，请右键点击程序，选择'以管理员身份运行'。" + Environment.NewLine +
                               "本程序需要以管理员权限运行才能正常工作。",
                               "权限不足",
                               MessageBoxButtons.OK,
                               MessageBoxIcon.Warning);
            }
        }

        /// <summary>将已存在的实例窗口前置并恢复显示</summary>
        private static void BringExistingWindowToFront()
        {
            IntPtr hwnd = FindWindow(null, "私有云盘");
            if (hwnd != IntPtr.Zero)
            {
                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, 9); // SW_RESTORE
                }
                SetForegroundWindow(hwnd);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
