using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private static readonly Weather VoidWeather = new("THE VOID", "07091b", "0e1026", "151733", "a1a9ff",
        "8792c4", "101127", 1, 31, -42, .38f, 0f, Night: .82f);
    private Sky? voidSky;
    private Sky? weatherSkyBeforeVoid;

    private void ApplyVoidAtmosphere()
    {
        ApplyWeather(VoidWeather);
        weatherSkyBeforeVoid ??= weatherEnvironment.Sky;
        if (voidSky is null)
        {
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RynthidVoidSky.gdshader") };
            material.SetShaderParameter("compatibility_srgb", RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility");
            voidSky = new Sky
            {
                SkyMaterial = material,
                ProcessMode = Sky.ProcessModeEnum.Quality,
                RadianceSize = Sky.RadianceSizeEnum.Size32
            };
        }
        weatherEnvironment.Sky = voidSky;
        weatherEnvironment.FogEnabled = false;
        weatherEnvironment.AmbientLightEnergy = .27f;
        AcTerrainRenderer.DisableCloudShadow();
        RenderingServer.GlobalShaderParameterSet("dereth_scene_grade", new Vector4(.10f, 1.04f, -.24f, 0f));
    }

    private void RestoreVoidAtmosphere()
    {
        if (weatherSkyBeforeVoid is not { } previousSky) return;
        weatherEnvironment.Sky = previousSky;
        weatherSkyBeforeVoid = null;
    }
}
