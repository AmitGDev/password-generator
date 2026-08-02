using System.Windows;
using Microsoft.Win32;

namespace PasswordGenerator;

public partial class App : Application {
  // Applying the theme before base.OnStartup() runs means the theme
  // dictionary is already merged - and every color in the style files is
  // a DynamicResource - by the time StartupUri creates and shows
  // MainWindow, so the window never flashes the wrong theme on launch.
  protected override void OnStartup(StartupEventArgs e) {
    ApplySystemTheme();
    base.OnStartup(e);
  }

  /// <summary>
  /// Reads the current Windows light/dark app-mode setting and merges the
  /// matching theme dictionary, replacing any previously merged one.
  /// Because every color in the style dictionaries is a DynamicResource,
  /// already-open windows re-theme immediately - no restart or explicit
  /// UI refresh needed. Called at startup and again whenever Windows
  /// broadcasts a theme change (see MainWindow's WM_SETTINGCHANGE hook).
  /// </summary>
  public static void ApplySystemTheme() {
    var themeUri = new Uri(
        IsWindowsInLightTheme() ? "Themes/Themes.Light.xaml" : "Themes/Themes.Dark.xaml",
        UriKind.Relative);

    var dictionaries = Current.Resources.MergedDictionaries;
    for (var i = dictionaries.Count - 1; i >= 0; i--) {
      var source = dictionaries[i].Source?.OriginalString;
      if (source is not null && (source.EndsWith("Themes.Light.xaml") || source.EndsWith("Themes.Dark.xaml"))) {
        dictionaries.RemoveAt(i);
      }
    }

    dictionaries.Add(new ResourceDictionary { Source = themeUri });
  }

  // Windows stores light/dark app mode as a DWORD under this key: 1 means
  // light, 0 means dark. Missing entirely (older Windows without dark
  // mode support) defaults to light, matching Windows' own historical
  // default before dark mode existed.
  internal static bool IsWindowsInLightTheme() {
    const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    using var key = Registry.CurrentUser.OpenSubKey(keyPath);
    return key?.GetValue("AppsUseLightTheme") is not int intValue || intValue != 0;
  }
}