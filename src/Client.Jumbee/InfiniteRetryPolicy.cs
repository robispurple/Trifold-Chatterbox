using Microsoft.AspNetCore.SignalR.Client;

namespace Client.Jumbee;

/// <summary>
/// Provides an unbounded SignalR reconnect backoff policy matching Client.Spectre and Client.TerminalGui.
/// </summary>
internal sealed class InfiniteRetryPolicy : IRetryPolicy
{
    #region Methods

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < delays.Length
            ? delays[retryContext.PreviousRetryCount]
            : delays[^1];

    #endregion

    #region Fields

    private static readonly TimeSpan[] delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    #endregion
}
