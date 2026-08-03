using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PasswordGenerator {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    // Guards against the slider and textbox re-entrantly overwriting each
    // other when one updates the other in response to a user edit.
    private bool _isSyncingLength;

    // Keeps the length popup slider open for a short grace period after the
    // mouse leaves the textbox or the popup, so moving between the two
    // (or briefly overshooting either) doesn't snap it shut.
    private readonly DispatcherTimer _lengthPopupCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Auto-dismisses the "Copied" confirmation toast 2 seconds after a copy.
    private readonly DispatcherTimer _copyConfirmationTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow() {
      InitializeComponent();
      _lengthPopupCloseTimer.Tick += LengthPopupCloseTimer_Tick;
      _copyConfirmationTimer.Tick += CopyConfirmationTimer_Tick;
      CopyConfirmationPopup.CustomPopupPlacementCallback = GetCopyConfirmationPlacement;
    }

    // Vertically centers the "Copied" toast against CopyButton's actual
    // height and sits it just to the left, using the popup's real measured
    // size rather than a guessed fixed offset - stays correct regardless of
    // font metrics or future content changes, unlike a hardcoded VerticalOffset.
    private static CustomPopupPlacement[] GetCopyConfirmationPlacement(Size popupSize, Size targetSize, Point offset) {
      const double gap = 4;
      var x = -popupSize.Width - gap;
      var y = (targetSize.Height - popupSize.Height) / 2;
      return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None) };
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
      // TODO: write PasswordTextBox.Text to the clipboard here once the
      // generator model exists.
      ShowCopyConfirmation();
    }

    private void ShowCopyConfirmation() {
      // Stop-then-start rather than just Start, so a repeated copy while
      // the toast is already showing resets it to a fresh 2 seconds
      // instead of letting an earlier timer close it early.
      _copyConfirmationTimer.Stop();
      CopyConfirmationPopup.IsOpen = true;
      _copyConfirmationTimer.Start();
    }

    private void CopyConfirmationTimer_Tick(object? sender, EventArgs e) {
      _copyConfirmationTimer.Stop();
      CopyConfirmationPopup.IsOpen = false;
    }

    private void CharacterOptionChanged(object sender, RoutedEventArgs e) {
    }

    private void LengthHoverArea_MouseEnter(object sender, MouseEventArgs e) {
      _lengthPopupCloseTimer.Stop();
      LengthSliderPopup.IsOpen = true;
    }

    private void LengthHoverArea_MouseLeave(object sender, MouseEventArgs e) {
      _lengthPopupCloseTimer.Stop();
      _lengthPopupCloseTimer.Start();
    }

    private void LengthPopupCloseTimer_Tick(object? sender, EventArgs e) {
      _lengthPopupCloseTimer.Stop();
      LengthSliderPopup.IsOpen = false;
    }

    private void LengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (_isSyncingLength) {
        return;
      }

      _isSyncingLength = true;
      LengthTextBox.Text = ((int)e.NewValue).ToString();
      _isSyncingLength = false;
    }

    private void LengthTextBox_TextChanged(object sender, TextChangedEventArgs e) {
      // LengthTextBox is parsed before LengthSlider in the XAML, so setting
      // its initial Text during InitializeComponent fires this handler
      // before LengthSlider's field has been assigned. Bail out until the
      // rest of the window is wired up.
      if (LengthSlider is null || _isSyncingLength) {
        return;
      }

      if (!int.TryParse(LengthTextBox.Text, out var length)) {
        return;
      }

      // Clamp silently rather than rejecting keystrokes, so typing a
      // multi-digit number doesn't fight the user character by character.
      var clamped = Math.Clamp(length, (int)LengthSlider.Minimum, (int)LengthSlider.Maximum);

      _isSyncingLength = true;
      LengthSlider.Value = clamped;
      _isSyncingLength = false;
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) {
      // TODO: call the generator model and write the result into
      // PasswordTextBox here once it exists.
      if (AutoCopyCheckBox.IsChecked == true) {
        ShowCopyConfirmation();
      }
    }
  }
}