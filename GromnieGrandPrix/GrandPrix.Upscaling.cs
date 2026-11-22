using Godot;

public partial class GrandPrix
{
    private enum RaceUpscaler { Off, Fsr1, Fsr2, MetalFxSpatial, MetalFxTemporal, Bilinear }
    private enum RaceRenderQuality { UltraQuality, Quality, Balanced, Performance }
    private readonly AndroidRacePacing androidRacePacing = new();
    private bool paceAndroidFrames;

    private bool YanshiPerformanceProfile => AndroidGraphics && activeTown == YanshiXlCourseId &&
        raceSettings.Upscaler == RaceUpscaler.Bilinear && raceSettings.RenderQuality == RaceRenderQuality.Performance;

    private static bool UpscalerAvailable(RaceUpscaler upscaler)
    {
        if (upscaler is RaceUpscaler.Off or RaceUpscaler.Bilinear) return true;
        string method = RenderingServer.GetCurrentRenderingMethod();
        if (method is not ("forward_plus" or "mobile") || RenderingServer.GetRenderingDevice() is not { } device)
            return false;
        bool metal = RenderingServer.GetCurrentRenderingDriverName() == "metal";
        return upscaler switch
        {
            RaceUpscaler.Fsr1 or RaceUpscaler.Fsr2 => method == "forward_plus",
            RaceUpscaler.MetalFxSpatial => metal && device.HasFeature(RenderingDevice.Features.MetalfxSpatial),
            RaceUpscaler.MetalFxTemporal => metal && method == "forward_plus"
                && device.HasFeature(RenderingDevice.Features.MetalfxTemporal)
                && TryGetMetalFxScaleRange(device, out _, out _),
            _ => false,
        };
    }

    private static float RenderScale(RaceRenderQuality quality) => quality switch
    {
        RaceRenderQuality.UltraQuality => .77f,
        RaceRenderQuality.Balanced => .59f,
        RaceRenderQuality.Performance => AndroidGraphics ? .42f : .50f,
        _ => .67f,
    };

    private float SupportedRenderScale(RaceUpscaler mode, RaceRenderQuality quality)
    {
        if (mode == RaceUpscaler.Off) return 1f;
        float scale = racing && YanshiPerformanceProfile ? .28f : RenderScale(quality);
        return mode == RaceUpscaler.MetalFxTemporal && RenderingServer.GetRenderingDevice() is { } device
            && TryGetMetalFxScaleRange(device, out float lower, out float upper)
            ? Math.Clamp(scale, lower, upper) : scale;
    }

    private static bool TryGetMetalFxScaleRange(RenderingDevice device, out float lower, out float upper)
    {
        // Godot stores these limits in millionths. Round each inward so the
        // float handed back to the viewport stays within the device's range.
        double min = device.LimitGet(RenderingDevice.Limit.MetalfxTemporalScalerMinScale) / 1_000_000d;
        double max = device.LimitGet(RenderingDevice.Limit.MetalfxTemporalScalerMaxScale) / 1_000_000d;
        lower = (float)min;
        upper = (float)Math.Min(max, 1);
        if (lower < min) lower = MathF.BitIncrement(lower);
        if (upper > max) upper = MathF.BitDecrement(upper);
        return lower > 0 && lower < 1f && upper >= lower;
    }

    private RaceUpscaler ActiveUpscaler => UpscalerAvailable(raceSettings.Upscaler) ? raceSettings.Upscaler : RaceUpscaler.Off;

    private void ApplyRaceUpscaling(Viewport viewport)
    {
        RaceUpscaler active = ActiveUpscaler;
        bool temporal = active is RaceUpscaler.Fsr2 or RaceUpscaler.MetalFxTemporal;
        float scale = SupportedRenderScale(active, raceSettings.RenderQuality);
        var mode = active switch
        {
            RaceUpscaler.Fsr1 => Viewport.Scaling3DModeEnum.Fsr,
            RaceUpscaler.Fsr2 => Viewport.Scaling3DModeEnum.Fsr2,
            RaceUpscaler.MetalFxSpatial => Viewport.Scaling3DModeEnum.MetalfxSpatial,
            RaceUpscaler.MetalFxTemporal => Viewport.Scaling3DModeEnum.MetalfxTemporal,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        if (temporal && viewport.Msaa3D != Viewport.Msaa.Disabled)
            viewport.Msaa3D = Viewport.Msaa.Disabled;
        // MetalFX validates the scale each time the viewport is reconfigured.
        if (active == RaceUpscaler.MetalFxTemporal && viewport.Scaling3DScale != scale)
            viewport.Scaling3DScale = scale;
        if (viewport.Scaling3DMode != mode) viewport.Scaling3DMode = mode;
        if (viewport.Scaling3DScale != scale) viewport.Scaling3DScale = scale;
        if (!temporal && viewport.Msaa3D != (Viewport.Msaa)raceSettings.Msaa)
            viewport.Msaa3D = (Viewport.Msaa)raceSettings.Msaa;
        if (AndroidGraphics)
        {
            bool yanshi = racing && YanshiPerformanceProfile;
            Engine.MaxFps = yanshi ? 30 : 0;
            paceAndroidFrames = yanshi && RenderingServer.GetCurrentRenderingDriverName() == "opengl3";
            if (!paceAndroidFrames) androidRacePacing.Reset();
            for (int i = 0; i < racers.Count; i++)
                racers[i].ReduceRoadContactUpdates = yanshi && i != localRacerIndex;
            if (!track.IsVoid)
                weatherEnvironment.FogDensity = Math.Max(currentWeather.FogDensity, yanshi ? .005f : .0026f);
        }
    }

    private string ActiveUpscalingDescription()
    {
        RaceUpscaler active = ActiveUpscaler;
        if (raceSettings.Upscaler != active) return "Saved upscaler unavailable. Using native resolution.";
        if (active == RaceUpscaler.Off) return "Rendering at native resolution.";

        float percent = SupportedRenderScale(active, raceSettings.RenderQuality) * 100;
        string description = $"3D rendering at {percent:0.#}% of output width and height.";
        if (racing && YanshiPerformanceProfile) description += " Yanshi XL targets 30 FPS.";
        if (active is RaceUpscaler.Fsr2 or RaceUpscaler.MetalFxTemporal)
            description += " Temporal anti-aliasing is active.";
        return description;
    }
}
