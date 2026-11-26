using ConsoleGUI.Data;
using Jumbee.Console;
using JumbeeColor = global::Jumbee.Console.Color;
using static global::Jumbee.Console.Color;

namespace Client.Jumbee;

/// <summary>
/// Custom retained-mode header control that writes directly into Jumbee's differential ANSI cell buffer
/// and triggers dirty-region repaints via <see cref="Control.Invalidate"/>.
/// </summary>
internal sealed class ChatHeaderBar : Control
{
    #region Constructors

    public ChatHeaderBar(string username, string chatHubUrl, string grpcBaseUrl)
    {
        this.username = username;
        this.chatHubUrl = chatHubUrl;
        this.grpcBaseUrl = grpcBaseUrl;
        Focusable = false;
    }

    #endregion

    #region Methods

    public void UpdateStatus(string nextStatus)
    {
        if (string.Equals(status, nextStatus, StringComparison.Ordinal))
        {
            return;
        }

        status = nextStatus;
        Invalidate();
    }

    public void UpdateHeartbeat(string uptime, long timestampUtc)
    {
        var localTime = DateTimeOffset.FromUnixTimeSeconds(timestampUtc).ToLocalTime();
        var formatted = $"♥ {uptime} ({localTime:HH:mm:ss})";
        if (string.Equals(heartbeat, formatted, StringComparison.Ordinal))
        {
            return;
        }

        heartbeat = formatted;
        Invalidate();
    }

    protected override int IntrinsicHeight() => 2;

    protected override void Render()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ClearRow(0, width, headerBg);
        RenderTitleRow(width);

        if (height > 1)
        {
            ClearRow(1, width, metaBg);
            RenderMetadataRow(width);
        }
    }

    private void RenderTitleRow(int width)
    {
        var title = $" Chatterbox - Jumbee ({username}) ";
        var statusPrefix = "│ ";
        var heartbeatSegment = $" {heartbeat} ";
        var rightStart = Math.Max(0, width - heartbeatSegment.Length);

        var col = WriteSegment(0, 0, title, Math.Min(width, rightStart), Yellow1, headerBg, Decoration.Bold);
        if (col < rightStart)
        {
            col = WriteSegment(col, 0, statusPrefix, rightStart, Grey50, headerBg, Decoration.None);
        }

        if (col < rightStart)
        {
            var statusColor = ResolveStatusColor(status);
            WriteSegment(col, 0, status, rightStart, statusColor, headerBg, Decoration.None);
        }

        if (width >= heartbeatSegment.Length)
        {
            WriteSegment(rightStart, 0, heartbeatSegment, width, Magenta1, headerBg, Decoration.Bold);
        }
        else
        {
            WriteSegment(0, 0, heartbeatSegment, width, Magenta1, headerBg, Decoration.Bold);
        }
    }

    private void RenderMetadataRow(int width)
    {
        var endpoints = $" Hub: {chatHubUrl} | gRPC: {grpcBaseUrl} ";
        const string hint = " Ctrl+Q or /quit to exit ";
        var showHint = width >= hint.Length + 16;
        var rightStart = showHint ? width - hint.Length : width;

        WriteSegment(0, 1, endpoints, rightStart, Grey70, metaBg, Decoration.Dim);
        if (showHint)
        {
            WriteSegment(rightStart, 1, hint, width, Cyan1, metaBg, Decoration.Dim);
        }
    }

    private void ClearRow(int row, int width, JumbeeColor background)
    {
        var blank = new Character(' ', background: background);
        for (var x = 0; x < width; x++)
        {
            consoleBuffer.Write(x, row, in blank);
        }
    }

    private int WriteSegment(
        int startX,
        int row,
        string text,
        int maxX,
        JumbeeColor foreground,
        JumbeeColor background,
        Decoration decoration)
    {
        var x = startX;
        for (var i = 0; i < text.Length && x < maxX; i++)
        {
            var ch = new Character(text[i], foreground: foreground, background: background, decoration: decoration);
            consoleBuffer.Write(x++, row, in ch);
        }

        return x;
    }

    private static JumbeeColor ResolveStatusColor(string currentStatus) =>
        currentStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
            ? Green1
            : currentStatus.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)
              || currentStatus.StartsWith("Connection failed", StringComparison.OrdinalIgnoreCase)
                ? Red1
                : Yellow1;

    #endregion

    #region Fields

    private static readonly JumbeeColor headerBg = Grey15;
    private static readonly JumbeeColor metaBg = Grey11;

    private readonly string username;
    private readonly string chatHubUrl;
    private readonly string grpcBaseUrl;
    private string status = "Initializing...";
    private string heartbeat = "♥ --:--:--";

    #endregion
}
