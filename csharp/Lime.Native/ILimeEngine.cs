namespace Lime.Native;

internal interface ILimeEngine
{
    global::System.Threading.Tasks.ValueTask<CandidatesResult> CandidatesAsync(
        string keys,
        global::System.Threading.CancellationToken cancellationToken);

    global::System.Threading.Tasks.ValueTask<string?> CommitAsync(
        string text,
        bool update,
        bool isNew,
        global::System.Threading.CancellationToken cancellationToken);

    UserData GetUserData();

    global::System.Threading.Tasks.ValueTask LearnTextAsync(
        string text,
        global::System.Threading.CancellationToken cancellationToken);
}