using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private static readonly Weather VoidWeather = new("THE VOID", "07091b", "0e1026", "151733", "a1a9ff",
        "8792c4", "101127", 1, 31, -42, .38f, .00042f, Night: .82f);
    private Sky? voidSky;
    private Sky? weatherSkyBeforeVoid;

    private void ApplyVoidAtmosphere()
    {
        ApplyWeather(VoidWeather);
        weatherSkyBeforeVoid ??= weatherEnvironment.Sky;
        voidSky ??= new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RynthidVoidSky.gdshader") },
            ProcessMode = Sky.ProcessModeEnum.Quality,
            RadianceSize = Sky.RadianceSizeEnum.Size32
        };
        weatherEnvironment.Sky = voidSky;
        weatherEnvironment.AmbientLightEnergy = .27f;
        AcTerrainRenderer.DisableCloudShadow();
        RenderingServer.GlobalShaderParameterSet("dereth_scene_grade", new Vector4(.10f, 1.04f, -.42f, .014f));
    }

    private void RestoreVoidAtmosphere()
    {
        if (weatherSkyBeforeVoid is not { } previousSky) return;
        weatherEnvironment.Sky = previousSky;
        weatherSkyBeforeVoid = null;
    }
}
