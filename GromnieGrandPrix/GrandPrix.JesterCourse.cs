using Godot;

public partial class GrandPrix
{
    private const int JesterCourseId = 13006;
    private const string JesterCourseName = "Jester’s Descent";
    private const float JesterPortalEntry = .848f, JesterPortalExit = .968f;
    private static readonly string[] JesterChapters = ["THE GALLERY", "THE WRONG SIDE", "THE BROKEN HOUR", "THE LAST DOOR"];
    private Node3D? jesterRoot;
    private Sky? jesterSky;
    private ShaderMaterial? jesterSkyPaint;
    private int jesterChapter;
    private bool jesterCameraSnap;

    private static BogCircuit CreateJesterCircuit()
    {
        const int anchors = 120;
        var points = new Vector3[anchors];
        for (int i = 0; i < anchors; i++)
        {
            float t = i / (float)anchors;
            float heading = 2 * Mathf.Tau * t + .58f;
            float radius = 367 + 86 * Mathf.Cos(t * Mathf.Tau - .33f)
                + 22 * Mathf.Sin(heading * 3 + 1.1f) + 11 * Mathf.Sin(heading * 5 + .36f);
            float wave = Mathf.Sin(t * Mathf.Pi);
            float altitude = 268 - 226 * Mathf.SmoothStep(.07f, .79f, t) + 226 * Mathf.SmoothStep(.857f, .986f, t)
                + 6 * Mathf.Sin(heading * 2) * wave * wave;
            points[i] = new Vector3(radius * Mathf.Cos(heading), altitude, radius * Mathf.Sin(heading));
        }
        var circuit = new BogCircuit(JesterCourseName, [], null, 17, voidCorners: points, voidStyle: VoidCircuitStyle.Jester);
        circuit.ReserveMirrorSector(circuit.Length * JesterPortalEntry, circuit.Length * JesterPortalExit);
        return circuit;
    }

    private bool JesterCourseReady() => IsInstanceValid(jesterRoot) && IsInstanceValid(jesterRoot!.GetParent());

    private void BuildJesterCourse(Node3D scene, BogCircuit circuit)
    {
        ClearJesterCourse();
        jesterRoot = new Node3D { Name = "Jester’s Descent" };
        scene.AddChild(jesterRoot);
        BuildJesterRoad(jesterRoot, circuit);
        BuildJesterArchitecture(jesterRoot, circuit);
        BuildJesterClocks(jesterRoot, circuit);
        BuildJesterCast(jesterRoot, circuit);
        BuildJesterMirrors(jesterRoot, circuit);
        GD.Print($"{JesterCourseName}: {circuit.Length * (1 + JesterPortalEntry - JesterPortalExit):F0} m driven per lap, "
            + $"{circuit.Point(0).Y - circuit.Point(circuit.Length * JesterPortalEntry).Y:F0} m descent to the mirror.");
    }

    private void ClearJesterCourse()
    {
        StopJesterReflection();
        jesterMirrors.Clear();
        jesterLens = null;
        jesterLensCamera = null;
        jesterRoot = null;
        ClearJesterCast();
        jesterCameraSnap = false;
    }

    private void BuildJesterRoad(Node3D root, BogCircuit circuit)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/JesterRoad.gdshader") };
        var stone = townRegion?.Blend?.Textures.FirstOrDefault(layer => layer.Type == 32);
        if (stone is not null && assets.Texture(stone.Texture) is { } texture)
            material.SetShaderParameter("native_stone", texture);
        int tiles = Mathf.RoundToInt(circuit.Length / 12) * 2;
        var deck = BogCircuit.MeshAt(root, BuildClosedVoidDeck(circuit, tiles), Vector3.Zero, Vector3.One, material);
        deck.Name = "Continuous stone ribbon";
        deck.Layers |= JesterReflectionSourceLayer;
        var rims = BogCircuit.MeshAt(root, BuildVoidDeckRims(circuit, JesterRoadColor), Vector3.Zero, Vector3.One,
            new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            });
        rims.Name = "Luminous stone shoulders";
        rims.Layers |= JesterReflectionSourceLayer;
    }

    private static Color JesterRoadColor(float fraction)
    {
        float depth = Mathf.SmoothStep(.10f, .79f, fraction) * (1 - Mathf.SmoothStep(.90f, .98f, fraction));
        return new Color("8adccd").Lerp(new Color("e194c1"), depth);
    }

    private void ApplyJesterAtmosphere()
    {
        ApplyVoidAtmosphere();
        if (jesterSky is null)
        {
            jesterSkyPaint = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/JesterSky.gdshader") };
            jesterSkyPaint.SetShaderParameter("compatibility_srgb", RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility");
            jesterSky = new Sky
            {
                SkyMaterial = jesterSkyPaint, RadianceSize = Sky.RadianceSizeEnum.Size32,
                ProcessMode = Sky.ProcessModeEnum.Quality
            };
        }
        weatherEnvironment.Sky = jesterSky;
        jesterChapter = -1;
        ApplyJesterChapter(0, 1);
    }

    private void TickJesterDescent()
    {
        if (racers.Count == 0 || countdown > 0 || elapsed < 1.3f) return;
        float fraction = Mathf.PosMod(Player.Distance, track.Length) / track.Length;
        int chapter = fraction switch
        {
            < .24f or >= .958f => 0,
            < .52f => 1,
            < .80f => 2,
            _ => 3
        };
        if (chapter == jesterChapter) return;
        ApplyJesterChapter(chapter, lap);
        if (fraction < .87f) Callout(JesterChapters[chapter], 2.2f);
    }

    private void ApplyJesterChapter(int chapter, int lapNumber)
    {
        jesterChapter = chapter;
        float intensity = chapter + (lapNumber - 1) * .28f;
        float depth = Mathf.Clamp(intensity / 3.55f, 0, 1);
        jesterSkyPaint?.SetShaderParameter("descent", depth);
        weatherSun.LightEnergy = Mathf.Lerp(1.12f, .44f, depth);
        weatherEnvironment.AmbientLightEnergy = Mathf.Lerp(.53f, .30f, depth);
        RenderingServer.GlobalShaderParameterSet("dereth_scene_grade", new Vector4(.06f - depth * .24f, 1 + depth * .08f, -.08f - depth * .22f, 0));
    }

    private bool TryJesterPortal(MountedRacer racer, float before)
    {
        if (!track.IsJester || racer.FreeDriving || racer.GatesCleared == 0) return false;
        float offset = Mathf.Floor(before / track.Length) * track.Length;
        float entry = offset + track.Length * JesterPortalEntry;
        float exit = offset + track.Length * JesterPortalExit;
        if (entry <= track.Gate(racer.GatesCleared - 1) || exit >= track.Gate(racer.GatesCleared) ||
            !Crossed(before, racer.Distance, entry)) return false;
        racer.Distance = exit + (racer.Distance - entry);
        ResetRivalMotion(racer);
        if (racer == Player)
        {
            jesterCameraSnap = true;
            Callout("THROUGH THE LOOKING GLASS", 2.6f);
        }
        return true;
    }
}
