using System.Buffers.Binary;

namespace DerethSurvivors;

internal static class AceNativeAppearance
{
    private const uint CreateObject = 0xF745, UpdateObject = 0xF7DB, UpdateAppearance = 0xF625;
    private sealed record Model(uint Palette, CharacterPaletteChange[] Colors, CharacterTextureChange[] Textures,
        CharacterPartChange[] Parts);
    private sealed record ModelUpdate(Model Model, ushort Instance, ushort Sequence);

    internal static async Task<AceRiderSnapshot> ImportAsync(AceNativeSession session, uint id, string name,
        CancellationToken cancellationToken)
    {
        string? rosterName = session.Characters.Where(entry => entry.Id == id).Select(entry => entry.Name).FirstOrDefault();
        if (id == 0 || rosterName is null || !rosterName.Equals(name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a character from the account's current roster.");
        string identity = rosterName;

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        object gate = new();
        AceRiderSnapshot? body = null;
        ushort bodyInstance = 0;
        ModelUpdate? newestModel = null;
        session.WorldMessage += Receive;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(24));
            await session.EnterAsync(id, deadline.Token).ConfigureAwait(false);
            await ready.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            await session.CompleteEntryAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            // The initial self description already includes equipped clothing and dyes.
            // Accept an authoritative description refresh sent during world entry too.
            await Task.Delay(TimeSpan.FromMilliseconds(350), deadline.Token).ConfigureAwait(false);
            lock (gate) return body!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The shard did not send your character's appearance in time.");
        }
        finally { session.WorldMessage -= Receive; }

        void Receive(byte[] message)
        {
            if (message.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4)) != id) return;
            if (TryReadBody(message, id, identity, out var actor, out ushort instance, out ushort sequence))
            {
                lock (gate)
                {
                    if (newestModel is { } prior && (short)(instance - prior.Instance) < 0) return;
                    if (newestModel is { } update && update.Instance == instance &&
                        (short)(update.Sequence - sequence) >= 0)
                        actor = Apply(actor!, update.Model);
                    else
                        newestModel = new(new(actor!.Palette, actor.Palettes, actor.Textures, actor.Parts), instance, sequence);
                    body = actor;
                    bodyInstance = instance;
                    ready.TrySetResult();
                }
                return;
            }

