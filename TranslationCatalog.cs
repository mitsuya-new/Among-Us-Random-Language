using System.Globalization;
using System.Text.RegularExpressions;

namespace RandomLanguageMod;

internal sealed class TranslationCatalog
{
    internal sealed record Request(string Key, string[] Arguments, bool Upper, string Prefix, string Suffix);
    private readonly Dictionary<int, Dictionary<string, string>> languages;
    private readonly int[] selectable;
    private readonly Dictionary<string, string> exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Key, Regex Pattern, int[] Indices)> patterns = new();
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:,-?\d+)?(?::[^{}]+)?\}");

    internal TranslationCatalog(Dictionary<int, Dictionary<string, string>> languages, int sourceLanguage, int[]? allowed = null)
    {
        this.languages = languages;
        selectable = allowed ?? languages.Keys.ToArray();
        foreach (var table in languages.OrderBy(p => p.Key == sourceLanguage ? 0 : 1))
            foreach (var pair in table.Value)
                if (!string.IsNullOrWhiteSpace(pair.Value) && !Placeholder.IsMatch(pair.Value) &&
                    pair.Value.Any(char.IsLetter)) exact.TryAdd(pair.Value.Trim(), pair.Key);
        if (!languages.TryGetValue(sourceLanguage, out var source)) return;
        foreach (var pair in source)
        {
            var matches = Placeholder.Matches(pair.Value);
            if (matches.Count == 0 || matches.Count > 8 || pair.Value.Replace(matches[0].Value, "").Trim().Length < 3)
                continue;
            var indices = matches.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
            if (indices.Max() > 15) continue;
            string regex = "\\A";
            int offset = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                regex += Regex.Escape(pair.Value[offset..match.Index]) + $"(?<a{i}>.+?)";
                offset = match.Index + match.Length;
            }
            regex += Regex.Escape(pair.Value[offset..]) + "\\z";
            patterns.Add((pair.Key, new Regex(regex, RegexOptions.Singleline | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(5)), indices));
        }
    }

    internal Request? Resolve(string text, string? preferredKey = null, bool forceUpper = false)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        int start = text.IndexOf(trimmed, StringComparison.Ordinal);
        string prefix = text[..start], suffix = text[(start + trimmed.Length)..];
        bool upper = forceUpper || (trimmed.Any(c => char.ToUpperInvariant(c) != char.ToLowerInvariant(c)) &&
            trimmed == trimmed.ToUpperInvariant());
        if (preferredKey != null && languages.Values.Any(t => t.TryGetValue(preferredKey, out var value) &&
                string.Equals(value.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            return new Request(preferredKey, Array.Empty<string>(), upper, prefix, suffix);
        if (exact.TryGetValue(trimmed, out var key))
            return new Request(key, Array.Empty<string>(), upper, prefix, suffix);
        foreach (var pattern in patterns)
        {
            Match match;
            try { match = pattern.Pattern.Match(trimmed); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var args = new string[pattern.Indices.Max() + 1];
            Array.Fill(args, "");
            for (int i = 0; i < pattern.Indices.Length; i++) args[pattern.Indices[i]] = match.Groups[$"a{i}"].Value;
            return new Request(pattern.Key, args, upper, prefix, suffix);
        }
        return null;
    }

    internal string? Render(Request request, int language)
    {
        if (!languages.TryGetValue(language, out var table) || !table.TryGetValue(request.Key, out var value) ||
            string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (request.Arguments.Length > 0)
                value = string.Format(CultureInfo.InvariantCulture, value, request.Arguments.Cast<object>().ToArray());
            else if (Placeholder.IsMatch(value)) return null;
            if (request.Upper) value = value.ToUpperInvariant();
            return request.Prefix + value + request.Suffix;
        }
        catch (FormatException) { return null; }
    }

    internal (int Language, string Text)? Choose(Request request, int? previous, Random random, string? previousText = null)
    {
        var choices = selectable.Select(l => (Language: l, Text: Render(request, l)))
            .Where(p => p.Text != null).ToArray();
        if (choices.Length == 0) return null;
        var other = choices.Where(p => p.Language != previous).ToArray();
        var pool = other.Length > 0 ? other : choices;
        var visiblyDifferent = pool.Where(p => p.Text != previousText).ToArray();
        if (visiblyDifferent.Length > 0) pool = visiblyDifferent;
        var choice = pool[random.Next(pool.Length)];
        return (choice.Language, choice.Text!);
    }

    internal int DistinctTranslations(Request request) => selectable.Select(l => Render(request, l))
        .Where(t => t != null).Distinct(StringComparer.Ordinal).Count();
}
