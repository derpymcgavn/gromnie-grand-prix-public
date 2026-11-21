using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using static DerethSurvivors.AceNativePackets;

namespace DerethSurvivors;

// A short stock-client session scoped to one roster and one appearance import.
internal sealed class AceNativeSession : IAsyncDisposable
{
    private readonly UdpClient primary = new(AddressFamily.InterNetwork), secondary = new(AddressFamily.InterNetwork);
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1);
    private readonly Dictionary<uint, byte[]> outgoing = [];
    private readonly HashSet<uint> received = [];
    private readonly List<byte[]> messages = [];
    private readonly TaskCompletionSource<(string Account, AceNativeCharacter[] Characters)> roster =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource datAccepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource worldAccepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource loggedOff = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object closeLock = new();
    private Task? receiveLoop, closeTask;
    private Decoder decoder = null!;
    private AceIsaacRandom cipher = null!;
    private byte[] cookieReply = [];
    private string account = "";
    private ushort clientId;
    private uint sequence = 1, fragment, contiguous = 1, highest = 1, selected;
    private int entering, worldReady;

    internal IReadOnlyList<AceNativeCharacter> Characters { get; private set; } = [];
    internal event Action<byte[]>? WorldMessage;

    internal static async Task<AceNativeSession> ConnectAsync(string host, int port, string account, string password,
        byte[] datInfo, CancellationToken token)
    {
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown || port is < 1 or > 65534)
            throw new ArgumentException("Enter an ACE hostname and a port between 1 and 65534.");
        if (datInfo.Length is < 4 or > 25600 || U32(datInfo) != 0xF7E6)
            throw new ArgumentException("The local DAT version response is unavailable.");
        byte[] login = BuildLogin(account, password);
        password = "";
        var session = new AceNativeSession();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var address = (await Dns.GetHostAddressesAsync(host, deadline.Token).ConfigureAwait(false))
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new IOException("The ACE address did not resolve to an IPv4 server.");
            session.primary.Connect(address, port);
            session.secondary.Connect(address, port + 1);
            try { await session.primary.SendAsync(login, deadline.Token).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(login); }

            byte[] handshake;
            do { handshake = (await session.primary.ReceiveAsync(deadline.Token).ConfigureAwait(false)).Buffer; }
            while (!IsConnectRequest(handshake));
            session.clientId = checked((ushort)U32(handshake, 36));
            if (session.clientId == 0) throw new InvalidDataException("The ACE server did not assign a session.");
            session.decoder = new Decoder(U32(handshake, 40));
            session.cipher = new AceIsaacRandom(U32(handshake, 44));
            session.cookieReply = Build(ConnectResponse, handshake.AsSpan(28, 8), session.clientId);
            await session.secondary.SendAsync(session.cookieReply, deadline.Token).ConfigureAwait(false);
            session.receiveLoop = session.ReceiveAsync(datInfo);
            var list = await session.roster.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (!list.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ACE roster belongs to a different account.");
            await session.datAccepted.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            session.account = list.Account;
            session.Characters = list.Characters;
            return session;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException("The ACE server did not finish connecting. Check the server and port.");
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(login); }
    }

    internal async Task EnterAsync(uint id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Characters.Any(character => character.Id == id) || Interlocked.CompareExchange(ref entering, 1, 0) != 0)
            throw new InvalidOperationException("Select one available character from this ACE account.");
        await SendMessageAsync(Body(writer => writer.Write(0xF7C8u))).ConfigureAwait(false);
        await worldAccepted.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        Volatile.Write(ref selected, id);
        await SendMessageAsync(Body(writer =>
        {
            writer.Write(0xF657u);
            writer.Write(id);
            WriteText(writer, account);
        })).ConfigureAwait(false);
    }

    internal async Task CompleteEntryAsync()
    {
        if (Volatile.Read(ref selected) == 0) throw new InvalidOperationException("No ACE character has been selected.");
        if (Interlocked.CompareExchange(ref worldReady, 1, 0) != 0) return;
        try
        {
            await SendMessageAsync(Body(writer =>
            {
                writer.Write(0xF7B1u);
                writer.Write(1u);
                writer.Write(0xA1u);
            }), queue: 5).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref worldReady, 0);
            throw;
        }
    }

    private async Task SendMessageAsync(byte[] message, ushort queue = 9)
    {
        var token = lifetime.Token;
        await sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ushort count = checked((ushort)((message.Length + ChunkSize - 1) / ChunkSize));
            if (outgoing.Count + count > 512) throw new IOException("The ACE server is not acknowledging the connection.");
            uint fragmentId = ++fragment;
            for (ushort index = 0; index < count; index++)
            {
                int start = index * ChunkSize, length = Math.Min(ChunkSize, message.Length - start);
                var body = Body(writer =>
                {
                    writer.Write(fragmentId);
                    writer.Write(0u);
                    writer.Write(count);
                    writer.Write((ushort)(FragmentSize + length));
                    writer.Write(index);
                    writer.Write(queue);
                    writer.Write(message, start, length);
                });
                byte[] packet = Build(Fragments | EncryptedChecksum, body, clientId, ++sequence, checksumKey: cipher.Next());
                outgoing.Add(sequence, packet);
                await primary.SendAsync(packet, token).ConfigureAwait(false);
            }
        }
        finally { sendLock.Release(); }
    }

    private async Task ReceiveAsync(byte[] datInfo)
    {
        var token = lifetime.Token;
        var first = primary.ReceiveAsync(token).AsTask();
        var second = secondary.ReceiveAsync(token).AsTask();
        var tick = Task.Delay(1000, token);
        bool sawSecond = false, answeredDat = false;
        long lastPacket = Environment.TickCount64;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var next = await Task.WhenAny(first, second, tick).ConfigureAwait(false);
                if (next == tick)
                {
                    token.ThrowIfCancellationRequested();
                    if (Environment.TickCount64 - lastPacket > 30000) throw new IOException("The ACE server stopped responding.");
                    if (!sawSecond) await secondary.SendAsync(cookieReply, token).ConfigureAwait(false);
                    await MaintainTransport(token).ConfigureAwait(false);
                    tick = Task.Delay(1000, token);
                    continue;
                }
                bool fromSecondary = next == second;
                byte[] packet;
                if (!fromSecondary)
                {
                    packet = (await first.ConfigureAwait(false)).Buffer;
                    first = primary.ReceiveAsync(token).AsTask();
                }
                else
                {
                    packet = (await second.ConfigureAwait(false)).Buffer;
                    second = secondary.ReceiveAsync(token).AsTask();
                }

                messages.Clear();
                if (!decoder.Decode(packet, messages)) continue;
                sawSecond |= fromSecondary;
                lastPacket = Environment.TickCount64;
                uint flags = U32(packet, 4);
                if ((flags & Disconnect) != 0) throw new IOException("The ACE server closed the session.");
                await NoteTransport(packet, flags, token).ConfigureAwait(false);
                foreach (byte[] message in messages)
                {
                    if (message.Length < 4) continue;
                    switch (U32(message))
                    {
                        case 0xF658: roster.TrySetResult(ReadRoster(message)); break;
                        case 0xF659: throw new IOException(RejectionMessage(message));
                        case 0xF7DC: throw new IOException("The ACE server refused the login. Check the account and server status.");
                        case 0xF7C1: throw new IOException("The ACE server rejected this account.");
                        case 0xF7E5 when !answeredDat:
                            answeredDat = true;
                            await SendMessageAsync(datInfo, queue: 7).ConfigureAwait(false);
                            break;
                        case 0xF7E7: throw new IOException("The ACE server requires newer DATs. Update the DATs in your regular AC client.");
                        case 0xF7EA when answeredDat: datAccepted.TrySetResult(); break;
                        case 0xF7DF: worldAccepted.TrySetResult(); break;
                        case 0xF653 when message.Length == 4: loggedOff.TrySetResult(); break;
                    }
                    if (Volatile.Read(ref selected) != 0) WorldMessage?.Invoke(message);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            roster.TrySetException(error);
            datAccepted.TrySetException(error);
            worldAccepted.TrySetException(error);
            loggedOff.TrySetException(error);
        }
        finally { lifetime.Cancel(); }
    }

    private static string RejectionMessage(byte[] message) => message.Length < 8 ? "The ACE server rejected character entry." :
        U32(message, 4) switch
        {
            1 or 8 or 13 or 16 => "The ACE account or character is already in use. Log out in AC and try again.",
            9 or 10 => "The ACE server did not accept the account or password.",
            21 => "The ACE server is full or closed.",
            _ => "The ACE server rejected character entry."
        };

    private async Task NoteTransport(byte[] packet, uint flags, CancellationToken token)
    {
        await sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (decoder.Confirmed > 0)
                foreach (uint done in outgoing.Keys.Where(key => key <= decoder.Confirmed).ToArray()) outgoing.Remove(done);
            foreach (uint requested in decoder.Retransmit)
                if (outgoing.TryGetValue(requested, out var cached)) await primary.SendAsync(cached, token).ConfigureAwait(false);
        }
        finally { sendLock.Release(); }

        uint number = U32(packet);
        if (flags == Ack || (flags & Resend) != 0 || number <= contiguous) return;
        if (number - contiguous > 2048) throw new IOException("The ACE connection lost too many packets.");
        highest = Math.Max(highest, number);
        received.Add(number);
        while (received.Remove(contiguous + 1)) contiguous++;
    }

    private async Task MaintainTransport(CancellationToken token)
    {
        await sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await primary.SendAsync(Build(Ack, Body(writer => writer.Write(contiguous)), clientId, sequence), token).ConfigureAwait(false);
            if (highest > contiguous)
            {
                uint[] missing = Enumerable.Range(1, (int)Math.Min(32, highest - contiguous))
                    .Select(offset => contiguous + (uint)offset).Where(number => !received.Contains(number)).ToArray();
                if (missing.Length > 0)
                    await primary.SendAsync(Build(Resend, Body(writer =>
                    {
                        writer.Write(missing.Length);
                        foreach (uint number in missing) writer.Write(number);
                    }), clientId, sequence), token).ConfigureAwait(false);
            }
            foreach (byte[] packet in outgoing.Values) await primary.SendAsync(packet, token).ConfigureAwait(false);
        }
        finally { sendLock.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (closeLock) return new ValueTask(closeTask ??= CloseAsync());
    }

    private async Task CloseAsync()
    {
        uint character = Volatile.Read(ref selected);
        if (character != 0 && Volatile.Read(ref worldReady) != 0 && !lifetime.IsCancellationRequested)
        {
            try
            {
                await SendMessageAsync(Body(writer => { writer.Write(0xF653u); writer.Write(character); })).ConfigureAwait(false);
                await loggedOff.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or TimeoutException) { }
        }
        lifetime.Cancel();
        try
        {
            if (receiveLoop is not null) await receiveLoop.ConfigureAwait(false);
            await sendLock.WaitAsync().ConfigureAwait(false);
            if (clientId != 0)
            {
                try { await primary.SendAsync(Build(Disconnect, [], clientId)).ConfigureAwait(false); }
                catch (SocketException) { }
            }
        }
        finally
        {
            primary.Dispose();
            secondary.Dispose();
            sendLock.Dispose();
            lifetime.Dispose();
        }
    }
}
