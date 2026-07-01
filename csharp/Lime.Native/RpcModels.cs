namespace Lime.Native;

internal sealed class RpcRequest
{
    public string? Action { get; set; }

    public global::System.Text.Json.JsonElement Body { get; set; }
}

internal sealed class RpcResponse
{
    public int Status { get; set; }

    public string Body { get; set; } = "";
}

internal sealed class CandidatesRequest
{
    public string? Keys { get; set; }
}

internal sealed class CommitRequest
{
    public string? Text { get; set; }

    public bool? New { get; set; }

    public bool? Update { get; set; }
}

internal sealed class MessageBody
{
    public string Message { get; set; } = "";
}

internal sealed class ErrorBody
{
    public string Message { get; set; } = "";
}

internal sealed class CandidatesResult
{
    public global::System.Collections.Generic.List<Candidate> Candidates { get; set; } = [];
}

internal sealed class Candidate
{
    public global::System.Collections.Generic.List<string> Pinyin { get; set; } = [];

    public double Score { get; set; }

    public string Word { get; set; } = "";

    public global::System.Collections.Generic.List<string> Remainkeys { get; set; } = [];

    public string Preedit { get; set; } = "";

    public int Consumedkeys { get; set; }
}

internal sealed class UserData
{
    public global::System.Collections.Generic.Dictionary<int, int[]> Words { get; set; } = [];

    public global::System.Collections.Generic.List<UserDataContextToken> Context { get; set; } = [];
}

internal sealed class UserDataContextToken
{
    public string T { get; set; } = "";

    public int Token { get; set; }
}

internal sealed class LastCandidates
{
    public long Time { get; set; }

    public global::System.Collections.Generic.List<string> Candidates { get; set; } = [];
}

internal sealed class InputLogSnapshot
{
    public global::System.Collections.Generic.List<double> KeyDeltaTimes { get; set; } = [];

    public long? LastKeyTime { get; set; }

    public global::System.Collections.Generic.List<double> ZiDeltaTimes { get; set; } = [];

    public long? LastZiTime { get; set; }

    public long ZiCount { get; set; }

    public LastCandidates LastCandidates { get; set; } = new();

    public global::System.Collections.Generic.Dictionary<int, global::System.Collections.Generic.List<double>> OffsetTimes { get; set; } = [];

    public string History { get; set; } = "";
}

internal sealed class RpcException : global::System.Exception
{
    public RpcException(int status, string message)
        : base(message)
    {
        this.Status = status;
    }

    public RpcException(int status, string message, global::System.Exception innerException)
        : base(message, innerException)
    {
        this.Status = status;
    }

    public int Status { get; }
}