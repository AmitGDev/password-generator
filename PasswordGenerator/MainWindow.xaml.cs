using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace PasswordGenerator {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
    }

    private const int WM_SETTINGCHANGE = 0x001A;

    protected override void OnSourceInitialized(EventArgs e) {
      base.OnSourceInitialized(e);

      if (PresentationSource.FromVisual(this) is HwndSource source) {
        source.AddHook(WndProc);
        ApplyTitleBarTheme(!App.IsWindowsInLightTheme());
      }
    }

    // The title bar is drawn by the OS (DWM), not by WPF content, so none
    // of the DynamicResource-based theming above has any effect on it - it
    // needs this separate, explicit Win32 call.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void ApplyTitleBarTheme(bool isDark) {
      if (PresentationSource.FromVisual(this) is not HwndSource source) {
        return;
      }

      var useDark = isDark ? 1 : 0;
      DwmSetWindowAttribute(source.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    // Windows broadcasts WM_SETTINGCHANGE with lParam "ImmersiveColorSet"
    // whenever the system light/dark app-mode setting changes, so the app
    // can react live instead of only picking up the setting at next launch.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
      if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero
          && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") {
        App.ApplySystemTheme();
        ApplyTitleBarTheme(!App.IsWindowsInLightTheme());
      }

      return IntPtr.Zero;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) {
    }

    private void CharacterOptionChanged(object sender, RoutedEventArgs e) {
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) {
    }
  }
}