            if (TryReadModelUpdate(message) is not { } refresh) return;
            lock (gate)
            {
                if (newestModel is { } recent && ((short)(refresh.Instance - recent.Instance) < 0 ||
                    refresh.Instance == recent.Instance && (short)(refresh.Sequence - recent.Sequence) < 0))
                    return;
                if (body is { } previous && bodyInstance == refresh.Instance)
                    body = Apply(previous, refresh.Model);
                newestModel = refresh;
            }
        }
    }

    private static AceRiderSnapshot Apply(AceRiderSnapshot body, Model model) =>
        body with { Palette = model.Palette, Palettes = model.Colors, Textures = model.Textures, Parts = model.Parts };

    // Narrow adaptation of DerethClient's model-description reader and OpenAC's
    // CreateObject reader (OpenAC MIT license, copyright Erik Nihlén and contributors).
    // Only parse the selected actor's model and physics prefix. Ignore inventory,
    // worn attachment objects and public properties which the race does not use.
    internal static bool TryReadBody(byte[] message, uint expectedId, string name, out AceRiderSnapshot? snapshot,
        out ushort instance, out ushort sequence)
    {
        snapshot = null;
        instance = sequence = 0;
        if (message.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(message) is not (CreateObject or UpdateObject) ||
            BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4)) != expectedId)
            return false;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(message, writable: false));
            Skip(reader, 8);
            Model model = ReadModel(reader);
            uint physics = reader.ReadUInt32();
            Skip(reader, 4); // Current physics state.
            if ((physics & 0x10000) != 0)
            {
                uint movementLength = reader.ReadUInt32();
                if (movementLength != 0) Skip(reader, (long)movementLength + 4);
            }
            else if ((physics & 0x20000) != 0) Skip(reader, 4); // Placement frame.
            if ((physics & 0x8000) != 0) Skip(reader, 32); // Position and quaternion.

            uint motion = (physics & 0x2) != 0 ? reader.ReadUInt32() : 0;
            uint sound = (physics & 0x800) != 0 ? reader.ReadUInt32() : 0;
            if ((physics & 0x1000) != 0) Skip(reader, 4); // Physics script table.
            uint setup = (physics & 1) != 0 ? reader.ReadUInt32() : 0;
            if ((physics & 0x20) != 0) Skip(reader, 8); // Parent attachment.
            if ((physics & 0x40) != 0)
            {
                uint children = reader.ReadUInt32();
                if (children > 1024) return false;
                Skip(reader, children * 8L);
            }
            float? scale = (physics & 0x80) != 0 ? reader.ReadSingle() : null;
            if ((physics & 0x100) != 0) Skip(reader, 4); // Friction.
            if ((physics & 0x200) != 0) Skip(reader, 4); // Elasticity.
            if ((physics & 0x40000) != 0) Skip(reader, 4); // Translucency.
            if ((physics & 0x4) != 0) Skip(reader, 12); // Velocity.
            if ((physics & 0x8) != 0) Skip(reader, 12); // Acceleration.
            if ((physics & 0x10) != 0) Skip(reader, 12); // Angular velocity.
            if ((physics & 0x2000) != 0) Skip(reader, 4); // Default effect.
            if ((physics & 0x4000) != 0) Skip(reader, 4); // Effect intensity.
            Skip(reader, 7 * 2); // Position through force-position sequence numbers.
            sequence = reader.ReadUInt16();
            instance = reader.ReadUInt16();
            if (setup >> 24 != 2 || motion >> 24 != 9 || sound != 0 && sound >> 24 != 0x20 ||
                scale is { } size && (!float.IsFinite(size) || size <= 0))
                return false;
            snapshot = new(1, name, setup, motion, sound, scale, model.Palette, model.Colors, model.Textures, model.Parts);
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException) { return false; }
    }

    private static ModelUpdate? TryReadModelUpdate(byte[] message)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(message) != UpdateAppearance) return null;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(message, writable: false));
            Skip(reader, 8);
            Model model = ReadModel(reader);
            if (reader.BaseStream.Length - reader.BaseStream.Position != 4) return null;
            return new(model, reader.ReadUInt16(), reader.ReadUInt16());
        }
        catch (Exception error) when (error is IOException or InvalidDataException) { return null; }
    }

    private static Model ReadModel(BinaryReader reader)
    {
        if (reader.ReadByte() != 0x11) throw new InvalidDataException("Unknown character model description.");
        int paletteCount = reader.ReadByte(), textureCount = reader.ReadByte(), partCount = reader.ReadByte();
        uint palette = paletteCount > 0 ? ReadPacked(reader, 0x04000000) : 0;
        var colors = new CharacterPaletteChange[paletteCount];
        for (int i = 0; i < colors.Length; i++)
        {
            uint id = ReadPacked(reader, 0x04000000);
            int offset = reader.ReadByte() * 8;
            int count = reader.ReadByte() * 8;
            colors[i] = new(id, offset, count == 0 ? 2048 - offset : count);
        }
        var textures = new List<CharacterTextureChange>(textureCount);
        for (int i = 0; i < textureCount; i++)
        {
            int part = reader.ReadByte();
            uint before = ReadPacked(reader, 0x05000000), after = ReadPacked(reader, 0x05000000);
            if (before != 0 && after != 0) textures.Add(new(part, before, after));
        }
        var parts = new CharacterPartChange[partCount];
        for (int i = 0; i < parts.Length; i++) parts[i] = new(reader.ReadByte(), ReadPacked(reader, 0x01000000));
        Skip(reader, (4 - (reader.BaseStream.Position & 3)) & 3);
        return new(palette, colors, textures.ToArray(), parts);
    }

    private static uint ReadPacked(BinaryReader reader, uint type)
    {
        uint value = reader.ReadUInt16();
        if ((value & 0x8000) != 0) value = ((value & 0x7fff) << 16) | reader.ReadUInt16();
        return value == 0 ? 0 : value | type;
    }

    private static void Skip(BinaryReader reader, long bytes)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < bytes)
            throw new EndOfStreamException("Truncated character description.");
        reader.BaseStream.Position += bytes;
    }
}
