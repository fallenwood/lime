namespace Lime.Native;

internal sealed class NamedPipeRpcServer(ILimeEngine limeEngine, InputLog inputLog)
{
    private const string WindowsNamedPipePrefix = @"\\.\pipe\";

    private readonly ILimeEngine limeEngine = limeEngine;
    private readonly InputLog inputLog = inputLog;

    public static string GetDisplayPipePath(string pipeName)
    {
        var normalizedPipeName = NormalizePipeName(pipeName);
        return WindowsNamedPipePrefix + normalizedPipeName;
    }

    public async global::System.Threading.Tasks.Task RunAsync(string pipeName, global::System.Threading.CancellationToken cancellationToken)
    {
        var normalizedPipeName = NormalizePipeName(pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var pipeStream = new global::System.IO.Pipes.NamedPipeServerStream(
                normalizedPipeName,
                global::System.IO.Pipes.PipeDirection.InOut,
                global::System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances,
                global::System.IO.Pipes.PipeTransmissionMode.Byte,
                global::System.IO.Pipes.PipeOptions.Asynchronous);

            try
            {
                await pipeStream.WaitForConnectionAsync(cancellationToken);
            }
            catch
            {
                await pipeStream.DisposeAsync();
                throw;
            }

            _ = global::System.Threading.Tasks.Task.Run(
                () => this.HandleClientAsync(pipeStream, cancellationToken),
                cancellationToken);
        }
    }

    private static string NormalizePipeName(string pipeName)
    {
        var normalizedPipeName = string.IsNullOrWhiteSpace(pipeName) ? "lime" : pipeName.Trim();
        if (normalizedPipeName.StartsWith(WindowsNamedPipePrefix, global::System.StringComparison.OrdinalIgnoreCase))
        {
            normalizedPipeName = normalizedPipeName[WindowsNamedPipePrefix.Length..];
        }

        if (normalizedPipeName.Length == 0)
        {
            throw new global::System.ArgumentException("命名管道名称不能为空。", nameof(pipeName));
        }

        return normalizedPipeName;
    }

