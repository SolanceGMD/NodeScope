using System.Windows;
using System.Windows.Controls;
namespace NodeScope;
public sealed class RenameDialog : Window
{
    private readonly TextBox _name;
    public string NewName => _name.Text.Trim();
    public RenameDialog(string current)
    {
        Title = "Rename node"; Width = 360; Height = 145; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.DarkSlateGray; Foreground = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "New node name" });
        _name = new TextBox { Text = current, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(_name);
        var ok = new Button { Content = "Rename", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(NewName)) DialogResult = true; };
        panel.Children.Add(ok); Content = panel;
    }
}
