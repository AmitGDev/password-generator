using System.Security.Cryptography;

namespace PasswordGenerator;

/// <summary>
/// How adjacent words (and the appended number, if any) are joined.
/// </summary>
public enum PassphraseSeparator {
  None,
  Hyphen,
  Underscore,
  Custom,
}

/// <summary>
/// How each word's leading letter is cased.
/// </summary>
public enum PassphraseCapitalization {
  None,
  TitleCase,
  RandomPerWord,
}

/// <summary>
/// Options describing how a passphrase should be composed. Kept separate
/// from the UI so the generator can be validated without any WPF
/// dependency, mirroring <see cref="PasswordOptions"/>.
/// </summary>
public sealed record PassphraseOptions {
  public int WordCount { get; init; } = 5;
  public PassphraseSeparator Separator { get; init; } = PassphraseSeparator.Hyphen;

  // Only consulted when Separator is Custom; ignored otherwise. Not
  // restricted to a single character - whatever the user typed is used
  // verbatim - since unlike Hyphen/Underscore there's no fixed meaning
  // to fall back on.
  public string CustomSeparator { get; init; } = "-";

  public PassphraseCapitalization Capitalization { get; init; } = PassphraseCapitalization.None;
  public bool AppendNumber { get; init; }
}

/// <summary>
/// Thrown when the requested options can't produce a valid passphrase
/// (currently just a non-positive word count).
/// </summary>
public sealed class PassphraseOptionsException(string message) : Exception(message);

public static class CryptoPassphraseGenerator {
  // Starter list for the layout/wiring pass - 100 words, all lowercase,
  // alphabetic only (ApplyCapitalization assumes a letter at index 0).
  // Swap this in place for the full ~7k-word list once the surrounding
  // pipeline (separator, capitalization, append options) is confirmed
  // working end to end; nothing else in this file depends on the count.
  private static readonly string[] Words = [
    "apple", "river", "tiger", "cloud", "stone", "flame", "ocean", "brave", "eagle", "silver",
    "maple", "storm", "coral", "amber", "frost", "delta", "echo", "forge", "glade", "haven",
    "ivory", "jungle", "karma", "lemon", "mirror", "noble", "opal", "pearl", "quartz", "raven",
    "summit", "thunder", "umbrella", "velvet", "willow", "xenon", "yonder", "zephyr", "anchor", "blossom",
    "canyon", "dagger", "ember", "falcon", "granite", "harbor", "inlet", "jasper", "kettle", "lantern",
    "meadow", "nectar", "oasis", "panther", "quiver", "ridge", "saffron", "temple", "unity", "vapor",
    "wander", "yield", "zenith", "badge", "comet", "driftwood", "exile", "fable", "glacier", "horizon",
    "indigo", "jolt", "kindle", "ledger", "mosaic", "nomad", "orbit", "prairie", "quest", "rustic",
    "shadow", "tundra", "utopia", "vortex", "whisper", "yarn", "zodiac", "arbor", "breeze", "cinder",
    "dawn", "elm", "fjord", "grove", "hollow", "island", "juniper", "knoll", "lagoon", "marsh",
  ];

  // Upper bound (exclusive) for the number GetInt32 draws when AppendNumber
  // is set - a 0-999 range, i.e. up to 3 digits, without zero-padding.
  private const int AppendedNumberUpperBound = 1000;

  /// <summary>
  /// Generates a passphrase using a CSPRNG for word selection and the
  /// random-capitalization coin flips and the appended number - every draw
  /// uses RandomNumberGenerator.GetInt32, which rejects out-of-range
  /// samples internally, so none of them need manual modulo-bias
  /// correction. The separator itself is never randomly drawn: whichever
  /// kind Options.Separator specifies is applied verbatim.
  /// </summary>
  public static string Generate(PassphraseOptions options) {
    ValidateWordCount(options.WordCount);

    var words = new string[options.WordCount];
    for (var i = 0; i < words.Length; i++) {
      words[i] = Words[RandomNumberGenerator.GetInt32(Words.Length)];
    }

    ApplyCapitalization(words, options.Capitalization);

    // Reused below for the appended number too, so the whole phrase reads
    // as one consistently-joined chain rather than switching join style
    // partway through.
    var joiner = SeparatorText(options);

    var passphrase = string.Join(joiner, words);

    if (options.AppendNumber) {
      passphrase += joiner + RandomNumberGenerator.GetInt32(AppendedNumberUpperBound);
    }

    return passphrase;
  }

  /// <summary>
  /// Upper bound on the key space in bits, shown to give the user a rough
  /// strength signal - mirrors GetMaximumEntropyBits in
  /// CryptoPasswordGenerator. Word choice is the dominant term
  /// (WordCount * log2(wordlist size)); RandomPerWord and AppendNumber
  /// each add their own independent draw on top, since neither constrains
  /// which words get picked. The separator (including Custom) contributes
  /// nothing here: like Hyphen/Underscore, it's a visible, user-chosen
  /// setting rather than a secret the generator drew at random.
  /// </summary>
  public static double GetMaximumEntropyBits(PassphraseOptions options) {
    ValidateWordCount(options.WordCount);

    var bits = options.WordCount * Math.Log2(Words.Length);

    if (options.Capitalization == PassphraseCapitalization.RandomPerWord) {
      bits += options.WordCount;
    }

    if (options.AppendNumber) {
      bits += Math.Log2(AppendedNumberUpperBound);
    }

    return bits;
  }

  /// <summary>
  /// Size of the wordlist - the "X" in the X^WordCount search-space
  /// formula, and what WordlistSizeTextBlock displays. Unlike
  /// GetPoolSize, this doesn't depend on PassphraseOptions: the wordlist
  /// isn't filtered by any user-facing choice the way character classes
  /// are.
  /// </summary>
  public static int GetWordlistSize() => Words.Length;

  private static void ValidateWordCount(int wordCount) {
    if (wordCount <= 0) {
      throw new PassphraseOptionsException(
          "Word count must be greater than zero.");
    }
  }

  private static void ApplyCapitalization(string[] words, PassphraseCapitalization capitalization) {
    switch (capitalization) {
      case PassphraseCapitalization.None:
        return;

      case PassphraseCapitalization.TitleCase:
        for (var i = 0; i < words.Length; i++) {
          words[i] = Capitalize(words[i]);
        }
        return;

      case PassphraseCapitalization.RandomPerWord:
        for (var i = 0; i < words.Length; i++) {
          // An independent CSPRNG coin flip per word, rather than one
          // flip applied to all of them - so knowing one word's case
          // reveals nothing about another's, and the entropy add in
          // GetMaximumEntropyBits (1 bit per word) actually holds.
          if (RandomNumberGenerator.GetInt32(2) == 1) {
            words[i] = Capitalize(words[i]);
          }
        }
        return;

      default:
        throw new ArgumentOutOfRangeException(nameof(capitalization));
    }
  }

  private static string Capitalize(string word) =>
      char.ToUpperInvariant(word[0]) + word[1..];

  private static string SeparatorText(PassphraseOptions options) => options.Separator switch {
    PassphraseSeparator.None => "",
    PassphraseSeparator.Hyphen => "-",
    PassphraseSeparator.Underscore => "_",
    PassphraseSeparator.Custom => options.CustomSeparator,
    _ => throw new ArgumentOutOfRangeException(nameof(options), options.Separator, null),
  };
}
