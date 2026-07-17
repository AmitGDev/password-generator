using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PasswordGenerator;

public partial class MainWindow : Window {
  // Guards against the slider and textbox re-entrantly overwriting each
  // other when one updates the other in response to a user edit.
  private bool _isSyncingLength;

  // Keeps the length popup slider open for a short grace period after the
  // mouse leaves the textbox or the popup, so moving between the two
  // (or briefly overshooting either) doesn't snap it shut.
  private readonly DispatcherTimer _lengthPopupCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

  public MainWindow() {
    InitializeComponent();
    _lengthPopupCloseTimer.Tick += LengthPopupCloseTimer_Tick;
    UpdatePoolSizeDisplay();
    GeneratePassword();
  }

  private void LengthPopupCloseTimer_Tick(object? sender, EventArgs e) {
    _lengthPopupCloseTimer.Stop();
    LengthSliderPopup.IsOpen = false;
  }

  private void LengthHoverArea_MouseEnter(object sender, MouseEventArgs e) {
    _lengthPopupCloseTimer.Stop();
    LengthSliderPopup.IsOpen = true;
  }

  private void LengthHoverArea_MouseLeave(object sender, MouseEventArgs e) {
    _lengthPopupCloseTimer.Stop();
    _lengthPopupCloseTimer.Start();
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

  private void GenerateButton_Click(object sender, RoutedEventArgs e) => GeneratePassword();

  private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyPasswordToClipboard();

  private void CopyPasswordToClipboard() {
    if (string.IsNullOrEmpty(PasswordTextBox.Text)) {
      return;
    }

    Clipboard.SetText(PasswordTextBox.Text);
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
    } catch (PasswordOptionsException ex) {
      PasswordTextBox.Text = string.Empty;
      EntropyTextBlock.Text = string.Empty;
      SearchSpaceTextBlock.Text = string.Empty;
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
