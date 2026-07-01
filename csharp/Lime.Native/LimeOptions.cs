namespace Lime.Native;

internal sealed class LimeOptions
{
    public string PipeName { get; init; } = "";

    public string? EngineName { get; init; }

    public string? ModelPath { get; init; }

    public string? DictionaryPath { get; init; }

    public int MaxNewTokens { get; init; } = 8;

    public bool ShowHelp { get; init; }

    public static LimeOptions Parse(string[] args, string defaultPipeName)
    {
        var pipeName = global::System.Environment.GetEnvironmentVariable("LIME_PIPE");
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            pipeName = defaultPipeName;
        }

        var engineName = global::System.Environment.GetEnvironmentVariable("LIME_ENGINE");
        var modelPath = global::System.Environment.GetEnvironmentVariable("LIME_ONNX_MODEL");
        string? dictionaryPath = null;
        var maxNewTokens = 8;
        var showHelp = false;

        for (var argumentIndex = 0; argumentIndex < args.Length; argumentIndex++)
        {
            var argument = args[argumentIndex];
            if (argument == "--help" || argument == "-h")
            {
                showHelp = true;
                continue;
            }

            if (argument == "--pipe")
            {
                pipeName = RequireValue(argument, args, ref argumentIndex);
                continue;
            }

            if (argument.StartsWith("--pipe=", global::System.StringComparison.Ordinal))
            {
                pipeName = argument["--pipe=".Length..];
                continue;
            }

            if (argument == "--engine")
            {
                engineName = RequireValue(argument, args, ref argumentIndex);
                continue;
            }

            if (argument.StartsWith("--engine=", global::System.StringComparison.Ordinal))
            {
                engineName = argument["--engine=".Length..];
                continue;
            }

            if (argument == "--model")
            {
                modelPath = RequireValue(argument, args, ref argumentIndex);
                continue;
            }

            if (argument.StartsWith("--model=", global::System.StringComparison.Ordinal))
            {
                modelPath = argument["--model=".Length..];
                continue;
            }

            if (argument == "--dictionary")
            {
                dictionaryPath = RequireValue(argument, args, ref argumentIndex);
                continue;
            }

            if (argument.StartsWith("--dictionary=", global::System.StringComparison.Ordinal))
            {
                dictionaryPath = argument["--dictionary=".Length..];
                continue;
            }

            if (argument == "--max-new-tokens")
            {
                maxNewTokens = ParsePositiveInt(argument, RequireValue(argument, args, ref argumentIndex));
                continue;
            }

            if (argument.StartsWith("--max-new-tokens=", global::System.StringComparison.Ordinal))
            {
                maxNewTokens = ParsePositiveInt("--max-new-tokens", argument["--max-new-tokens=".Length..]);
                continue;
            }

            throw new global::System.ArgumentException($"未知参数：{argument}");
        }

        return new LimeOptions
        {
            PipeName = string.IsNullOrWhiteSpace(pipeName) ? defaultPipeName : pipeName,
            EngineName = string.IsNullOrWhiteSpace(engineName) ? null : engineName,
            ModelPath = string.IsNullOrWhiteSpace(modelPath) ? null : modelPath,
            DictionaryPath = dictionaryPath,
            MaxNewTokens = maxNewTokens,
            ShowHelp = showHelp,
        };
    }

    private static int ParsePositiveInt(string optionName, string value)
    {
        if (!int.TryParse(value, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new global::System.ArgumentException($"参数 {optionName} 必须是正整数。");
        }

        return result;
    }

    private static string RequireValue(string optionName, string[] args, ref int argumentIndex)
    {
        if (argumentIndex + 1 >= args.Length)
        {
            throw new global::System.ArgumentException($"参数 {optionName} 需要一个值。");
        }

        argumentIndex++;
        return args[argumentIndex];
    }
}