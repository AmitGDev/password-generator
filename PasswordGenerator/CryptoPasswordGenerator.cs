using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PasswordGenerator;

/// <summary>
/// Options describing which character classes to draw from and how the
/// result should be composed. Kept separate from the UI so the generator
/// can be validated without any WPF dependency.
/// </summary>
public sealed record PasswordOptions {
  public int Length { get; init; } = 16;
  public bool IncludeUppercase { get; init; } = true;
  public bool IncludeLowercase { get; init; } = true;
  public bool IncludeDigits { get; init; } = true;
  public bool IncludeSymbols { get; init; } = true;
  public bool ExcludeAmbiguous { get; init; }
}

/// <summary>
/// Thrown when the requested options can't produce a valid password
/// (e.g. no character classes selected, or length too short to fit one
/// character of each selected class).
/// </summary>
public sealed class PasswordOptionsException(string message) : Exception(message);

public static class CryptoPasswordGenerator {
  private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
  private const string Digits = "0123456789";
  private const string Symbols = "!@#$%^&*()-_=+[]{};:,.<>/?~`|\\";

  // Uppercase, Lowercase, Digits, Symbols - keeps the List<string> capacity
  // in BuildSelectedClasses tied to an explained value instead of a bare 4.
  private const int CharacterClassCount = 4;

  // Characters that are easy to mis-key or mis-read (1/l/I, 0/O). A
  // FrozenSet communicates intent - a fixed, immutable lookup set built
  // once - even though at this size the performance benefit over
  // searching a string is negligible.
  private static readonly FrozenSet<char> Ambiguous = FrozenSet.ToFrozenSet(['I', 'l', '1', 'O', '0']);

  /// <summary>
  /// Generates a password using a CSPRNG for both character selection and
  /// shuffling. Every index drawn is unbiased (RandomNumberGenerator.GetInt32
  /// rejects out-of-range samples internally), so no manual modulo-bias
  /// correction is needed here.
  /// </summary>
  public static string Generate(PasswordOptions options) {
    ValidateLength(options.Length);

    var classes = BuildSelectedClasses(options);

    if (classes.Count == 0) {
      throw new PasswordOptionsException(
          "Select at least one character type.");
    }

    if (options.Length < classes.Count) {
      throw new PasswordOptionsException(
          $"Length must be at least {classes.Count} to include one of each selected type.");
    }

    var pool = string.Concat(classes);
    var result = new char[options.Length];

    // Seed one guaranteed character per selected class first so the
    // "each selected class appears at least once" guarantee doesn't
    // depend on the luck of the full-pool draw below, then fill the
    // remainder from the combined pool and shuffle so the guaranteed
    // characters aren't predictably front-loaded.
    //
    // classes is never mutated after BuildSelectedClasses returns it,
    // so we iterate its backing storage directly via AsSpan rather than
    // through the List<T> enumerator.
    var nextSlot = 0;
    foreach (var characterClass in CollectionsMarshal.AsSpan(classes)) {
      result[nextSlot++] = characterClass[RandomNumberGenerator.GetInt32(characterClass.Length)];
    }

    for (var i = nextSlot; i < result.Length; i++) {
      result[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
    }

    ShuffleInPlace(result);
    return new string(result);
  }

  /// <summary>
  /// Upper bound on the key space in bits (length * log2(pool size)), shown to
  /// give the user a rough strength signal. This is a ceiling, not the
  /// true entropy of the result: always seeding one character per selected
  /// class slightly reduces true entropy versus pure uniform sampling, but
  /// the difference is negligible for any reasonable password length.
  /// </summary>
  public static double GetMaximumEntropyBits(PasswordOptions options) {
    ValidateLength(options.Length);

    var classes = BuildSelectedClasses(options);
    if (classes.Count == 0) {
      return 0;
    }

    var poolSize = classes.Sum(c => c.Length);
    return options.Length * Math.Log2(poolSize);
  }

  /// <summary>
  /// Size of the combined character pool for the given options - the "X"
  /// in the X^Length search-space formula. Kept separate from
  /// GetMaximumEntropyBits, consistent with how Generate() and
  /// GetMaximumEntropyBits() already each derive their own view of the
  /// selected classes rather than sharing one merged step.
  /// </summary>
  public static int GetPoolSize(PasswordOptions options) =>
      BuildSelectedClasses(options).Sum(c => c.Length);

  private static void ValidateLength(int length) {
    if (length <= 0) {
      throw new PasswordOptionsException(
          "Length must be greater than zero.");
    }
  }

  private static List<string> BuildSelectedClasses(PasswordOptions options) {
    var classes = new List<string>(CharacterClassCount);

    void AddIfSelected(bool selected, string characters) {
      if (!selected) {
        return;
      }

      var filtered = options.ExcludeAmbiguous
          ? new string(characters.Where(c => !Ambiguous.Contains(c)).ToArray())
          : characters;

      if (filtered.Length > 0) {
        classes.Add(filtered);
      }
    }

    AddIfSelected(options.IncludeUppercase, Uppercase);
    AddIfSelected(options.IncludeLowercase, Lowercase);
    AddIfSelected(options.IncludeDigits, Digits);
    AddIfSelected(options.IncludeSymbols, Symbols);

    return classes;
  }

  // Fisher-Yates using a CSPRNG for the swap index. Using crypto-secure
  // randomness here (not just for character selection) matters because
  // position also leaks information about the guaranteed-class seeding
  // above.
  private static void ShuffleInPlace(char[] characters) {
    for (var i = characters.Length - 1; i > 0; i--) {
      var j = RandomNumberGenerator.GetInt32(i + 1);
      (characters[i], characters[j]) = (characters[j], characters[i]);
    }
  }
}
