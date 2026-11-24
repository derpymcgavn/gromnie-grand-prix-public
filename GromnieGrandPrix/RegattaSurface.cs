using Godot;

internal static class RegattaSurface
{
    // Wave vectors, angular frequencies and amplitudes share world meters and
    // simulation seconds with RegattaSurface.gdshaderinc.
    private static readonly Vector4 WaveX = new(.112f, -.165f, .270f, -.290f);
    private static readonly Vector4 WaveZ = new(.057f, .101f, .154f, .310f);
    private static readonly Vector4 Frequency = new(1.11f, 1.38f, 1.74f, 2.04f);
    private static readonly Vector4 Phase = new(.20f, 1.70f, 4.60f, 2.80f);
    private static readonly Vector4 Amplitude = new(.24f, .18f, .14f, .10f);

    internal static float HeightOffset(Vector2 position, float time, RegattaFluidField? fluid = null)
    {
        Vector4 phase = WaveX * position.X + WaveZ * position.Y - Frequency * time + Phase;
        return Amplitude.Dot(new Vector4(Mathf.Sin(phase.X), Mathf.Sin(phase.Y), Mathf.Sin(phase.Z), Mathf.Sin(phase.W)))
            + (fluid?.HeightOffset(position) ?? 0);
    }

    internal static void Configure(ShaderMaterial material)
    {
        material.SetShaderParameter("regatta_wave_x", WaveX);
        material.SetShaderParameter("regatta_wave_z", WaveZ);
        material.SetShaderParameter("regatta_wave_frequency", Frequency);
        material.SetShaderParameter("regatta_wave_phase", Phase);
        material.SetShaderParameter("regatta_wave_amplitude", Amplitude);
    }
}
