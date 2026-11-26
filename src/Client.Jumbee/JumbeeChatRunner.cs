using ConsoleGUI.Input;
using Jumbee.Console;
using static global::Jumbee.Console.Color;

namespace Client.Jumbee;

/// <summary>
/// Hosts the interactive Jumbee.Console retained-mode UI and marshals background network updates onto the UI thread.
/// </summary>
internal sealed class JumbeeChatRunner
{
    #region Constructors

    public JumbeeChatRunner(
        string username,
        string chatHubUrl,
        string grpcBaseUrl,
        Func<Action<ChatMessage>, Action<string>, Action<string, long>, ChatClientSession> sessionFactory)
    {
        this.username = username;
        this.chatHubUrl = chatHubUrl;
        this.grpcBaseUrl = grpcBaseUrl;
        this.sessionFactory = sessionFactory;
    }

    #endregion

    #region Methods

    public async Task RunAsync()
    {
        var headerBar = new ChatHeaderBar(username, chatHubUrl, grpcBaseUrl);

        var messageLog = new Log
        {
            MaxEntries = 1000
        }
            .WithRoundedBorder(Cyan1)
            .WithTitle($"Messages ({username})", TitlePos.TopLeft, TitleBorderStyle.Inline, TitleColorStyle.Normal);

        var chatPrompt = new ChatPrompt("Type a message and press Enter... (/quit or /exit to leave)")
        {
            Prompt = ">"
        };
        chatPrompt.WithSuggestions(["/quit", "/exit"]);
        chatPrompt
            .WithRoundedBorder(Yellow)
            .WithTitle("Input", TitlePos.TopLeft, TitleBorderStyle.Inline, TitleColorStyle.Normal);

        var previousInterceptor = chatPrompt.Input.KeyInterceptor;
        chatPrompt.Input.KeyInterceptor = inputEvent =>
        {
            if (previousInterceptor?.Invoke(inputEvent) == true)
            {
                return true;
            }

            return TryForwardScrollKey(messageLog, chatPrompt.Input, inputEvent);
        };

        var footer = new Footer(
            new FooterHint("Enter", "Send"),
            new FooterHint("Ctrl+Up/Dn", "Focus"),
            new FooterHint("PgUp/PgDn", "Scroll"),
            new FooterHint("End", "Latest"),
            new FooterHint("Ctrl+Q", "Quit"));

        var inputArea = new Boundary(chatPrompt, height: 3);
        var chatArea = new DockPanel(DockedControlPlacement.Bottom, inputArea, messageLog);
        var bodyArea = new DockPanel(DockedControlPlacement.Top, headerBar, chatArea);
        var rootLayout = new DockPanel(DockedControlPlacement.Bottom, footer, bodyArea);

        var uiActive = 0;

        void InvokeOnUi(Action action)
        {
            if (Volatile.Read(ref uiActive) == 0)
            {
                return;
            }

            try
            {
                UI.Invoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        void RequestExit()
        {
            if (Interlocked.Exchange(ref uiActive, 0) == 1 || UI.IsRunning)
            {
                UI.Stop();
            }
        }

        await using var session = sessionFactory(
            message =>
            {
                if (Volatile.Read(ref uiActive) == 0)
                {
                    return;
                }

                messageLog.Write(message.ToMarkup());
            },
            status => InvokeOnUi(() => headerBar.UpdateStatus(status)),
            (uptime, timestampUtc) => InvokeOnUi(() => headerBar.UpdateHeartbeat(uptime, timestampUtc)));

        chatPrompt.Submitted += (_, rawText) =>
        {
            chatPrompt.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                UI.SetFocus(chatPrompt);
                return;
            }

            var trimmed = rawText.Trim();
            if (string.Equals(trimmed, "/quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "/exit", StringComparison.OrdinalIgnoreCase))
            {
                RequestExit();
                return;
            }

            messageLog.ScrollToBottom();
            UI.SetFocus(chatPrompt);
            _ = Task.Run(() => session.SendMessageAsync(rawText));
        };

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            RequestExit();
        };

        Console.CancelKeyPress += cancelHandler;

        var ctrlCKey = UI.HotKeys.Ctrl(ConsoleKey.C);
        UI.RegisterHotKey(UI.HotKeys.CtrlQ, RequestExit);
        UI.RegisterHotKey(ctrlCKey, RequestExit);
        UI.SetFocus(chatPrompt);

        var (initialWidth, initialHeight) = GetSafeTerminalDimensions();

        try
        {
            Volatile.Write(ref uiActive, 1);
            var uiTask = UI.Start(
                rootLayout,
                width: initialWidth,
                height: initialHeight,
                fps: 20,
                useAlternateScreen: true);

            UI.Post(() => _ = Task.Run(() => session.StartAsync()));

            await uiTask;
        }
        finally
        {
            Volatile.Write(ref uiActive, 0);
            Console.CancelKeyPress -= cancelHandler;
            UI.UnregisterHotKey(ctrlCKey);
        }

        await session.StopAsync();
    }

    private static bool TryForwardScrollKey(Log messageLog, TextInput input, InputEvent inputEvent)
    {
        var keyInfo = inputEvent.Key;
        if (keyInfo.Key == ConsoleKey.End &&
            (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) || input.Text.Length == 0))
        {
            messageLog.ScrollToBottom();
            return true;
        }

        if (keyInfo.Key is not (ConsoleKey.PageUp or ConsoleKey.PageDown or ConsoleKey.UpArrow or ConsoleKey.DownArrow))
        {
            return false;
        }

        messageLog.OnInput(new UI.InputEventArgs(inputEvent));
        return true;
    }

    private static (int Width, int Height) GetSafeTerminalDimensions()
    {
        try
        {
            var width = Console.WindowWidth > 0 ? Math.Max(40, Console.WindowWidth) : 110;
            var height = Console.WindowHeight > 0 ? Math.Max(12, Console.WindowHeight) : 25;
            return (width, height);
        }
        catch (IOException)
        {
            return (110, 25);
        }
    }

    #endregion

    #region Fields

    private readonly string username;
    private readonly string chatHubUrl;
    private readonly string grpcBaseUrl;
    private readonly Func<Action<ChatMessage>, Action<string>, Action<string, long>, ChatClientSession> sessionFactory;

    #endregion
}
