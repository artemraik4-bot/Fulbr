using Microsoft.Win32;
using System.Windows;

namespace Fulbr.App.Services;

public static class ThemeManager
{
    public static string DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 0 ? "Dark" : "Light";
        }
        catch
        {
            // Fall through to dark.
        }
        return "Dark";
    }

    public static void Apply(string theme)
    {
        var actual = theme.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? DetectSystemTheme()
            : theme;

        var source = actual.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/Light.xaml", UriKind.Relative)
            : new Uri("Themes/Dark.xaml", UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = source };

        if (dictionaries.Count == 0)
            dictionaries.Add(replacement);
        else
            dictionaries[0] = replacement;
    }
}
