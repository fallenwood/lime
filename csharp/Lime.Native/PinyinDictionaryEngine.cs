namespace Lime.Native;

internal sealed class PinyinDictionaryEngine : ILimeEngine
{
    private static readonly string[] Initials =
    [
        "zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l", "g", "k", "h", "j", "q", "x", "r", "z", "c", "s", "y", "w",
    ];

    private static readonly global::System.Collections.Generic.Dictionary<string, string> FuzzyInitials = new(global::System.StringComparer.Ordinal)
    {
        ["c"] = "ch",
        ["z"] = "zh",
        ["s"] = "sh",
        ["ch"] = "c",
        ["zh"] = "z",
        ["sh"] = "s",
    };

    private static readonly global::System.Collections.Generic.Dictionary<string, string> FuzzyFinals = new(global::System.StringComparer.Ordinal)
    {
        ["an"] = "ang",
        ["ang"] = "an",
        ["en"] = "eng",
        ["eng"] = "en",
        ["in"] = "ing",
        ["ing"] = "in",
        ["uan"] = "uang",
        ["uang"] = "uan",
    };

    private readonly object gate = new();
    private readonly global::System.Collections.Generic.Dictionary<string, WordEntry[]> entriesByPinyin;
    private readonly global::System.Collections.Generic.Dictionary<string, double> maxWeightByPinyin;
    private readonly global::System.Collections.Generic.List<string> sortedPinyin;
    private string context = "";

    private PinyinDictionaryEngine(
        global::System.Collections.Generic.Dictionary<string, WordEntry[]> entriesByPinyin,
        global::System.Collections.Generic.Dictionary<string, double> maxWeightByPinyin,
        global::System.Collections.Generic.List<string> sortedPinyin)
    {
        this.entriesByPinyin = entriesByPinyin;
        this.maxWeightByPinyin = maxWeightByPinyin;
        this.sortedPinyin = sortedPinyin;
    }

    public static PinyinDictionaryEngine Load(string dictionaryPath)
    {
        var entries = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<WordEntry>>(global::System.StringComparer.Ordinal);
        var isDataSection = false;

        foreach (var line in global::System.IO.File.ReadLines(dictionaryPath, global::System.Text.Encoding.UTF8))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length == 0 || trimmedLine.StartsWith("#", global::System.StringComparison.Ordinal))
            {
                continue;
            }

            if (!isDataSection)
            {
                if (trimmedLine == "...")
                {
                    isDataSection = true;
                }

                continue;
            }

            if (!TryParseDictionaryLine(trimmedLine, out var word, out var pinyin, out var weight))
            {
                continue;
            }

            if (!entries.TryGetValue(pinyin, out var pinyinEntries))
            {
                pinyinEntries = [];
                entries[pinyin] = pinyinEntries;
            }

