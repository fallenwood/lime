namespace Lime.Native;

internal sealed class OnnxLimeEngine : ILimeEngine, global::System.IDisposable
{
    private readonly PinyinDictionaryEngine dictionaryEngine;
    private readonly global::System.Threading.SemaphoreSlim generationLock = new(1, 1);
    private readonly object contextGate = new();
    private readonly global::Microsoft.ML.OnnxRuntimeGenAI.OgaHandle ogaHandle;
    private readonly global::Microsoft.ML.OnnxRuntimeGenAI.Model model;
    private readonly global::Microsoft.ML.OnnxRuntimeGenAI.Tokenizer tokenizer;
    private readonly int maxNewTokens;
    private string context = "";
    private bool disposed;

    public OnnxLimeEngine(string modelPath, PinyinDictionaryEngine dictionaryEngine, int maxNewTokens)
    {
        this.dictionaryEngine = dictionaryEngine;
        this.maxNewTokens = maxNewTokens;
        this.ogaHandle = new global::Microsoft.ML.OnnxRuntimeGenAI.OgaHandle();
        this.model = new global::Microsoft.ML.OnnxRuntimeGenAI.Model(modelPath);
        this.tokenizer = new global::Microsoft.ML.OnnxRuntimeGenAI.Tokenizer(this.model);
    }

    public async global::System.Threading.Tasks.ValueTask<CandidatesResult> CandidatesAsync(
        string keys,
        global::System.Threading.CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        var dictionaryResult = await this.dictionaryEngine.CandidatesAsync(keys, cancellationToken);
        if (string.IsNullOrWhiteSpace(keys))
        {
            return dictionaryResult;
        }

        await this.generationLock.WaitAsync(cancellationToken);
        try
        {
            var prompt = this.BuildPrompt(keys, dictionaryResult);
            var generatedText = this.GenerateText(prompt, cancellationToken);
            var candidate = BuildOnnxCandidate(generatedText, dictionaryResult);
            if (candidate is not null)
            {
                RemoveDuplicateCandidate(dictionaryResult, candidate.Word);
                dictionaryResult.Candidates.Insert(0, candidate);
                if (dictionaryResult.Candidates.Count > 80)
                {
                    dictionaryResult.Candidates.RemoveRange(80, dictionaryResult.Candidates.Count - 80);
                }
            }
        }
        finally
        {
            _ = this.generationLock.Release();
        }

        return dictionaryResult;
    }

    public async global::System.Threading.Tasks.ValueTask<string?> CommitAsync(
        string text,
        bool update,
        bool isNew,
        global::System.Threading.CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        var newText = await this.dictionaryEngine.CommitAsync(text, update, isNew, cancellationToken);
        lock (this.contextGate)
        {
            if (string.IsNullOrEmpty(newText))
            {
                return newText;
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

        return newText;
    }

    public UserData GetUserData()
    {
        this.ThrowIfDisposed();
        lock (this.contextGate)
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
        _ = await this.CommitAsync(text, update: true, isNew: true, cancellationToken);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.tokenizer.Dispose();
        this.model.Dispose();
        this.ogaHandle.Dispose();
        this.generationLock.Dispose();
        this.disposed = true;
    }

    private static Candidate? BuildOnnxCandidate(string generatedText, CandidatesResult dictionaryResult)
    {
        var word = SanitizeGeneratedText(generatedText);
        if (word.Length == 0)
        {
            return null;
        }

        var template = dictionaryResult.Candidates.Count == 0 ? null : dictionaryResult.Candidates[0];
        if (template is null)
        {
            return new Candidate
            {
                Pinyin = [],
                Score = 10,
                Word = word,
                Remainkeys = [],
                Preedit = "",
                Consumedkeys = 0,
            };
        }

        return new Candidate
        {
            Pinyin = new global::System.Collections.Generic.List<string>(template.Pinyin),
            Score = template.Score + 10,
            Word = word,
            Remainkeys = new global::System.Collections.Generic.List<string>(template.Remainkeys),
            Preedit = template.Preedit,
            Consumedkeys = template.Consumedkeys,
        };
    }

    private static void RemoveDuplicateCandidate(CandidatesResult result, string word)
    {
        for (var candidateIndex = result.Candidates.Count - 1; candidateIndex >= 0; candidateIndex--)
        {
            if (result.Candidates[candidateIndex].Word == word)
            {
                result.Candidates.RemoveAt(candidateIndex);
            }
        }
    }

    private static string SanitizeGeneratedText(string generatedText)
    {
        var text = generatedText.Trim();
        var lineBreak = text.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
        {
            text = text[..lineBreak].Trim();
        }

        foreach (var prefix in new[] { "候选：", "候选:", "输出：", "输出:" })
        {
            if (text.StartsWith(prefix, global::System.StringComparison.Ordinal))
            {
                text = text[prefix.Length..].Trim();
            }
        }

        text = text.Trim('"', '\'', '`', ' ', '\t');
        if (text.Length > 16)
        {
            text = text[..16];
        }

        return text;
    }

    private string BuildPrompt(string keys, CandidatesResult dictionaryResult)
    {
        var contextSnapshot = this.GetContextSnapshot();
        var dictionaryCandidates = new global::System.Text.StringBuilder();
        var candidateLimit = global::System.Math.Min(8, dictionaryResult.Candidates.Count);
        for (var candidateIndex = 0; candidateIndex < candidateLimit; candidateIndex++)
        {
            if (candidateIndex > 0)
            {
                dictionaryCandidates.Append('、');
            }

            dictionaryCandidates.Append(dictionaryResult.Candidates[candidateIndex].Word);
        }

        return "你是中文输入法候选生成器。根据上下文、拼音和字典候选，输出一个最可能的中文候选词或短语。只输出候选文本，不要解释。\n" +
            $"上下文：{contextSnapshot}\n" +
            $"拼音：{keys}\n" +
            $"字典候选：{dictionaryCandidates}\n" +
            "候选：";
    }

    private string GenerateText(string prompt, global::System.Threading.CancellationToken cancellationToken)
    {
        var sequences = this.tokenizer.Encode(prompt);
        var promptLength = sequences[0].Length;
        using var generatorParams = new global::Microsoft.ML.OnnxRuntimeGenAI.GeneratorParams(this.model);
        generatorParams.SetSearchOption("max_length", promptLength + this.maxNewTokens);
        generatorParams.SetSearchOption("do_sample", false);

        using var generator = new global::Microsoft.ML.OnnxRuntimeGenAI.Generator(this.model, generatorParams);
        generator.AppendTokenSequences(sequences);

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            generator.GenerateNextToken();
        }

        var sequence = generator.GetSequence(0);
        if (sequence.Length <= promptLength)
        {
            return "";
        }

        return this.tokenizer.Decode(sequence[promptLength..]);
    }

    private string GetContextSnapshot()
    {
        lock (this.contextGate)
        {
            if (this.context.Length <= 80)
            {
                return this.context;
            }

            return this.context[^80..];
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new global::System.ObjectDisposedException(nameof(OnnxLimeEngine));
        }
    }
}