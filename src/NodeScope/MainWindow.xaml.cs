using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace NodeScope;

public partial class MainWindow : Window
{
    private readonly BridgeClient _bridge = new();
    private readonly ObservableCollection<PropertyRow> _properties = new();
    private string? _projectFile;
    private string? _godotExe;
    private int _selectedId;
    private int _seenRuntimeLogs;
    private string _selectedName = "";
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();
        PropertiesGrid.ItemsSource = _properties;
        _poll.Tick += async (_, _) => { if (_bridge.Connected) await RefreshTreeAsync(); };
        _poll.Start();
        Log("NodeScope ready.");
    }

    private void Log(string message) { LogList.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}"); }
    private void SetStatus(string text) { StatusText.Text = text; FooterText.Text = text; }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Godot project (project.godot)|project.godot", Title = "Select project.godot" };
        if (d.ShowDialog() == true) { _projectFile = d.FileName; SetStatus("Project: " + _projectFile); }
    }
    private void GodotExe_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Godot executable (Godot*.exe)|Godot*.exe|Executable (*.exe)|*.exe" };
        if (d.ShowDialog() == true) { _godotExe = d.FileName; Log("Godot executable: " + _godotExe); }
    }
    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_projectFile is null) throw new InvalidOperationException("Choose a project.godot first.");
            var source = FindAddonDirectory();
            var target = Path.Combine(Path.GetDirectoryName(_projectFile)!, "addons", "nodescope");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(source, target);
            var config = File.ReadAllText(_projectFile);
            if (!config.Contains("[editor_plugins]")) config += "\n[editor_plugins]\nenabled=PackedStringArray(\"res://addons/nodescope/plugin.cfg\")\n";
            else if (!config.Contains("res://addons/nodescope/plugin.cfg"))
            {
                var marker = "enabled=PackedStringArray(";
                int at = config.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0) { int end = config.IndexOf(')', at); config = config.Insert(end, (end > at + marker.Length ? ", " : "") + "\"res://addons/nodescope/plugin.cfg\""); }
                else config += "\nenabled=PackedStringArray(\"res://addons/nodescope/plugin.cfg\")\n";
            }
            File.WriteAllText(_projectFile, config);
            Log("Addon installed and enabled in project.godot. Restart the editor/game if already running.");
            SetStatus("Addon installed · " + Path.GetDirectoryName(_projectFile));
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private static string FindAddonDirectory()
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "addons", "nodescope"), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "addons", "nodescope")) };
        return candidates.FirstOrDefault(Directory.Exists) ?? throw new DirectoryNotFoundException("Bundled addons/nodescope folder was not found. Download the repository or run from the project directory.");
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_projectFile is null) throw new InvalidOperationException("Choose a project.godot first.");
            if (_godotExe is null) throw new InvalidOperationException("Choose your Godot 4 executable first.");
            Process.Start(new ProcessStartInfo(_godotExe, $"--path \"{Path.GetDirectoryName(_projectFile)}\"") { UseShellExecute = true });
            Log("Launched Godot project. Ensure the NodeScope addon is enabled in the editor.");
            _ = ConnectWithRetryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task ConnectWithRetryAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            try { await Task.Delay(500); await _bridge.ConnectAsync(); await Dispatcher.InvokeAsync(async () => { SetStatus("Connected · 127.0.0.1:47831"); Log("Runtime bridge connected."); await RefreshTreeAsync(); }); return; }
            catch { }
        }
        await Dispatcher.InvokeAsync(() => Log("No runtime bridge detected. Confirm addon enabled and game running, then press Attach."));
    }
    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        try { await _bridge.ConnectAsync(); SetStatus("Connected · 127.0.0.1:47831"); Log("Attached to running game."); await RefreshTreeAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task RefreshTreeAsync()
    {
        try
        {
            var result = await _bridge.SendAsync("tree", new { });
            SceneTree.Items.Clear();
            if (result.TryGetProperty("nodes", out var nodes))
                foreach (var node in nodes.EnumerateArray()) AddTreeItem(null, node);
            var logs = await _bridge.SendAsync("logs", new { });
            if (logs.TryGetProperty("entries", out var entries))
            {
                var all = entries.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                foreach (var entry in all.Skip(_seenRuntimeLogs)) Log("GAME " + entry);
                _seenRuntimeLogs = all.Length;
            }
        }
        catch (Exception ex) { Log("Refresh: " + ex.Message); }
    }
    private void AddTreeItem(TreeViewItem? parent, JsonElement node)
    {
        var item = new TreeViewItem { Header = $"{node.GetProperty("name").GetString()}  <{node.GetProperty("type").GetString()}>", Tag = node.GetProperty("id").GetInt32() };
        if (parent is null) SceneTree.Items.Add(item); else parent.Items.Add(item);
        foreach (var child in node.GetProperty("children").EnumerateArray()) AddTreeItem(item, child);
        item.IsExpanded = true;
    }
    private async void SceneTree_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (SceneTree.SelectedItem is not TreeViewItem item || item.Tag is not int id) return;
        _selectedId = id; _selectedName = item.Header.ToString() ?? "";
        try
        {
            var result = await _bridge.SendAsync("inspect", new { id });
            SelectedTitle.Text = result.GetProperty("name").GetString() + " · " + result.GetProperty("type").GetString();
            _properties.Clear();
            foreach (var p in result.GetProperty("properties").EnumerateArray())
                _properties.Add(new PropertyRow { Name = p.GetProperty("name").GetString()!, Value = p.GetProperty("value").GetString() ?? "" });
        }
        catch (Exception ex) { Log(ex.Message); }
    }
    private async void ApplyProperty_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId == 0 || PropertiesGrid.SelectedItem is not PropertyRow row) { Log("Select a property row first."); return; }
        try
        {
            object value = row.Value;
            if (row.Name == "visible") value = bool.Parse(row.Value);
            else if (row.Name is "radius" or "height") value = double.Parse(row.Value, System.Globalization.CultureInfo.InvariantCulture);
            else if (row.Name is "position" or "rotation" or "scale" or "size")
            {
                var s = row.Value.Replace("(", "").Replace(")", "").Split(',');
                if (s.Length != 3) throw new FormatException("Vector must contain three comma-separated numbers.");
                value = s.Select(x => double.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            }
            await _bridge.SendAsync("set_property", new { id = _selectedId, property = row.Name, value });
            Log("Updated " + row.Name);
            await RefreshTreeAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void AddBox_Click(object s, RoutedEventArgs e) => await CreateAsync("box");
    private async void AddSphere_Click(object s, RoutedEventArgs e) => await CreateAsync("sphere");
    private async void AddCylinder_Click(object s, RoutedEventArgs e) => await CreateAsync("cylinder");
    private async Task CreateAsync(string type)
    {
        try { await _bridge.SendAsync("create", new { parent = _selectedId, type }); Log("Created " + type); await RefreshTreeAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Duplicate_Click(object s, RoutedEventArgs e) { await RunNodeAction("duplicate", new { id = _selectedId }); }
    private async void Delete_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("Delete selected runtime node?", "NodeScope", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await RunNodeAction("delete", new { id = _selectedId });
    }
    private async void Rename_Click(object s, RoutedEventArgs e)
    {
        var dialog = new RenameDialog(_selectedName);
        if (dialog.ShowDialog() == true) await RunNodeAction("rename", new { id = _selectedId, name = dialog.NewName });
    }
    private async Task RunNodeAction(string action, object args)
    {
        try { await _bridge.SendAsync(action, args); Log(action + " complete."); await RefreshTreeAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Camera_Click(object s, RoutedEventArgs e) { try { var r = await _bridge.SendAsync("camera", new { command = "toggle" }); Log("Free camera: " + r.GetProperty("enabled").GetBoolean()); } catch (Exception ex) { ShowError(ex); } }
    private async void CameraSpeed_Click(object s, RoutedEventArgs e) { try { await _bridge.SendAsync("camera", new { command = "speed", value = double.Parse(CameraSpeed.Text, System.Globalization.CultureInfo.InvariantCulture) }); Log("Camera speed updated."); } catch (Exception ex) { ShowError(ex); } }
    private void SearchBox_Changed(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        foreach (TreeViewItem item in SceneTree.Items) FilterItem(item, query);
    }
    private bool FilterItem(TreeViewItem item, string query)
    {
        bool childMatch = false;
        foreach (TreeViewItem child in item.Items) childMatch |= FilterItem(child, query);
        bool own = item.Header?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
        item.Visibility = string.IsNullOrEmpty(query) || own || childMatch ? Visibility.Visible : Visibility.Collapsed;
        if (childMatch) item.IsExpanded = true;
        return own || childMatch;
    }
    private void Refresh_Click(object s, RoutedEventArgs e) => _ = RefreshTreeAsync();
    private void ClearLog_Click(object s, RoutedEventArgs e) => LogList.Items.Clear();
    private void ShowError(Exception ex) { Log("ERROR: " + ex.Message); MessageBox.Show(ex.Message, "NodeScope", MessageBoxButton.OK, MessageBoxImage.Error); }
    protected override async void OnClosed(EventArgs e) { _poll.Stop(); await _bridge.DisposeAsync(); base.OnClosed(e); }
}
public sealed class PropertyRow { public string Name { get; set; } = ""; public string Value { get; set; } = ""; }
