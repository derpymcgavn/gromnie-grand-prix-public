// Adapted from OpenAC AcDream.Core.Net/Cryptography/IsaacRandom.cs.
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors. MIT, see THIRD_PARTY_NOTICES.txt.
namespace DerethSurvivors;

internal sealed class AceIsaacRandom
{
    private const int StateSize = 256;
    private readonly uint[] state = new uint[StateSize], result = new uint[StateSize];
    private uint a, b, c;
    private int offset = StateSize - 1;

    public AceIsaacRandom(uint seed)
    {
        Span<uint> mix = stackalloc uint[8];
        mix.Fill(0x9E3779B9u);
        for (int pass = 0; pass < 4; pass++) Mix(mix);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int start = 0; start < StateSize; start += 8)
            {
                if (pass == 1)
                    for (int index = 0; index < 8; index++) mix[index] += state[start + index];
                Mix(mix);
                mix.CopyTo(state.AsSpan(start, 8));
            }
        }
        a = b = c = seed;
        Scramble();
    }

    public uint Next()
    {
        uint value = result[offset--];
        if (offset >= 0) return value;
        Scramble();
        offset = StateSize - 1;
        return value;
    }

    private static void Mix(Span<uint> s)
    {
        s[0] ^= s[1] << 11; s[3] += s[0]; s[1] += s[2];
        s[1] ^= s[2] >> 2; s[4] += s[1]; s[2] += s[3];
        s[2] ^= s[3] << 8; s[5] += s[2]; s[3] += s[4];
        s[3] ^= s[4] >> 16; s[6] += s[3]; s[4] += s[5];
        s[4] ^= s[5] << 10; s[7] += s[4]; s[5] += s[6];
        s[5] ^= s[6] >> 4; s[0] += s[5]; s[6] += s[7];
        s[6] ^= s[7] << 8; s[1] += s[6]; s[7] += s[0];
        s[7] ^= s[0] >> 9; s[2] += s[7]; s[0] += s[1];
    }

    private void Scramble()
    {
        b += ++c;
        for (int i = 0; i < StateSize; i++)
        {
            uint x = state[i];
            a ^= (i & 3) switch { 0 => a << 13, 1 => a >> 6, 2 => a << 2, _ => a >> 16 };
            a += state[(i + 128) & 0xFF];
            uint y = state[(x >> 2) & 0xFF] + a + b;
            state[i] = y;
            result[i] = b = state[(y >> 10) & 0xFF] + x;
        }
    }
}
