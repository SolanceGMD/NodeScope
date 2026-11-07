using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NodeScope;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

internal sealed class LauncherForm : Form
{
    private readonly ListView games = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 48, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10), Text = "Looking for Wobble Works..." };
    private readonly string dllPath = Path.Combine(AppContext.BaseDirectory, "NodeScopeHook.dll");
    private string installFolder = "";

    public LauncherForm()
    {
        Text = "NodeScope · Wobble Works";
        Width = 760; Height = 500; MinimumSize = new Size(620, 380);
        StartPosition = FormStartPosition.CenterScreen;
        games.Columns.Add("Game window", 390);
        games.Columns.Add("Executable", 230);
        games.Columns.Add("PID", 75);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10), WrapContents = false };
        var refresh = new Button { Text = "Find running game", Width = 140, Height = 30 };
        var locate = new Button { Text = "Locate install folder", Width = 150, Height = 30 };
        var inject = new Button { Text = "Inject NodeScope", Width = 150, Height = 30 };
        refresh.Click += (_, _) => RefreshGames();
        locate.Click += (_, _) => LocateInstall();
        inject.Click += (_, _) => InjectSelected();
        top.Controls.Add(refresh); top.Controls.Add(locate); top.Controls.Add(inject);

        var note = new Label
        {
            Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 4, 10, 4),
            Text = "Wobble Works target build. Start the game through itch first, then select it here. The current native module is an injection/overlay prototype; live scene editing is not yet implemented."
        };
        Controls.Add(games); Controls.Add(top); Controls.Add(note); Controls.Add(status);

        installFolder = FindDefaultInstall();
        RefreshGames();
    }

    private static string FindDefaultInstall()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidate = Path.Combine(appData, "itch", "apps", "wobble-works", "Wobble-Works-0.1v-itch");
        return Directory.Exists(candidate) ? candidate : "";
    }

    private void LocateInstall()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Wobble Works installation folder", UseDescriptionForTitle = true };
        if (Directory.Exists(installFolder)) dialog.SelectedPath = installFolder;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            installFolder = dialog.SelectedPath;
            RefreshGames();
        }
    }

    private bool IsWobbleWorks(Process process)
    {
        try
        {
            var exe = process.MainModule?.FileName ?? "";
            var title = process.MainWindowTitle;
            if (!string.IsNullOrWhiteSpace(installFolder) &&
                exe.StartsWith(installFolder, StringComparison.OrdinalIgnoreCase)) return true;
            return exe.Contains("Wobble-Works", StringComparison.OrdinalIgnoreCase) ||
                   exe.Contains("Wobble Works", StringComparison.OrdinalIgnoreCase) ||
                   title.Contains("Wobble Works", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void RefreshGames()
    {
        games.Items.Clear();
        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName))
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero || !IsWobbleWorks(process))
                    continue;
                var item = new ListViewItem(string.IsNullOrWhiteSpace(process.MainWindowTitle) ? "Wobble Works" : process.MainWindowTitle) { Tag = process.Id };
                item.SubItems.Add(process.MainModule?.ModuleName ?? process.ProcessName);
                item.SubItems.Add(process.Id.ToString());
                games.Items.Add(item);
            }
            catch { }
            finally { process.Dispose(); }
        }
        status.Text = games.Items.Count > 0
            ? $"Found {games.Items.Count} Wobble Works process(es). Select the game to inject."
            : $"Wobble Works not found. Start it through itch. Install folder: {(string.IsNullOrEmpty(installFolder) ? "not detected — use Locate install folder" : installFolder)}";
    }

    private void InjectSelected()
    {
        if (games.SelectedItems.Count == 0) { status.Text = "Select the running Wobble Works window first."; return; }
        if (!File.Exists(dllPath)) { status.Text = "NodeScopeHook.dll is missing. Keep it beside NodeScope.exe."; return; }
        var pid = (int)games.SelectedItems[0].Tag!;
        try
        {
            var ok = NativeInjector.Inject(pid, dllPath);
            status.Text = ok
                ? $"Injection request completed for Wobble Works (PID {pid}). Press = in the game to toggle the overlay."
                : "Injection failed. Check permissions and ensure both processes are x64.";
        }
        catch (Exception ex) { status.Text = "Injection failed: " + ex.Message; }
    }
}

internal static class NativeInjector
{
    const uint CREATE_THREAD = 0x0002, QUERY = 0x0400, VM_OPERATION = 0x0008, VM_WRITE = 0x0020;
    const uint COMMIT = 0x1000, RESERVE = 0x2000, PAGE_RW = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attrs, UIntPtr stack, IntPtr start, IntPtr param, uint flags, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);

    public static bool Inject(int pid, string path)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(path) + "\0");
        var process = OpenProcess(CREATE_THREAD | QUERY | VM_OPERATION | VM_WRITE, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException($"OpenProcess failed ({Marshal.GetLastWin32Error()}).");
        IntPtr remote = IntPtr.Zero, thread = IntPtr.Zero;
        try
        {
            remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)bytes.Length, COMMIT | RESERVE, PAGE_RW);
            if (remote == IntPtr.Zero) throw new InvalidOperationException($"VirtualAllocEx failed ({Marshal.GetLastWin32Error()}).");
            if (!WriteProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out var written) || written.ToUInt64() != (ulong)bytes.Length)
                throw new InvalidOperationException($"WriteProcessMemory failed ({Marshal.GetLastWin32Error()}).");
            var load = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            if (load == IntPtr.Zero) throw new InvalidOperationException("Could not locate LoadLibraryW.");
            thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, load, remote, 0, out _);
            if (thread == IntPtr.Zero) throw new InvalidOperationException($"CreateRemoteThread failed ({Marshal.GetLastWin32Error()}).");
            if (WaitForSingleObject(thread, 15000) != 0) throw new TimeoutException("Timed out waiting for the target process.");
            return GetExitCodeThread(thread, out var code) && code != 0;
        }
        finally
        {
            if (thread != IntPtr.Zero) CloseHandle(thread);
            if (remote != IntPtr.Zero) VirtualFreeEx(process, remote, UIntPtr.Zero, 0x8000);
            CloseHandle(process);
        }
    }
}