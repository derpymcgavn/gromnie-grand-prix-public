using Godot;

public partial class GrandPrix
{
    private const uint JesterReflectionSourceLayer = 1u << 10;
    private const uint JesterMirrorLayer = 1u << 9;
    private const uint JesterWindowLayer = 1u << 8;
    private sealed record JesterMirror(Node3D Frame, ShaderMaterial Glass, bool Portal);
    private readonly List<JesterMirror> jesterMirrors = [];
    private JesterMirror? jesterActiveMirror;
    private SubViewport? jesterLens;
    private Camera3D? jesterLensCamera;

    private void BuildJesterMirrors(Node3D root, BogCircuit circuit)
    {
        var mirrors = new Node3D { Name = "The looking glasses" };
        root.AddChild(mirrors);
        Shader shader = GD.Load<Shader>("res://Shaders/JesterMirror.gdshader");
        var frameMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("936c43"), Metallic = .80f, Roughness = .29f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var inlay = new StandardMaterial3D
        {
            AlbedoColor = new Color("d4ab78"), EmissionEnabled = true, Emission = new Color("80c1b5"),
            EmissionEnergyMultiplier = .6f, Metallic = .64f, Roughness = .21f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var darkBacking = new StandardMaterial3D
        {
            AlbedoColor = new Color("2b192c"), Metallic = .64f, Roughness = .44f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        foreach (var (fraction, side, halfWidth, height, warp) in new[]
        {
            (.045f, 1f, 12f, 26f, .007f), (.305f, -1f, 13f, 26f, .020f),
            (.493f, -1f, 15f, 32f, .038f), (.691f, 1f, 18f, 38f, .061f)
        })
        {
            float station = circuit.Length * fraction;
            Vector3 across = circuit.Right(station);
            Vector3 forward = circuit.Forward(station);
            Vector3 face = -across * side - forward * .85f;
            Vector3 position = circuit.Point(station) + across * (side * (circuit.HalfWidth + halfWidth + 5))
                - Vector3.Up * 2;
            AddFrame("Silvered mirror", position, UprightBasis(face), halfWidth, height, warp);
        }

        float entry = circuit.Length * JesterPortalEntry;
        AddFrame("The Last Door", circuit.Point(entry) - Vector3.Up * .4f, UprightBasis(-circuit.Forward(entry)),
            circuit.HalfWidth + 2, 24, .026f, portal: true);
        float exit = circuit.Length * JesterPortalExit - 3;
        AddFrame("The other side", circuit.Point(exit) - Vector3.Up * .4f, UprightBasis(circuit.Forward(exit)),
            circuit.HalfWidth + 2, 24, 0, open: true);

        if (DisplayServer.GetName() == "headless") return;
        jesterLens = new SubViewport
        {
            Name = "Shared planar reflection",
            World3D = GetViewport().FindWorld3D(),
            Size = new Vector2I(OS.HasFeature("android") ? 384 : 640, 360),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        mirrors.AddChild(jesterLens);
        jesterLensCamera = new Camera3D
        {
            CullMask = 1u | JesterReflectionSourceLayer, Near = .18f, Far = 1350
        };
        jesterLens.AddChild(jesterLensCamera);
        jesterLensCamera.MakeCurrent();

        ReflectionStation(.06f);
        ReflectionStation(.68f);

        void ReflectionStation(float fraction)
        {
            var probe = new ReflectionProbe
            {
                Name = "Baked room reflection", Position = circuit.Point(circuit.Length * fraction) + Vector3.Up * 10,
                Size = new Vector3(150, 95, 150), MaxDistance = 155,
                CullMask = JesterReflectionSourceLayer, ReflectionMask = JesterMirrorLayer,
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once
            };
            mirrors.AddChild(probe);
        }

        void AddFrame(string label, Vector3 position, Basis basis, float halfWidth, float height, float warp,
            bool portal = false, bool open = false)
        {
            var frame = new Node3D { Name = label, Transform = new Transform3D(basis, position) };
            mirrors.AddChild(frame);
            var trim = BogCircuit.MeshAt(frame, MirrorArch(halfWidth + 1.35f, height + 1.35f, 1.42f),
                new Vector3(0, 0, .08f), Vector3.One, frameMaterial);
            trim.Layers |= JesterReflectionSourceLayer;
            var bead = BogCircuit.MeshAt(frame, MirrorArch(halfWidth + .41f, height + .41f, .30f),
                new Vector3(0, 0, -.25f), Vector3.One, inlay);
            bead.Layers |= JesterReflectionSourceLayer;
            if (open) return;
            var glass = new ShaderMaterial { Shader = shader };
            glass.SetShaderParameter("portal", portal);
            glass.SetShaderParameter("warp", warp);
            var filling = MirrorArch(halfWidth, height, 0);
            var panel = BogCircuit.MeshAt(frame, filling, Vector3.Zero, Vector3.One, glass);
            panel.Name = "Reflective silver";
            panel.Layers = portal ? JesterWindowLayer : JesterWindowLayer | JesterMirrorLayer;
            panel.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (!portal)
            {
                // A separate back lets the reflection camera see the room without seeing its own face.
                var back = BogCircuit.MeshAt(frame, filling, new Vector3(0, 0, .27f), Vector3.One, darkBacking);
                back.Layers = JesterWindowLayer;
                back.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
            jesterMirrors.Add(new JesterMirror(frame, glass, portal));
        }
    }

    private static Basis UprightBasis(Vector3 forward) =>
        Basis.LookingAt(new Vector3(forward.X, 0, forward.Z).Normalized(), Vector3.Up);

    private static ArrayMesh MirrorArch(float halfWidth, float height, float rim)
    {
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        int segments = 24;
        var border = new List<Vector2> { new(halfWidth, 0) };
        var inside = new List<Vector2> { new(halfWidth - rim, 0) };
        float archHeight = height * .36f;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.Pi / segments;
            border.Add(new Vector2(halfWidth * Mathf.Cos(angle), height - archHeight + archHeight * Mathf.Sin(angle)));
            inside.Add(new Vector2((halfWidth - rim) * Mathf.Cos(angle),
                height - archHeight + (archHeight - rim) * Mathf.Sin(angle)));
        }
        border.Add(new Vector2(-halfWidth, 0));
        inside.Add(new Vector2(-halfWidth + rim, 0));
        for (int i = 0; i < border.Count - 1; i++)
        {
            if (rim > 0)
            {
                Include(border[i]); Include(inside[i]); Include(border[i + 1]);
                Include(inside[i]); Include(inside[i + 1]); Include(border[i + 1]);
            }
            else
            {
                Include(Vector2.Zero); Include(border[i]); Include(border[i + 1]);
            }
        }
        return mesh.Commit();

        void Include(Vector2 point)
        {
            mesh.SetNormal(Vector3.Back);
            mesh.SetUV(new Vector2(.5f + .5f * point.X / halfWidth, 1 - point.Y / height));
            mesh.AddVertex(new Vector3(point.X, point.Y, 0));
        }
    }

    private void StopJesterReflection()
    {
        if (jesterActiveMirror is { } previous)
        {
            previous.Glass.SetShaderParameter("live_view", false);
            previous.Glass.SetShaderParameter("lens_view", default(Variant));
        }
        jesterActiveMirror = null;
        if (IsInstanceValid(jesterLens)) jesterLens!.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    private void UpdateJesterMirrors()
    {
        if (!track.IsJester || jesterLens is not { } lens || jesterLensCamera is not { } reflected || !course.Visible) return;
        JesterMirror? closest = null;
        float best = 137 * 137;
        foreach (var mirror in jesterMirrors)
        {
            Vector3 relative = mirror.Frame.GlobalPosition - camera.GlobalPosition;
            float distance = relative.LengthSquared();
            if (distance >= best || -camera.GlobalBasis.Z.Dot(relative) < -7) continue;
            if (mirror.Portal && -mirror.Frame.GlobalBasis.Z.Dot(relative) > 0) continue;
            (closest, best) = (mirror, distance);
        }
        if (closest is null)
        {
            if (jesterActiveMirror is not null) StopJesterReflection();
            return;
        }
        if (closest != jesterActiveMirror)
        {
            StopJesterReflection();
            closest.Glass.SetShaderParameter("lens_view", lens.GetTexture());
            (jesterActiveMirror = closest).Glass.SetShaderParameter("live_view", true);
            lens.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = lens.Size.X;
        var lensSize = new Vector2I(width, Mathf.Clamp(Mathf.RoundToInt(width * size.Y / Math.Max(size.X, 1)), 260, 650));
        if (lens.Size != lensSize) lens.Size = lensSize;
        reflected.Fov = camera.Fov;
        reflected.KeepAspect = camera.KeepAspect;
        if (closest.Portal)
        {
            float entry = track.Length * JesterPortalEntry, exit = track.Length * JesterPortalExit;
            float lane = Mathf.Clamp((camera.GlobalPosition - track.Point(entry)).Dot(track.Right(entry)), -11, 11);
            // The approach winds uphill directly behind the exit. Put the view through the door
            // on the landing, clear of that deck, so the receiving bend is visible in the glass.
            reflected.GlobalPosition = track.Surface(exit + 2, lane * .65f) + Vector3.Up * 7;
            reflected.LookAt(track.Point(exit + 45) + Vector3.Up * 4);
            return;
        }

        Vector3 normal = closest.Frame.GlobalBasis.Z;
        Basis source = camera.GlobalBasis;
        Vector3 origin = closest.Frame.GlobalPosition;
        var basis = new Basis(-Flip(source.X), Flip(source.Y), Flip(source.Z)).Orthonormalized();
        reflected.GlobalTransform = new Transform3D(basis, origin + Flip(camera.GlobalPosition - origin));

        Vector3 Flip(Vector3 direction) => direction - 2 * direction.Dot(normal) * normal;
    }
}
