using Godot;

public partial class GrandPrix
{
    private ColorRect sceneGrade = null!;

    private void BuildSceneGrade()
    {
        if (OS.HasFeature("android")) return;
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        sceneGrade = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/SceneGrade.gdshader") }
        };
        layer.AddChild(sceneGrade);
        sceneGrade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        speedBlur.VisibilityChanged += UpdateSceneGrade;
        tiltShiftOverlay.VisibilityChanged += UpdateSceneGrade;
        UpdateSceneGrade();
    }

    private void UpdateSceneGrade()
    {
        // The motion and tilt-shift passes include the grade. Use one screen copy per frame.
        sceneGrade.Visible = !speedBlur.Visible && !tiltShiftOverlay.Visible;
    }

    private static void ApplySceneGrade(Weather weather)
    {
        float mist = Mathf.SmoothStep(.00155f, .0032f, weather.FogDensity);
        float sunset = (1 - Mathf.SmoothStep(17, 39, weather.SunHeight)) * (1 - weather.Night);
        float contrast = .19f - mist * .065f - weather.Night * .08f;
        float saturation = 1.09f - weather.Rain * .12f - mist * .064f - weather.Night * .16f;
        float warmth = .29f + sunset * .47f - weather.Rain * .43f - weather.Night * .48f;
        float midtones = .004f + mist * .012f + weather.Night * .042f;
        RenderingServer.GlobalShaderParameterSet("dereth_scene_grade", new Vector4(contrast, saturation, warmth, midtones));
    }
}