    private async global::System.Threading.Tasks.Task HandleClientAsync(
        global::System.IO.Pipes.NamedPipeServerStream pipeStream,
        global::System.Threading.CancellationToken cancellationToken)
    {
        await using (pipeStream)
        {
            try
            {
                using var reader = new global::System.IO.StreamReader(
                    pipeStream,
                    global::System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);
                await using var writer = new global::System.IO.StreamWriter(
                    pipeStream,
                    new global::System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: true)
                {
                    NewLine = "\n",
                    AutoFlush = true,
                };

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return;
                }

                var response = await this.RespondToLineAsync(line, cancellationToken);
                var responseLine = global::System.Text.Json.JsonSerializer.Serialize(response, LimeJsonContext.Relaxed.RpcResponse);
                await writer.WriteLineAsync(responseLine);
            }
            catch (global::System.OperationCanceledException)
            {
                throw;
            }
            catch (global::System.Exception exception)
            {
                global::System.Console.Error.WriteLine("命名管道连接错误:");
                global::System.Console.Error.WriteLine(exception);
            }
        }
    }

    private async global::System.Threading.Tasks.ValueTask<RpcResponse> RespondToLineAsync(
        string line,
        global::System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var request = global::System.Text.Json.JsonSerializer.Deserialize(line, LimeJsonContext.Relaxed.RpcRequest);
            if (request is null)
            {
                throw new RpcException(400, "命名管道请求为空");
            }

            return await this.HandleRpcRequestAsync(request, cancellationToken);
        }
        catch (RpcException exception)
        {
            return RpcJson(exception.Status, new ErrorBody { Message = exception.Message }, LimeJsonContext.Relaxed.ErrorBody);
        }
        catch (global::System.Text.Json.JsonException exception)
        {
            global::System.Console.Error.WriteLine("命名管道请求 JSON 格式错误:");
            global::System.Console.Error.WriteLine(exception);
            return RpcJson(400, new ErrorBody { Message = "命名管道请求 JSON 格式错误" }, LimeJsonContext.Relaxed.ErrorBody);
        }
        catch (global::System.Exception exception)
        {
            global::System.Console.Error.WriteLine("处理命名管道请求失败:");
            global::System.Console.Error.WriteLine(exception);
            return RpcJson(500, new ErrorBody { Message = "命名管道请求处理失败" }, LimeJsonContext.Relaxed.ErrorBody);
        }
    }

    private async global::System.Threading.Tasks.ValueTask<RpcResponse> HandleRpcRequestAsync(
        RpcRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {
        switch (request.Action)
        {
            case "candidates":
                return await this.HandleCandidatesAsync(request.Body, cancellationToken);
            case "commit":
                return await this.HandleCommitAsync(request.Body, cancellationToken);
            case "userdata":
                return RpcJson(200, this.limeEngine.GetUserData(), LimeJsonContext.Relaxed.UserData);
            case "inputlog":
                return RpcJson(200, this.inputLog.Snapshot(), LimeJsonContext.Relaxed.InputLogSnapshot);
            case "learntext":
                return await this.HandleLearnTextAsync(request.Body, cancellationToken);
            default:
                throw new RpcException(404, $"未知命名管道操作：{request.Action}");
        }
    }

    private async global::System.Threading.Tasks.ValueTask<RpcResponse> HandleCandidatesAsync(
        global::System.Text.Json.JsonElement body,
        global::System.Threading.CancellationToken cancellationToken)
    {
        var data = ParseRpcBody(body, new CandidatesRequest(), LimeJsonContext.Relaxed.CandidatesRequest);
        var keys = data.Keys ?? "";
        var time = this.inputLog.RecordKeys(keys);
        var result = await this.limeEngine.CandidatesAsync(keys, cancellationToken);
        this.inputLog.RecordCandidates(result, time);
        return RpcJson(200, result, LimeJsonContext.Relaxed.CandidatesResult);
    }

    private async global::System.Threading.Tasks.ValueTask<RpcResponse> HandleCommitAsync(
        global::System.Text.Json.JsonElement body,
        global::System.Threading.CancellationToken cancellationToken)
    {
        var data = ParseRpcBody(body, new CommitRequest(), LimeJsonContext.Relaxed.CommitRequest);
        var text = data.Text ?? "";
        if (text.Length == 0)
        {
            throw new RpcException(400, "未提供文本内容");
        }

        var isNew = data.New ?? true;
        var shouldUpdate = data.Update ?? false;
        var newText = await this.limeEngine.CommitAsync(text, shouldUpdate, isNew, cancellationToken);
        this.inputLog.RecordCommit(text, isNew, newText);
        return RpcJson(200, new MessageBody { Message = "文本提交成功" }, LimeJsonContext.Relaxed.MessageBody);
    }

    private async global::System.Threading.Tasks.ValueTask<RpcResponse> HandleLearnTextAsync(
        global::System.Text.Json.JsonElement body,
        global::System.Threading.CancellationToken cancellationToken)
    {
        var text = body.ValueKind == global::System.Text.Json.JsonValueKind.String
            ? body.GetString() ?? ""
            : body.GetRawText();
        await this.limeEngine.LearnTextAsync(text, cancellationToken);
        return RpcJson(200, new MessageBody { Message = "文本提交成功" }, LimeJsonContext.Relaxed.MessageBody);
    }

    private static TBody ParseRpcBody<TBody>(
        global::System.Text.Json.JsonElement body,
        TBody fallback,
        global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TBody> jsonTypeInfo)
    {
        if (body.ValueKind is global::System.Text.Json.JsonValueKind.Undefined or global::System.Text.Json.JsonValueKind.Null)
        {
            return fallback;
        }

        try
        {
            if (body.ValueKind == global::System.Text.Json.JsonValueKind.String)
            {
                var text = body.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return fallback;
                }

                return global::System.Text.Json.JsonSerializer.Deserialize(text, jsonTypeInfo) ?? fallback;
            }

            return global::System.Text.Json.JsonSerializer.Deserialize(body.GetRawText(), jsonTypeInfo) ?? fallback;
        }
        catch (global::System.Text.Json.JsonException exception)
        {
            throw new RpcException(400, "请求数据格式错误", exception);
        }
    }

    private static RpcResponse RpcJson<TValue>(
        int status,
        TValue value,
        global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> jsonTypeInfo)
    {
        return new RpcResponse
        {
            Status = status,
            Body = global::System.Text.Json.JsonSerializer.Serialize(value, jsonTypeInfo),
        };
    }
}