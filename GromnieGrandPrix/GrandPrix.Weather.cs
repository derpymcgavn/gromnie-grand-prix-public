using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private sealed record Weather(string Name, string Zenith, string Horizon, string Cloud, string Light, string Ambient,
        string Fog, float Cover, float SunHeight, float SunHeading, float SunEnergy, float FogDensity, float Rain = 0, float Night = 0);

    private static readonly Weather[] WeatherPresets =
    [
        new("CLEAR SKY", "387bb7", "d4e1e6", "f3f4fb", "fff0d6", "dee9f7", "c0cfd4", .66f, 51, -41, .91f, .00105f),
        new("CLOUDBREAK", "4385b8", "c5d6e0", "f0f0f4", "ffedcd", "dce8f5", "c2ccd2", .52f, 39, -57, .83f, .00135f),
        new("LOW MIST", "8497a9", "c7d4d5", "d9e1e6", "eeeedd", "d7e5e8", "becdcb", .39f, 30, -14, .62f, .00315f),
        new("PASSING SHOWER", "4e627b", "9eacb8", "a9b3c0", "d9e4f8", "c4d5e8", "a1b1be", .28f, 38, -21, .40f, .00235f, .90f),
        new("GOLDEN HOUR", "6c83b6", "f4bd98", "e7c4bf", "ffbb86", "d8d9f0", "d7b7a3", .54f, 17, -74, .84f, .00170f, Night: .16f),
        new("MOONLIT NIGHT", "08122a", "405674", "7290ba", "abc8ff", "8da5cf", "425774", .65f, 24, -39, .36f, .00167f, Night: 1)
    ];

    private Godot.Environment weatherEnvironment = null!;
    private DirectionalLight3D weatherSun = null!;
    private ShaderMaterial skyMaterial = null!, rainMaterial = null!;
    private ColorRect rainOverlay = null!;
    private Label weatherLabel = null!;
    private NoiseTexture2D cloudNoise = null!;
    private Texture2D? firstMoon, secondMoon;
    private Weather currentWeather = WeatherPresets[0];
    private float raceWeatherAge, trackWater;
    private Vector2 cloudWind;
    private int previousRaceWeather = -1;
    private static readonly bool AndroidGraphics = OS.HasFeature("android");

    private void BuildWeather()
    {
        skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/DerethSky.gdshader") };
        cloudNoise = new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, SeamlessBlendSkirt = .19f, GenerateMipmaps = true,
            Noise = new FastNoiseLite { Seed = 7481, Frequency = .013f, FractalOctaves = 4, FractalGain = .52f }
        };
        skyMaterial.SetShaderParameter("cloud_noise", cloudNoise);
        skyMaterial.SetShaderParameter("simple_clouds", AndroidGraphics);
        weatherEnvironment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMaterial, ProcessMode = Sky.ProcessModeEnum.Incremental, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightSkyContribution = 0,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            AmbientLightEnergy = .66f, FogEnabled = true, FogSkyAffect = 0
        };
        AddChild(new WorldEnvironment { Environment = weatherEnvironment });
        weatherSun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 135 };
        AddChild(weatherSun);

        rainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RaceRain.gdshader") };
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        rainOverlay = new ColorRect { Material = rainMaterial, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(rainOverlay);
        rainOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ApplyWeather(WeatherPresets[0]);
    }

    private void LoadDerethMoons()
    {
        Texture2D? Load(uint model)
        {
            if (!assets.Archive.Entries.ContainsKey(model)) return null;
            return assets.Geometry(model).Surfaces.Select(id => assets.Surface(id, particle: true).AlbedoTexture)
                .FirstOrDefault(texture => texture is not null);
        }
        firstMoon = Load(0x01001F6A);
        secondMoon = Load(0x01001F67);
        if (firstMoon is not null) skyMaterial.SetShaderParameter("moon_one", firstMoon);
        if (secondMoon is not null) skyMaterial.SetShaderParameter("moon_two", secondMoon);
        skyMaterial.SetShaderParameter("has_moon_one", firstMoon is not null);
        skyMaterial.SetShaderParameter("has_moon_two", secondMoon is not null);
    }

    private int RollRaceWeather()
    {
        int choice = Random.Shared.Next(WeatherPresets.Length - (previousRaceWeather < 0 ? 0 : 1));
        if (previousRaceWeather >= 0 && choice >= previousRaceWeather) choice++;
        previousRaceWeather = choice;
        return choice;
    }

    private void ApplyWeather(Weather weather)
    {
        RestoreVoidAtmosphere();
        currentWeather = weather;
        raceWeatherAge = trackWater = 0;
        cloudWind = weather.Rain > 0 ? new Vector2(.012f, -.006f) : new Vector2(.0047f, .0026f);
        weatherSun.RotationDegrees = new Vector3(-weather.SunHeight, weather.SunHeading, 0);
        weatherSun.LightColor = new Color(weather.Light);
        weatherSun.LightEnergy = weather.SunEnergy;
        weatherEnvironment.AmbientLightColor = new Color(weather.Ambient);
        weatherEnvironment.AmbientLightEnergy = .54f + .12f * weather.Night + .10f * weather.Rain;
        weatherEnvironment.FogLightColor = new Color(weather.Fog);
        weatherEnvironment.FogDensity = AndroidGraphics ? Math.Max(weather.FogDensity, .0026f) : weather.FogDensity;
        weatherEnvironment.FogEnabled = true;
        skyMaterial.SetShaderParameter("zenith", new Color(weather.Zenith));
        skyMaterial.SetShaderParameter("horizon", new Color(weather.Horizon));
        skyMaterial.SetShaderParameter("cloud_color", new Color(weather.Cloud));
        skyMaterial.SetShaderParameter("sun_color", new Color(weather.Light));
        Vector3 light = weatherSun.Basis.Z.Normalized();
        Vector3 forward = track.Forward(BogCircuit.Grid + 75);
        Vector3 moonOneDirection = (forward.Rotated(Vector3.Up, .25f) + Vector3.Up * .37f).Normalized();
        Vector3 moonTwoDirection = (forward.Rotated(Vector3.Up, -.30f) + Vector3.Up * .20f).Normalized();
        if (weather.Night >= .9f)
        {
            light = moonOneDirection;
            weatherSun.Basis = Basis.LookingAt(-light);
        }
        skyMaterial.SetShaderParameter("sun_direction", light);
        skyMaterial.SetShaderParameter("moon_one_direction", moonOneDirection);
        skyMaterial.SetShaderParameter("moon_two_direction", moonTwoDirection);
        skyMaterial.SetShaderParameter("sunlight", weather.SunEnergy);
        skyMaterial.SetShaderParameter("cloud_threshold", weather.Cover);
        skyMaterial.SetShaderParameter("wind", cloudWind);
        skyMaterial.SetShaderParameter("night", weather.Night);
        var horizon = new Color(weather.Horizon);
        var zenith = new Color(weather.Zenith);
        RenderingServer.GlobalShaderParameterSet("dereth_wetness", 0f);
        RenderingServer.GlobalShaderParameterSet("dereth_rain", weather.Rain);
        RenderingServer.GlobalShaderParameterSet("dereth_horizon", new Vector3(horizon.R, horizon.G, horizon.B));
        RenderingServer.GlobalShaderParameterSet("dereth_zenith", new Vector3(zenith.R, zenith.G, zenith.B));
        RenderingServer.GlobalShaderParameterSet("dereth_light", light);
        AcTerrainRenderer.SetCloudShadow(cloudNoise, Vector2.Zero, 330,
            AndroidGraphics ? 0 : (weather.Night >= .9f ? .08f : .24f));
        rainMaterial.SetShaderParameter("intensity", weather.Rain);
        rainOverlay.Visible = weather.Rain > 0 && !YanshiPerformanceProfile;
        ApplySceneGrade(weather);
        if (weatherLabel is not null) weatherLabel.Text = weather.Name;
    }

    private void TickTrackWeather(float dt)
    {
        if (track.IsVoid)
        {
            TickVoidSetpiece(dt);
            return;
        }
        raceWeatherAge += dt;
        if (currentWeather.Rain > 0)
        {
            float rain = currentWeather.Rain * (1 - Mathf.SmoothStep(22f, 37f, raceWeatherAge));
            trackWater = Mathf.MoveToward(trackWater, rain, dt * (rain > trackWater ? .094f : .036f));
            rainOverlay.Visible = rain > .02f && !YanshiPerformanceProfile;
            rainMaterial.SetShaderParameter("intensity", rain);
            RenderingServer.GlobalShaderParameterSet("dereth_rain", rain);
            RenderingServer.GlobalShaderParameterSet("dereth_wetness", trackWater);
        }
        if (!AndroidGraphics)
            AcTerrainRenderer.SetCloudShadow(cloudNoise, cloudWind * (worldTime * 330f), 330,
                currentWeather.Night >= .9f ? .08f : .24f);
    }
}
