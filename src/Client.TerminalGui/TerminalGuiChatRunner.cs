using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Client.TerminalGui;

/// <summary>
/// Hosts the interactive Terminal.Gui widget tree and marshals background network updates onto the UI thread.
/// </summary>
internal sealed class TerminalGuiChatRunner
{
    #region Constructors

    public TerminalGuiChatRunner(
        string username,
        string chatHubUrl,
        string grpcBaseUrl,
        Func<Action<string>, Action<string>, Action<string, long>, ChatClientSession> sessionFactory)
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
        using var app = Application.Create();
        app.Init();

        using var window = new Window
        {
            Title = $"Chatterbox - Terminal.Gui ({username})",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var heartbeatLabel = new Label
        {
            Text = "♥ --:--:--",
            X = Pos.AnchorEnd(),
            Y = 0,
            Height = 1
        };

        var statusLabel = new Label
        {
            Text = FormatStatusText("Initializing..."),
            X = 1,
            Y = 0,
            Width = Dim.Fill(1, heartbeatLabel),
            Height = 1
        };

        ObservableCollection<string> messages = [];
        var listView = new ListView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            CanFocus = true,
            BorderStyle = LineStyle.Single
        };
        listView.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
        listView.SetSource(messages);

        var promptLabel = new Label
        {
            Text = ">",
            X = 0,
            Y = Pos.AnchorEnd(),
            Width = 1,
            Height = 1
        };

        var sendButton = new Button
        {
            Text = "Send",
            X = Pos.AnchorEnd(),
            Y = Pos.AnchorEnd()
        };

        var inputField = new TextField
        {
            X = 2,
            Y = Pos.AnchorEnd(),
            Width = Dim.Fill(1, sendButton),
            Height = 1
        };

        window.Add(statusLabel, heartbeatLabel, listView, promptLabel, inputField, sendButton);

        var uiActive = 0;

        void InvokeOnUi(Action action)
        {
            if (Volatile.Read(ref uiActive) == 0)
            {
                return;
            }

            try
            {
                app.Invoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        await using var session = sessionFactory(
            line => InvokeOnUi(() =>
            {
                messages.Add(line);
                listView.SelectedItem = messages.Count - 1;
                listView.EnsureSelectedItemVisible();
            }),
            status => InvokeOnUi(() => statusLabel.Text = FormatStatusText(status)),
            (uptime, timestampUtc) => InvokeOnUi(() =>
            {
                var localTime = DateTimeOffset.FromUnixTimeSeconds(timestampUtc).ToLocalTime();
                heartbeatLabel.Text = $"♥ {uptime} ({localTime:HH:mm:ss})";
            }));

        void SubmitInput(object? sender, CommandEventArgs e)
        {
            e.Handled = true;
            var rawText = inputField.Text;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                inputField.Text = string.Empty;
                inputField.SetFocus();
                return;
            }

            var trimmed = rawText.Trim();
            inputField.Text = string.Empty;
            inputField.SetFocus();

            if (string.Equals(trimmed, "/quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "/exit", StringComparison.OrdinalIgnoreCase))
            {
                app.RequestStop();
                return;
            }

            _ = Task.Run(() => session.SendMessageAsync(rawText));
        }

        inputField.Accepting += SubmitInput;
        sendButton.Accepting += SubmitInput;

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key != Key.Q.WithCtrl)
            {
                return;
            }

            key.Handled = true;
            app.RequestStop();
        };

        var started = 0;
        window.IsRunningChanged += (_, args) =>
        {
            if (!args.Value)
            {
                Volatile.Write(ref uiActive, 0);
                return;
            }

            Volatile.Write(ref uiActive, 1);
            inputField.SetFocus();

            if (Interlocked.Exchange(ref started, 1) == 0)
            {
                _ = Task.Run(() => session.StartAsync());
            }
        };

        app.Run(window);
        Volatile.Write(ref uiActive, 0);

        await session.StopAsync();
    }

    private string FormatStatusText(string status) =>
        $"Status: {status} | Hub: {chatHubUrl} | gRPC: {grpcBaseUrl} | Ctrl+Q or /quit to exit";

    #endregion

    #region Fields

    private readonly string username;
    private readonly string chatHubUrl;
    private readonly string grpcBaseUrl;
    private readonly Func<Action<string>, Action<string>, Action<string, long>, ChatClientSession> sessionFactory;

    #endregion
}
