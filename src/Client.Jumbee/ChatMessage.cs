using Jumbee.Console;

namespace Client.Jumbee;

/// <summary>
/// Classifies messages displayed in the Jumbee chat log.
/// </summary>
internal enum ChatMessageKind
{
    Chat,
    Server,
    System
}

/// <summary>
/// Represents a single chat, server notice, or system status entry with both plain-text and Jumbee markup formatting.
/// </summary>
internal sealed record ChatMessage(
    string Timestamp,
    ChatMessageKind Kind,
    string Text,
    string? Sender = null,
    string? Framework = null)
{
    #region Methods

    public string ToPlainText() => Kind switch
    {
        ChatMessageKind.Chat => $"[{Timestamp}] [{Framework}] {Sender}: {Text}",
        ChatMessageKind.Server => $"[{Timestamp}] [SERVER] {Text}",
        _ => $"[{Timestamp}] [SYSTEM] {Text}"
    };

    public string ToMarkup() => Kind switch
    {
        ChatMessageKind.Chat => FormatChatMarkup(Timestamp, Framework ?? "Unknown", Sender ?? "Unknown", Text),
        ChatMessageKind.Server => $"[grey]{Timestamp}[/] [bold magenta][[SERVER]][/] {Style.EscapeMarkup(Text)}",
        _ => $"[grey]{Timestamp}[/] [bold yellow][[SYSTEM]][/] [dim]{Style.EscapeMarkup(Text)}[/]"
    };

    private static string FormatChatMarkup(string timestamp, string framework, string sender, string text)
    {
        var frameworkTag = framework switch
        {
            "Spectre" => "[green]Spectre[/]",
            "TerminalGui" => "[blue]TerminalGui[/]",
            "Jumbee" => "[yellow]Jumbee[/]",
            _ => $"[magenta]{Style.EscapeMarkup(framework)}[/]"
        };

        return $"[grey]{timestamp}[/] [[{frameworkTag}]] [bold cyan]{Style.EscapeMarkup(sender)}[/]: {Style.EscapeMarkup(text)}";
    }

    #endregion
}
