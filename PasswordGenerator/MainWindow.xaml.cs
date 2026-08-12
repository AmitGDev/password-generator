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

    // Same sync-guard and hover-grace pattern as length, mirrored for the
    // passphrase word-count control. Layout-only for now - nothing reads
    // WordCountSlider.Value yet.
    private bool _isSyncingWordCount;
    private readonly DispatcherTimer _wordCountPopupCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Same hover-grace pattern as the Length/WordCount popups, for the
    // custom-separator symbol-picker slider. No sync guard needed here:
    // unlike Length/WordCount, the slider only ever writes into the
    // textbox (see CustomSeparatorSlider_ValueChanged), never the reverse.
    private readonly DispatcherTimer _customSeparatorPopupCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Quick-pick pool for the custom-separator slider - a small,
    // easy-to-type set, since (unlike the password tab's Symbols pool)
    // this one gets typed back by a human, often on a different keyboard
    // layout than it was generated on. Purely a UI convenience: the
    // generator itself takes CustomSeparator as whatever string the
    // textbox holds, with no dependency on this pool.
    private const string CustomSeparatorSymbolPool = "!@#$%^&*-_=+";

    // What the Result card showed for a mode the last time that mode
    // generated something. Holding one per mode - rather than just
    // trusting whatever is currently in the shared TextBoxes - is what
    // lets ModeTabControl_SelectionChanged restore Password's own last
    // result after the user has since generated a Passphrase (and vice
    // versa), instead of leaving the other mode's text sitting there.
    private readonly record struct GenerationResult(string Text, string EntropyText, string SearchSpaceText);
    private GenerationResult? _passwordResult;
    private GenerationResult? _passphraseResult;

    private bool IsPassphraseTabActive => ReferenceEquals(ModeTabControl.SelectedItem, PassphraseTabItem);

    public MainWindow() {
      InitializeComponent();
      _lengthPopupCloseTimer.Tick += LengthPopupCloseTimer_Tick;
      _wordCountPopupCloseTimer.Tick += WordCountPopupCloseTimer_Tick;
      _customSeparatorPopupCloseTimer.Tick += CustomSeparatorPopupCloseTimer_Tick;
      _copyConfirmationTimer.Tick += CopyConfirmationTimer_Tick;
      CopyConfirmationPopup.CustomPopupPlacementCallback = GetCopyConfirmationPlacement;
      UpdatePoolSizeDisplay();
      WordlistSizeTextBlock.Text = CryptoPassphraseGenerator.GetWordlistSize().ToString();
      GeneratePassword();
    }

    // ComboBoxes on the Passphrase tab route their SelectionChanged up
    // through the visual tree, so this fires for those too, not just for
    // an actual tab switch - PassphraseOptionChanged already handles the
    // ComboBox case, so here we only react when the TabControl itself is
    // the element whose selection changed.
    private void ModeTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (e.OriginalSource != ModeTabControl) {
        return;
      }

      var result = IsPassphraseTabActive ? _passphraseResult : _passwordResult;
      ResultHeaderText.Text = IsPassphraseTabActive ? "Generated Passphrase" : "Generated Password";
      PasswordTextBox.Text = result?.Text ?? string.Empty;
      EntropyTextBlock.Text = result?.EntropyText ?? string.Empty;
      SearchSpaceTextBlock.Text = result?.SearchSpaceText ?? string.Empty;
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

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyPasswordToClipboard();

    private void CopyPasswordToClipboard() {
      if (string.IsNullOrEmpty(PasswordTextBox.Text)) {
        return;
      }

      Clipboard.SetText(PasswordTextBox.Text);
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

    private void CharacterOptionChanged(object sender, RoutedEventArgs e) => UpdatePoolSizeDisplay();

    private void UpdatePoolSizeDisplay() {
      // Fires as each checkbox's default IsChecked is applied during
      // InitializeComponent, potentially before later checkboxes and the
      // PoolSizeTextBlock field are assigned yet. Bail out until the rest
      // of the window is wired up - the explicit call at the end of the
      // constructor triggers the first real update.
      if (UppercaseCheckBox is null || LowercaseCheckBox is null || DigitsCheckBox is null
          || SymbolsCheckBox is null || ExcludeAmbiguousCheckBox is null || PoolSizeTextBlock is null) {
        return;
      }

      var poolSize = CryptoPasswordGenerator.GetPoolSize(new PasswordOptions {
        IncludeUppercase = UppercaseCheckBox.IsChecked == true,
        IncludeLowercase = LowercaseCheckBox.IsChecked == true,
        IncludeDigits = DigitsCheckBox.IsChecked == true,
        IncludeSymbols = SymbolsCheckBox.IsChecked == true,
        ExcludeAmbiguous = ExcludeAmbiguousCheckBox.IsChecked == true,
      });

      PoolSizeTextBlock.Text = poolSize.ToString();
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

    // Mirrors LengthHoverArea_MouseEnter/Leave and LengthSlider_ValueChanged/
    // LengthTextBox_TextChanged above, for the passphrase Word count card.
    // Layout-only: keeps the floating slider control working the same way
    // as Length, with nothing yet reading WordCountSlider.Value.
    private void WordCountHoverArea_MouseEnter(object sender, MouseEventArgs e) {
      _wordCountPopupCloseTimer.Stop();
      WordCountSliderPopup.IsOpen = true;
    }

    private void WordCountHoverArea_MouseLeave(object sender, MouseEventArgs e) {
      _wordCountPopupCloseTimer.Stop();
      _wordCountPopupCloseTimer.Start();
    }

    private void WordCountPopupCloseTimer_Tick(object? sender, EventArgs e) {
      _wordCountPopupCloseTimer.Stop();
      WordCountSliderPopup.IsOpen = false;
    }

    private void WordCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (_isSyncingWordCount) {
        return;
      }

      _isSyncingWordCount = true;
      WordCountTextBox.Text = ((int)e.NewValue).ToString();
      _isSyncingWordCount = false;
    }

    private void WordCountTextBox_TextChanged(object sender, TextChangedEventArgs e) {
      // Same InitializeComponent ordering caveat as LengthTextBox_TextChanged.
      if (WordCountSlider is null || _isSyncingWordCount) {
        return;
      }

      if (!int.TryParse(WordCountTextBox.Text, out var wordCount)) {
        return;
      }

      var clamped = Math.Clamp(wordCount, (int)WordCountSlider.Minimum, (int)WordCountSlider.Maximum);

      _isSyncingWordCount = true;
      WordCountSlider.Value = clamped;
      _isSyncingWordCount = false;
    }

    private void CustomSeparatorHoverArea_MouseEnter(object sender, MouseEventArgs e) {
      _customSeparatorPopupCloseTimer.Stop();
      CustomSeparatorSliderPopup.IsOpen = true;
    }

    private void CustomSeparatorHoverArea_MouseLeave(object sender, MouseEventArgs e) {
      _customSeparatorPopupCloseTimer.Stop();
      _customSeparatorPopupCloseTimer.Start();
    }

    private void CustomSeparatorPopupCloseTimer_Tick(object? sender, EventArgs e) {
      _customSeparatorPopupCloseTimer.Stop();
      CustomSeparatorSliderPopup.IsOpen = false;
    }

    private void CustomSeparatorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
      CustomSeparatorTextBox.Text = CustomSeparatorSymbolPool[(int)e.NewValue].ToString();
    }

    // Fires for all four radio buttons in the Separator group. Its only
    // job is keeping CustomSeparatorTextBox's enabled state in sync with
    // which one is selected - actual generation reads the selection fresh
    // from the radio buttons in SelectedSeparator, not from anything
    // this handler stores.
    private void SeparatorOptionChanged(object sender, RoutedEventArgs e) {
      // Guard against InitializeComponent applying the default
      // IsChecked="True" on SeparatorCustomRadio before CustomSeparatorTextBox
      // has been assigned yet, same ordering caveat as UpdatePoolSizeDisplay.
      if (CustomSeparatorTextBox is null) {
        return;
      }

      CustomSeparatorTextBox.IsEnabled = SeparatorCustomRadio.IsChecked == true;
    }

    // Unlike CharacterOptionChanged, these have nothing to do: none of the
    // Passphrase tab's controls feed a live display the way the character
    // checkboxes feed PoolSizeTextBlock (WordlistSizeTextBlock reflects the
    // fixed wordlist, not any option). Actual generation only happens on
    // Generate, mirroring the Password tab - both overloads stay as
    // no-op handlers purely so the XAML has something to bind to.
    private void PassphraseOptionChanged(object sender, RoutedEventArgs e) {
    }

    private void PassphraseOptionChanged(object sender, SelectionChangedEventArgs e) {
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) {
      if (IsPassphraseTabActive) {
        GeneratePassphrase();
      } else {
        GeneratePassword();
      }
    }

    private PassphraseSeparator SelectedSeparator() =>
        SeparatorCustomRadio.IsChecked == true ? PassphraseSeparator.Custom : PassphraseSeparator.None;

    private PassphraseCapitalization SelectedCapitalization() {
      if (CapitalizationTitleCaseRadio.IsChecked == true) {
        return PassphraseCapitalization.TitleCase;
      }

      if (CapitalizationRandomPerWordRadio.IsChecked == true) {
        return PassphraseCapitalization.RandomPerWord;
      }

      return PassphraseCapitalization.None;
    }

    private void GeneratePassphrase() {
      var options = new PassphraseOptions {
        WordCount = (int)WordCountSlider.Value,
        Separator = SelectedSeparator(),
        CustomSeparator = CustomSeparatorTextBox.Text,
        Capitalization = SelectedCapitalization(),
        AppendNumber = AppendNumberCheckBox.IsChecked == true,
      };

      try {
        PasswordTextBox.Text = CryptoPassphraseGenerator.Generate(options);

        if (AutoCopyCheckBox.IsChecked == true) {
          CopyPasswordToClipboard();
        }

        // No "X^Y" prefix here the way GeneratePassword shows poolSize^Length:
        // RandomPerWord and AppendNumber add bits that don't share the
        // wordlist's base, so unlike the password case there isn't a single
        // base^exponent that already equals the full entropy -
        // FormatSearchSpace's derived-from-bits combinations figure is the
        // only one that stays accurate regardless of which options are on.
        var bits = CryptoPassphraseGenerator.GetMaximumEntropyBits(options);
        EntropyTextBlock.Text = $"{bits:F1} bits";
        SearchSpaceTextBlock.Text = FormatSearchSpace(bits);
        _passphraseResult = new GenerationResult(PasswordTextBox.Text, EntropyTextBlock.Text, SearchSpaceTextBlock.Text);
      } catch (PassphraseOptionsException ex) {
        PasswordTextBox.Text = string.Empty;
        EntropyTextBlock.Text = string.Empty;
        SearchSpaceTextBlock.Text = string.Empty;
        _passphraseResult = null;
        MessageBox.Show(ex.Message, "Cannot generate passphrase", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void GeneratePassword() {
      var options = new PasswordOptions {
        Length = (int)LengthSlider.Value,
        IncludeUppercase = UppercaseCheckBox.IsChecked == true,
        IncludeLowercase = LowercaseCheckBox.IsChecked == true,
        IncludeDigits = DigitsCheckBox.IsChecked == true,
        IncludeSymbols = SymbolsCheckBox.IsChecked == true,
        ExcludeAmbiguous = ExcludeAmbiguousCheckBox.IsChecked == true,
      };

      try {
        PasswordTextBox.Text = CryptoPasswordGenerator.Generate(options);

        if (AutoCopyCheckBox.IsChecked == true) {
          CopyPasswordToClipboard();
        }

        // Computed from the same options used for Generate() above, so
        // these always describe the password actually shown - not a
        // live preview that could drift from it if options change
        // afterward without a re-generate.
        var bits = CryptoPasswordGenerator.GetMaximumEntropyBits(options);
        var poolSize = CryptoPasswordGenerator.GetPoolSize(options);
        EntropyTextBlock.Text = $"{bits:F1} bits";
        SearchSpaceTextBlock.Text = $"{poolSize}^{options.Length} {FormatSearchSpace(bits)}";
        _passwordResult = new GenerationResult(PasswordTextBox.Text, EntropyTextBlock.Text, SearchSpaceTextBlock.Text);
      } catch (PasswordOptionsException ex) {
        PasswordTextBox.Text = string.Empty;
        EntropyTextBlock.Text = string.Empty;
        SearchSpaceTextBlock.Text = string.Empty;
        _passwordResult = null;
        MessageBox.Show(ex.Message, "Cannot generate password", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    // Formats the search space (2^entropyBits) as "≈ mantissa × 10^exponent
    // combinations". The raw number is too large to be meaningful in full
    // for any realistic password length, so this expresses it at a scale a
    // person can actually take in. Deliberately not a time-to-crack
    // estimate - that would require assuming an attacker's guesses-per-
    // second, which this app has no basis to claim. A display-formatting
    // concern, so it lives here rather than in CryptoPasswordGenerator,
    // which stays limited to generation math with no UI dependency.
    private static string FormatSearchSpace(double entropyBits) {
      var log10 = entropyBits * Math.Log10(2);
      var exponent = (int)Math.Floor(log10);
      var mantissa = Math.Pow(10, log10 - exponent);

      return $"≈ {mantissa:F1} × 10^{exponent} combinations";
    }
  }
}
