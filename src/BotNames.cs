using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Horizon;

internal static partial class BotNames
{
    private static readonly Regex RichText = new(@"</?(?:color|b|i|size)(?:=[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Cyrillic = new("[а-яА-ЯёЁіІїЇєЄґҐўЎ]");
    private static readonly Regex Words = new(@"[\p{L}\p{M}]+");
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "",
        ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya", ['і'] = "i", ['ї'] = "yi",
        ['є'] = "ye", ['ґ'] = "g", ['ў'] = "w"
    };

    internal static string Display(string? nickname, string fallback)
    {
        if (string.IsNullOrEmpty(nickname)) return fallback;
        string name = nickname.IndexOf('<') >= 0 ? RichText.Replace(nickname, "") : nickname;
        if (name.Length == 0) return fallback;
        if (!Cyrillic.IsMatch(name)) return name;
        return Known.TryGetValue(name, out var translated) ? translated : Words.Replace(name, TransliterateWord);
    }

    private static string TransliterateWord(Match match)
    {
        string word = match.Value;
        bool allCaps = word.Length > 1 && word == word.ToUpperInvariant();
        var result = new StringBuilder(word.Length);
        foreach (char letter in word)
        {
            if (!Letters.TryGetValue(char.ToLowerInvariant(letter), out var latin)) result.Append(letter);
            else if (!char.IsUpper(letter) || latin.Length == 0) result.Append(latin);
            else if (allCaps) result.Append(latin.ToUpperInvariant());
            else result.Append(char.ToUpperInvariant(latin[0])).Append(latin, 1, latin.Length - 1);
        }
        return result.ToString();
    }
}
