namespace Lime.Native;

internal sealed class InputLog
{
    private const int MaxLength = 100_000;

    private readonly object gate = new();
    private readonly global::System.Collections.Generic.List<double> keyDeltaTimes = [];
    private readonly global::System.Collections.Generic.List<double> ziDeltaTimes = [];
    private readonly global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<double>> offsetTimes = [];
    private long? lastKeyTime;
    private long? lastZiTime;
    private long ziCount;
    private LastCandidates lastCandidates = new();
    private string history = "";

    public long RecordKeys(string keys)
    {
        var time = NowMilliseconds();
        lock (this.gate)
        {
            if (this.lastKeyTime is null || keys.Length == 1)
            {
                this.lastKeyTime = time;
                this.lastZiTime = time;
            }
            else
            {
                PushLimited(this.keyDeltaTimes, time - this.lastKeyTime.Value);
                this.lastKeyTime = time;
            }
        }

        return time;
    }

    public void RecordCandidates(CandidatesResult result, long time)
    {
        lock (this.gate)
        {
            if (result.Candidates.Count <= 1)
            {
                this.lastZiTime = null;
                return;
            }

            var candidateWords = new global::System.Collections.Generic.List<string>(result.Candidates.Count);
            foreach (var candidate in result.Candidates)
            {
                candidateWords.Add(candidate.Word);
            }

            this.lastCandidates = new LastCandidates
            {
                Time = time,
                Candidates = candidateWords,
            };
        }
    }

    public void RecordCommit(string text, bool isNew, string? newText)
    {
        var time = NowMilliseconds();
        lock (this.gate)
        {
            if (isNew)
            {
                if (this.lastZiTime is not null && text.Length > 0)
                {
                    PushLimited(this.ziDeltaTimes, (time - this.lastZiTime.Value) / (double)text.Length);
                }

                this.lastZiTime = null;
                this.lastKeyTime = null;
                this.ziCount += text.Length;
                this.history += text;
            }

            var offset = this.lastCandidates.Candidates.IndexOf(newText ?? "");
            if (offset != -1 && this.lastCandidates.Time != 0)
            {
                if (!this.offsetTimes.TryGetValue(offset, out var offsets))
                {
                    offsets = [];
                    this.offsetTimes[offset] = offsets;
                }

                PushLimited(offsets, time - this.lastCandidates.Time);
            }

            this.lastCandidates = new LastCandidates();
        }
    }

    public InputLogSnapshot Snapshot()
    {
        lock (this.gate)
        {
            var offsetSnapshot = new global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<double>>();
            foreach (var offsetPair in this.offsetTimes)
            {
                offsetSnapshot[offsetPair.Key] = new global::System.Collections.Generic.List<double>(offsetPair.Value);
            }

            return new InputLogSnapshot
            {
                KeyDeltaTimes = new global::System.Collections.Generic.List<double>(this.keyDeltaTimes),
                LastKeyTime = this.lastKeyTime,
                ZiDeltaTimes = new global::System.Collections.Generic.List<double>(this.ziDeltaTimes),
                LastZiTime = this.lastZiTime,
                ZiCount = this.ziCount,
                LastCandidates = new LastCandidates
                {
                    Time = this.lastCandidates.Time,
                    Candidates = new global::System.Collections.Generic.List<string>(this.lastCandidates.Candidates),
                },
                OffsetTimes = offsetSnapshot,
                History = this.history,
            };
        }
    }

    private static long NowMilliseconds()
    {
        return global::System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static void PushLimited(global::System.Collections.Generic.List<double> values, double value)
    {
        values.Add(value);
        if (values.Count <= MaxLength)
        {
            return;
        }

        values.RemoveRange(0, values.Count - MaxLength);
    }
}