            pinyinEntries.Add(new WordEntry(word, pinyin, weight));
        }

        var entriesByPinyin = new global::System.Collections.Generic.Dictionary<string, WordEntry[]>(global::System.StringComparer.Ordinal);
        var maxWeightByPinyin = new global::System.Collections.Generic.Dictionary<string, double>(global::System.StringComparer.Ordinal);
        var sortedPinyin = new global::System.Collections.Generic.List<string>(entries.Keys);
        sortedPinyin.Sort((left, right) =>
        {
            var lengthOrder = right.Length.CompareTo(left.Length);
            return lengthOrder != 0 ? lengthOrder : string.CompareOrdinal(left, right);
        });

        foreach (var entryPair in entries)
        {
            entryPair.Value.Sort((left, right) => right.Weight.CompareTo(left.Weight));
            entriesByPinyin[entryPair.Key] = entryPair.Value.ToArray();
            maxWeightByPinyin[entryPair.Key] = entryPair.Value.Count == 0 ? 1 : global::System.Math.Max(1, entryPair.Value[0].Weight);
        }

        return new PinyinDictionaryEngine(entriesByPinyin, maxWeightByPinyin, sortedPinyin);
    }

    public global::System.Threading.Tasks.ValueTask<CandidatesResult> CandidatesAsync(
        string keys,
        global::System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKeys = NormalizeKeys(keys);
        var result = new CandidatesResult();
        if (normalizedKeys.Length == 0)
        {
            return new global::System.Threading.Tasks.ValueTask<CandidatesResult>(result);
        }

        var segments = this.ParseSegments(normalizedKeys);
        if (segments.Count == 0)
        {
            return new global::System.Threading.Tasks.ValueTask<CandidatesResult>(result);
        }

        this.AddPhraseCandidate(segments, result.Candidates);
        this.AddSingleCandidates(segments, result.Candidates);
        result.Candidates.Sort(CompareCandidates);
        if (result.Candidates.Count > 80)
        {
            result.Candidates.RemoveRange(80, result.Candidates.Count - 80);
        }

        return new global::System.Threading.Tasks.ValueTask<CandidatesResult>(result);
    }

    public global::System.Threading.Tasks.ValueTask<string?> CommitAsync(
        string text,
        bool update,
        bool isNew,
        global::System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length == 0)
        {
            return new global::System.Threading.Tasks.ValueTask<string?>((string?)null);
        }

        string newText;
        lock (this.gate)
        {
            if (update && this.context.Length > 0 && text.StartsWith(this.context, global::System.StringComparison.Ordinal))
            {
                newText = text[this.context.Length..];
            }
            else
            {
                newText = text;
            }

            if (isNew)
            {
                this.context += newText;
            }
            else
            {
                this.context = text;
            }
        }

        return new global::System.Threading.Tasks.ValueTask<string?>(newText);
    }

    public UserData GetUserData()
    {
        lock (this.gate)
        {
            var contextTokens = new global::System.Collections.Generic.List<UserDataContextToken>();
            for (var contextIndex = 0; contextIndex < this.context.Length; contextIndex++)
            {
                contextTokens.Add(new UserDataContextToken
                {
                    T = this.context[contextIndex].ToString(),
                    Token = contextIndex,
                });
            }

            return new UserData
            {
                Words = [],
                Context = contextTokens,
            };
        }
    }

    public async global::System.Threading.Tasks.ValueTask LearnTextAsync(
        string text,
        global::System.Threading.CancellationToken cancellationToken)
    {
        await this.CommitAsync(text, update: true, isNew: true, cancellationToken);
    }

    private static int CompareCandidates(Candidate left, Candidate right)
    {
        var pinyinLengthOrder = right.Pinyin.Count.CompareTo(left.Pinyin.Count);
        if (pinyinLengthOrder != 0)
        {
            return pinyinLengthOrder;
        }

        return right.Score.CompareTo(left.Score);
    }

    private void AddPhraseCandidate(
        global::System.Collections.Generic.List<PinyinSegment> segments,
        global::System.Collections.Generic.List<Candidate> candidates)
    {
        if (segments.Count <= 1)
        {
            return;
        }

        var chosenEntries = new global::System.Collections.Generic.List<WordEntry>(segments.Count);
        foreach (var segment in segments)
        {
            if (!segment.IsExact || !this.TryGetTopEntry(segment.Options, out var entry))
            {
                return;
            }

            chosenEntries.Add(entry);
        }

        var wordBuilder = new global::System.Text.StringBuilder();
        var pinyin = new global::System.Collections.Generic.List<string>(chosenEntries.Count);
        var preedit = new global::System.Text.StringBuilder();
        var consumedKeys = 0;
        var score = 0.0;

        for (var entryIndex = 0; entryIndex < chosenEntries.Count; entryIndex++)
        {
            var entry = chosenEntries[entryIndex];
            wordBuilder.Append(entry.Word);
            pinyin.Add(entry.Pinyin);
            consumedKeys += segments[entryIndex].Key.Length;
            score += this.NormalizeScore(entry);
            if (entryIndex > 0)
            {
                preedit.Append(' ');
            }

            preedit.Append(entry.Pinyin);
        }

        candidates.Add(new Candidate
        {
            Pinyin = pinyin,
            Score = 1.0 + (score / chosenEntries.Count),
            Word = wordBuilder.ToString(),
            Remainkeys = [],
            Preedit = preedit.ToString(),
            Consumedkeys = consumedKeys,
        });
    }

    private void AddSingleCandidates(
        global::System.Collections.Generic.List<PinyinSegment> segments,
        global::System.Collections.Generic.List<Candidate> candidates)
    {
        var firstSegment = segments[0];
        var remainKeys = BuildRemainKeys(segments);
        var seenWords = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);

        foreach (var option in firstSegment.Options)
        {
            if (!this.entriesByPinyin.TryGetValue(option, out var entries))
            {
                continue;
            }

            var addedForOption = 0;
            foreach (var entry in entries)
            {
                if (!seenWords.Add(entry.Word))
                {
                    continue;
                }

                candidates.Add(new Candidate
                {
                    Pinyin = [entry.Pinyin],
                    Score = this.NormalizeScore(entry),
                    Word = entry.Word,
                    Remainkeys = new global::System.Collections.Generic.List<string>(remainKeys),
                    Preedit = remainKeys.Count == 0 ? entry.Pinyin : entry.Pinyin + " ",
                    Consumedkeys = firstSegment.Key.Length,
                });

                addedForOption++;
                if (addedForOption >= 24 || candidates.Count >= 96)
                {
                    break;
                }
            }
        }
    }

    private bool TryGetTopEntry(global::System.Collections.Generic.List<string> pinyinOptions, out WordEntry entry)
    {
        foreach (var option in pinyinOptions)
        {
            if (this.entriesByPinyin.TryGetValue(option, out var entries) && entries.Length > 0)
            {
                entry = entries[0];
                return true;
            }
        }

        entry = WordEntry.Empty;
        return false;
    }

    private double NormalizeScore(WordEntry entry)
    {
        return entry.Weight / this.maxWeightByPinyin[entry.Pinyin];
    }

    private global::System.Collections.Generic.List<PinyinSegment> ParseSegments(string keys)
    {
        var segments = new global::System.Collections.Generic.List<PinyinSegment>();
        var keyIndex = 0;

        while (keyIndex < keys.Length)
        {
            if (keys[keyIndex] == '\'')
            {
                keyIndex++;
                continue;
            }

            var exactPinyin = this.FindLongestExactPinyin(keys, keyIndex);
            if (exactPinyin is not null)
            {
                segments.Add(new PinyinSegment(exactPinyin, this.BuildPinyinOptions(exactPinyin), isExact: true));
                keyIndex += exactPinyin.Length;
                continue;
            }

            var partialEnd = keys.IndexOf('\'', keyIndex);
            if (partialEnd == -1)
            {
                partialEnd = keys.Length;
            }

            var partialKey = keys[keyIndex..partialEnd];
            var options = this.BuildPartialOptions(partialKey);
            if (options.Count == 0)
            {
                return segments;
            }

            segments.Add(new PinyinSegment(partialKey, options, isExact: false));
            break;
        }

        return segments;
    }

    private string? FindLongestExactPinyin(string keys, int startIndex)
    {
        foreach (var pinyin in this.sortedPinyin)
        {
            if (keys.Length - startIndex < pinyin.Length)
            {
                continue;
            }

            if (string.CompareOrdinal(keys, startIndex, pinyin, 0, pinyin.Length) == 0)
            {
                return pinyin;
            }
        }

        return null;
    }

    private global::System.Collections.Generic.List<string> BuildPinyinOptions(string pinyin)
    {
        var options = new global::System.Collections.Generic.List<string>();
        var seenOptions = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);

        foreach (var option in GenerateFuzzyPinyin(pinyin))
        {
            if (this.entriesByPinyin.ContainsKey(option) && seenOptions.Add(option))
            {
                options.Add(option);
            }
        }

        return options;
    }

    private global::System.Collections.Generic.List<string> BuildPartialOptions(string partialKey)
    {
        var options = new global::System.Collections.Generic.List<string>();
        foreach (var pinyin in this.sortedPinyin)
        {
            if (!pinyin.StartsWith(partialKey, global::System.StringComparison.Ordinal))
            {
                continue;
            }

            options.Add(pinyin);
            if (options.Count >= 24)
            {
                break;
            }
        }

        return options;
    }

    private static global::System.Collections.Generic.List<string> BuildRemainKeys(global::System.Collections.Generic.List<PinyinSegment> segments)
    {
        var remainKeys = new global::System.Collections.Generic.List<string>();
        for (var segmentIndex = 1; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            remainKeys.Add(segment.Options.Count == 0 ? segment.Key : segment.Options[0]);
        }

        return remainKeys;
    }

    private static global::System.Collections.Generic.IEnumerable<string> GenerateFuzzyPinyin(string pinyin)
    {
        yield return pinyin;

        var initial = "";
        foreach (var candidateInitial in Initials)
        {
            if (pinyin.StartsWith(candidateInitial, global::System.StringComparison.Ordinal))
            {
                initial = candidateInitial;
                break;
            }
        }

        var final = pinyin[initial.Length..];
        if (initial.Length == 0 || final.Length == 0)
        {
            yield break;
        }

        _ = FuzzyInitials.TryGetValue(initial, out var fuzzyInitial);
        _ = FuzzyFinals.TryGetValue(final, out var fuzzyFinal);
        if (fuzzyInitial is not null)
        {
            yield return fuzzyInitial + final;
        }

        if (fuzzyFinal is not null)
        {
            yield return initial + fuzzyFinal;
        }

        if (fuzzyInitial is not null && fuzzyFinal is not null)
        {
            yield return fuzzyInitial + fuzzyFinal;
        }
    }

    private static bool TryParseDictionaryLine(string line, out string word, out string pinyin, out double weight)
    {
        word = "";
        pinyin = "";
        weight = 1;

        var firstSeparator = FindWhitespace(line, 0);
        if (firstSeparator == -1)
        {
            return false;
        }

        var secondStart = SkipWhitespace(line, firstSeparator);
        var secondSeparator = FindWhitespace(line, secondStart);
        if (secondStart >= line.Length || secondSeparator == -1)
        {
            return false;
        }

        var thirdStart = SkipWhitespace(line, secondSeparator);
        word = line[..firstSeparator];
        pinyin = line[secondStart..secondSeparator];
        if (thirdStart < line.Length)
        {
            var weightText = line[thirdStart..];
            _ = double.TryParse(
                weightText,
                global::System.Globalization.NumberStyles.Float,
                global::System.Globalization.CultureInfo.InvariantCulture,
                out weight);
        }

        return word.Length > 0 && pinyin.Length > 0;
    }

    private static int FindWhitespace(string value, int startIndex)
    {
        for (var charIndex = startIndex; charIndex < value.Length; charIndex++)
        {
            if (char.IsWhiteSpace(value[charIndex]))
            {
                return charIndex;
            }
        }

        return -1;
    }

    private static int SkipWhitespace(string value, int startIndex)
    {
        var charIndex = startIndex;
        while (charIndex < value.Length && char.IsWhiteSpace(value[charIndex]))
        {
            charIndex++;
        }

        return charIndex;
    }

    private static string NormalizeKeys(string keys)
    {
        var builder = new global::System.Text.StringBuilder(keys.Length);
        foreach (var currentChar in keys.Trim().ToLowerInvariant())
        {
            if (!char.IsWhiteSpace(currentChar))
            {
                builder.Append(currentChar);
            }
        }

        return builder.ToString();
    }

    private sealed class PinyinSegment(string key, global::System.Collections.Generic.List<string> options, bool isExact)
    {
        public string Key { get; } = key;

        public global::System.Collections.Generic.List<string> Options { get; } = options;

        public bool IsExact { get; } = isExact;
    }

    private sealed class WordEntry(string word, string pinyin, double weight)
    {
        public static readonly WordEntry Empty = new("", "", 0);

        public string Word { get; } = word;

        public string Pinyin { get; } = pinyin;

        public double Weight { get; } = weight;
    }
}