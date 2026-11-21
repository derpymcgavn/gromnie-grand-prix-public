using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DerethSurvivors;

internal sealed record AceNativeCharacter(uint Id, string Name);

internal static class AceNativePackets
{
    internal const int HeaderSize = 20, FragmentSize = 16, ChunkSize = 400;
    internal const uint EncryptedChecksum = 0x2, Fragments = 0x4, Resend = 0x1000, DeniedResend = 0x2000,
        Ack = 0x4000, Disconnect = 0x8000, Login = 0x10000, ConnectRequest = 0x40000, ConnectResponse = 0x80000;
    private const uint ChecksumSeed = 0xbadd70dd;
    private static readonly Encoding ServerText = GetServerText();

    private static Encoding GetServerText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    internal static byte[] BuildLogin(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Length > 50 || string.IsNullOrEmpty(password) || password.Length > 254 ||
            account.Any(c => c < ' ' || c > '~') || password.Any(c => c < ' ' || c > '~'))
            throw new ArgumentException("Enter an ACE account name and password using printable ASCII characters.");

        using var stream = new MemoryStream(384);
        using var writer = new BinaryWriter(stream);
        WriteText(writer, "1802");
        long start = stream.Position;
        writer.Write(0u);
        writer.Write(2u);
        writer.Write(0u);
        writer.Write((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        WriteText(writer, account);
        WriteText(writer, "");
        writer.Write((uint)password.Length + 1);
        writer.Write((byte)password.Length);
        foreach (char c in password) writer.Write((byte)c);
        long length = stream.Length;
        stream.Position = start;
        writer.Write((uint)(length - start));
        try { return Build(Login, stream.GetBuffer().AsSpan(0, (int)length), iteration: 1); }
        finally { CryptographicOperations.ZeroMemory(stream.GetBuffer()); }
    }

    internal static byte[] Build(uint flags, ReadOnlySpan<byte> body, ushort client = 0, uint sequence = 0,
        ushort iteration = 1, uint? checksumKey = null)
    {
        var packet = new byte[HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), ChecksumSeed);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12), client);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), checked((ushort)body.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(18), iteration);
        body.CopyTo(packet.AsSpan(HeaderSize));
        uint payload = checksumKey is { } key ? (Hash(body[..FragmentSize]) + Hash(body[FragmentSize..])) ^ key : Hash(body);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), unchecked(Hash(packet.AsSpan(0, HeaderSize)) + payload));
        return packet;
    }

    internal static uint HeaderHash(ReadOnlySpan<byte> packet)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        packet[..HeaderSize].CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], ChecksumSeed);
        return Hash(header);
    }

    internal static uint Hash(ReadOnlySpan<byte> bytes)
    {
        uint hash = (uint)bytes.Length << 16;
        int p = 0;
        unchecked
        {
            for (; p + 4 <= bytes.Length; p += 4) hash += BinaryPrimitives.ReadUInt32LittleEndian(bytes[p..]);
            for (int shift = 24; p < bytes.Length; p++, shift -= 8) hash += (uint)bytes[p] << shift;
        }
        return hash;
    }

    internal static uint U32(ReadOnlySpan<byte> bytes, int at = 0) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    internal static ushort U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);

    internal static bool IsConnectRequest(ReadOnlySpan<byte> packet) => packet.Length == 52 && U16(packet, 16) == 32 &&
        U32(packet, 4) == ConnectRequest && U32(packet, 8) == unchecked(HeaderHash(packet) + Hash(packet[HeaderSize..]));

    internal static byte[] Body(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }

    internal static void WriteText(BinaryWriter writer, string text)
    {
        writer.Write(checked((ushort)text.Length));
        foreach (char c in text) writer.Write((byte)c);
        writer.Write(new byte[(-(text.Length + 2)) & 3]);
    }

    internal static (string Account, AceNativeCharacter[] Characters) ReadRoster(byte[] message)
    {
        using var reader = new BinaryReader(new MemoryStream(message));
        reader.ReadUInt32(); // Opcode.
        reader.ReadUInt32(); // Status.
        var characters = ReadCharacters(available: true);
        ReadCharacters(available: false);
        reader.ReadUInt32(); // Slot count.
        string account = ReadText(reader);
        reader.ReadUInt32(); // Chat enabled.
        reader.ReadUInt32(); // Expansion enabled.
        return (account, characters.ToArray());

        List<AceNativeCharacter> ReadCharacters(bool available)
        {
            int count = reader.ReadInt32();
            if (count is < 0 or > 256) throw new InvalidDataException("Invalid ACE character list.");
            var seen = new HashSet<uint>();
            var entries = new List<AceNativeCharacter>(available ? count : 0);
            for (int index = 0; index < count; index++)
            {
                uint id = reader.ReadUInt32();
                string name = ReadText(reader);
                uint deletedAfter = reader.ReadUInt32();
                if (available && deletedAfter == 0 && id != 0 && name.Length is > 0 and <= 64 &&
                    !name.Any(char.IsControl) && seen.Add(id)) entries.Add(new(id, name));
            }
            return entries;
        }
    }

    private static string ReadText(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        byte[] bytes = reader.ReadBytes(length);
        int padding = (-(length + 2)) & 3;
        if (bytes.Length != length || reader.ReadBytes(padding).Length != padding)
            throw new InvalidDataException("Truncated ACE character list.");
        return ServerText.GetString(bytes);
    }

    internal sealed class Decoder(uint seed)
    {
        private readonly AceIsaacRandom cipher = new(seed);
        private readonly HashSet<uint> tokens = [];
        private readonly HashSet<uint> sequences = [];
        private readonly Dictionary<(ulong Fragment, ushort Queue), byte[][]> incomplete = [];
        private uint highest;

        internal uint Confirmed { get; private set; }
        internal List<uint> Retransmit { get; } = [];

        internal bool Decode(byte[] packet, List<byte[]> messages)
        {
            Confirmed = 0;
            Retransmit.Clear();
            if (packet.Length < HeaderSize || packet.Length != HeaderSize + U16(packet, 16)) return false;
            ReadOnlySpan<byte> bytes = packet;
            uint flags = U32(bytes, 4);
            if ((flags & ~0x0f00f007u) != 0) return false;
            using var header = new BinaryReader(new MemoryStream(packet, HeaderSize, packet.Length - HeaderSize));
            foreach (uint flag in new[] { Resend, DeniedResend })
                if ((flags & flag) != 0)
                {
                    uint count = header.ReadUInt32();
                    if (count > 256) throw new InvalidDataException("Invalid ACE retransmission count.");
                    for (uint i = 0; i < count; i++)
                    {
                        uint number = header.ReadUInt32();
                        if (flag == Resend) Retransmit.Add(number);
                    }
                }
            if ((flags & Ack) != 0) Confirmed = header.ReadUInt32();
            int optional = ((flags & 0x01000000) != 0 ? 8 : 0) + ((flags & 0x02000000) != 0 ? 4 : 0) +
                ((flags & 0x04000000) != 0 ? 8 : 0) + ((flags & 0x08000000) != 0 ? 6 : 0);
            int start = HeaderSize + checked((int)header.BaseStream.Position) + optional;
            if (start > bytes.Length) return false;
            uint hash = Hash(bytes[HeaderSize..start]);
            if ((flags & Fragments) != 0)
            {
                for (int at = start; at < bytes.Length;)
                {
                    if (bytes.Length - at < FragmentSize) return false;
                    ushort count = U16(bytes, at + 8), size = U16(bytes, at + 10), index = U16(bytes, at + 12);
                    if (count is 0 or > 256 || size < FragmentSize || size > bytes.Length - at || index >= count) return false;
                    hash = unchecked(hash + Hash(bytes.Slice(at, FragmentSize)) + Hash(bytes.Slice(at + FragmentSize, size - FragmentSize)));
                    at += size;
                }
            }
            else if (start != bytes.Length) return false;

            uint payload = unchecked(U32(bytes, 8) - HeaderHash(bytes));
            if ((flags & EncryptedChecksum) != 0)
            {
                if (tokens.Count == 0)
                    for (int i = 0; i < 256; i++) tokens.Add(cipher.Next());
                if (!tokens.Remove(payload ^ hash)) return false;
                tokens.Add(cipher.Next());
            }
            else if (payload != hash) return false;
            if ((flags & Fragments) == 0) return true;

            uint sequence = U32(bytes);
            if (!sequences.Add(sequence)) return true;
            highest = Math.Max(highest, sequence);
            if (highest > 2048) sequences.RemoveWhere(number => number < highest - 2048);
            for (int at = start; at < bytes.Length;)
            {
                ushort count = U16(bytes, at + 8), size = U16(bytes, at + 10), index = U16(bytes, at + 12);
                var identity = (BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]), U16(bytes, at + 14));
                if (!incomplete.TryGetValue(identity, out var slots))
                {
                    if (incomplete.Count >= 64) throw new InvalidDataException("Too many incomplete ACE messages.");
                    incomplete[identity] = slots = new byte[count][];
                }
                if (slots.Length != count) throw new InvalidDataException("Inconsistent ACE message fragments.");
                slots[index] = bytes.Slice(at + FragmentSize, size - FragmentSize).ToArray();
                if (slots.All(piece => piece is not null))
                {
                    messages.Add(slots.SelectMany(piece => piece).ToArray());
                    incomplete.Remove(identity);
                }
                at += size;
            }
            return true;
        }
    }
}
