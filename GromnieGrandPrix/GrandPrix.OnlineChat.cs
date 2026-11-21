using Godot;
using GromnieOnline;
using System.Text.Json;

public partial class GrandPrix
{
    private const int ChatHistoryLimit = 40;
    private readonly Queue<(string Name, string Text)> chatHistory = new();
    private RichTextLabel onlineChatLog = null!;
    private LineEdit onlineChatInput = null!;
    private Button onlineChatSend = null!;
    private ulong nextChatSend;

    private void MakeOnlineChat()
    {
        Text(onlineRoom, "LOBBY CHAT", 624, 193, 370, 16, Mint);
        onlineChatLog = new RichTextLabel
        {
            Position = new(620, 224), Size = new(378, 265), Theme = ThemeForUi(),
            ScrollFollowing = true, AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        onlineChatLog.AddThemeStyleboxOverride("normal", Surface("20352b", "687f56", 8));
        onlineChatLog.AddThemeFontSizeOverride("normal_font_size", 15);
        onlineChatLog.AddThemeColorOverride("default_color", Cream);
        onlineChatLog.AddThemeConstantOverride("line_separation", 4);
        onlineRoom.AddChild(onlineChatLog);
        onlineChatInput = OnlineField(onlineRoom, 506, "Message the lobby", Wire.ChatMaxLength);
        onlineChatInput.Position = new Vector2(620, 506);
        onlineChatInput.Size = new Vector2(285, 39);
        onlineChatInput.TextSubmitted += _ => SendLobbyChat();
        onlineChatSend = Action(onlineRoom, "SEND", 913, 506, 85, 39, SendLobbyChat);
    }

    private void RefreshOnlineChat(RelayRoster room)
    {
        bool available = room.ChatEnabled && !room.Locked && !OnlineRace && !onlineStartPending;
        onlineChatInput.Editable = available;
        onlineChatSend.Disabled = !available;
        onlineChatInput.PlaceholderText = room.ChatEnabled ? "Message the lobby" : "This relay needs a chat update";
    }

    private void SendLobbyChat()
    {
        if (!onlineChatInput.Editable) return;
        if (!Wire.TryChatText(onlineChatInput.Text, out string message))
        {
            if (onlineChatInput.Text.Trim().Length > 0)
                onlineStatus.Text = "Enter a short, single-line message.";
            return;
        }
        ulong now = Time.GetTicksMsec();
        if (now < nextChatSend)
        {
            onlineStatus.Text = "Wait a moment before sending again.";
            return;
        }
        nextChatSend = now + Wire.ChatIntervalMs + 50;
        onlineChatInput.Clear();
        SendOnline(Wire.Packet("chat", message));
    }

    private void ReceiveLobbyChat(Wire wire)
    {
        if (wire.Data is not { ValueKind: JsonValueKind.String } data ||
            !Wire.TryChatText(data.GetString(), out string message) ||
            lobby?.Members.FirstOrDefault(member => member.Id == wire.From) is not { } sender) return;
        string name = OnlineRiderName(DecodeLoadout(sender.Profile));
        chatHistory.Enqueue((name, message));
        if (chatHistory.Count > ChatHistoryLimit) chatHistory.Dequeue();
        var scroll = onlineChatLog.GetVScrollBar();
        double position = scroll.Value;
        bool follow = position >= scroll.MaxValue - scroll.Page - 2;
        onlineChatLog.Clear();
        foreach (var line in chatHistory)
        {
            onlineChatLog.PushColor(Mint);
            onlineChatLog.AddText(line.Name + "  ");
            onlineChatLog.Pop();
            onlineChatLog.AddText(line.Text);
            onlineChatLog.Newline();
        }
        if (follow) onlineChatLog.ScrollToLine(onlineChatLog.GetLineCount() - 1);
        else scroll.SetDeferred("value", position);
    }

    private void ClearLobbyChat()
    {
        chatHistory.Clear();
        nextChatSend = 0;
        onlineChatLog.Clear();
        onlineChatInput.Clear();
    }
}
