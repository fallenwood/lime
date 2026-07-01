namespace Lime.Native;

internal static class Program
{
    private const string DefaultPipeName = "lime";

    public static async global::System.Threading.Tasks.Task<int> Main(string[] args)
    {
        LimeOptions options;
        try
        {
            options = LimeOptions.Parse(args, DefaultPipeName);
        }
        catch (global::System.ArgumentException exception)
        {
            global::System.Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (!global::System.OperatingSystem.IsWindows())
        {
            global::System.Console.Error.WriteLine("当前 C# 服务器仅支持 Windows 命名管道。");
            return 1;
        }

        try
        {
            var dictionaryPath = ResolveDictionaryPath(options.DictionaryPath);
            var dictionaryEngine = PinyinDictionaryEngine.Load(dictionaryPath);
            var engine = CreateEngine(options, dictionaryEngine);
            using var engineLifetime = engine as global::System.IDisposable;
            var inputLog = new InputLog();
            var server = new NamedPipeRpcServer(engine, inputLog);

            using var cancellation = new global::System.Threading.CancellationTokenSource();
            global::System.Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            global::System.Console.WriteLine($"C# LIME NativeAOT server listening on {NamedPipeRpcServer.GetDisplayPipePath(options.PipeName)}");
            global::System.Console.WriteLine($"Dictionary: {dictionaryPath}");
            global::System.Console.WriteLine($"Engine: {GetEngineDisplayName(options)}");
            await server.RunAsync(options.PipeName, cancellation.Token);
            return 0;
        }
        catch (global::System.OperationCanceledException)
        {
            return 0;
        }
        catch (global::System.Exception exception)
        {
            global::System.Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static ILimeEngine CreateEngine(LimeOptions options, PinyinDictionaryEngine dictionaryEngine)
    {
        var engineName = options.EngineName;
        if (string.IsNullOrWhiteSpace(engineName))
        {
            engineName = string.IsNullOrWhiteSpace(options.ModelPath) ? "dictionary" : "onnx";
        }

        if (string.Equals(engineName, "dictionary", global::System.StringComparison.OrdinalIgnoreCase))
        {
            return dictionaryEngine;
        }

        if (string.Equals(engineName, "onnx", global::System.StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.ModelPath))
            {
                throw new global::System.ArgumentException("ONNX 引擎需要通过 --model 或 LIME_ONNX_MODEL 指定 ONNX Runtime GenAI 模型目录。");
            }

            return new OnnxLimeEngine(ResolveModelPath(options.ModelPath), dictionaryEngine, options.MaxNewTokens);
        }

        throw new global::System.ArgumentException($"未知引擎：{engineName}。可用值：dictionary, onnx。");
    }

    private static string ResolveModelPath(string configuredPath)
    {
        var fullPath = global::System.IO.Path.GetFullPath(configuredPath);
        if (global::System.IO.Directory.Exists(fullPath) || global::System.IO.File.Exists(fullPath))
        {
            return fullPath;
        }

        throw new global::System.IO.FileNotFoundException("找不到 ONNX Runtime GenAI 模型。", fullPath);
    }

    private static string GetEngineDisplayName(LimeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.EngineName))
        {
            return options.EngineName;
        }

        return string.IsNullOrWhiteSpace(options.ModelPath) ? "dictionary" : "onnx";
    }

    private static string ResolveDictionaryPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = global::System.IO.Path.GetFullPath(configuredPath);
            if (global::System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }

            throw new global::System.IO.FileNotFoundException("找不到拼音字典。", fullPath);
        }

        var candidates = new[]
        {
            global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, "assets", "pinyin", "8105.dict.yaml"),
            global::System.IO.Path.Combine(global::System.Environment.CurrentDirectory, "assets", "pinyin", "8105.dict.yaml"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = global::System.IO.Path.GetFullPath(candidate);
            if (global::System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new global::System.IO.FileNotFoundException("找不到拼音字典 assets/pinyin/8105.dict.yaml。");
    }

    private static void PrintUsage()
    {
        global::System.Console.WriteLine("Usage: dotnet run --project csharp/Lime.Native -- [--pipe lime] [--engine dictionary|onnx] [--model path] [--dictionary path]");
        global::System.Console.WriteLine("       LIME_ONNX_MODEL can also provide the ONNX Runtime GenAI model directory.");
        global::System.Console.WriteLine("       dotnet publish csharp/Lime.Native -c Release -r win-arm64 /p:PublishAot=true");
    }
}