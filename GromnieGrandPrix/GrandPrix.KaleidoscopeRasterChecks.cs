using Godot;

public partial class GrandPrix
{
    private async Task CaptureKaleidoscopeRasterMirrors(string folder)
    {
        Require(RenderingServer.GetCurrentRenderingMethod() == "forward_plus",
            "the raster reflection check uses Forward+");
        StartKaleidoscopeCheck();
        SetPhysicsProcess(false);
        countdown = 0;
        ClearSpeedBlur();
        raceUi.Hide();

        const float station = 108;
        for (int i = 0; i < racers.Count; i++)
        {
            var racer = racers[i];
            racer.Distance = station + i * 9;
            racer.Lane = i == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * (4.5f + i);
            racer.Speed = RoadPace;
            racer.Draw(track, .075f, true);
        }

        camera.Projection = Camera3D.ProjectionType.Perspective;
        camera.Fov = 69;
        camera.GlobalPosition = track.Surface(station - 13, 0) + Vector3.Up * 6.2f;
        camera.LookAt(track.Point(station + 32) + Vector3.Up * 5.6f);
        UpdateKaleidoscopeProbe(0, refresh: true);
        Require(kaleidoscopeProbe is { Visible: true, BoxProjection: true },
            "the course activates a local reflection probe on the raster renderer");
        var probe = kaleidoscopeProbe!;
        await WaitForCapture();
        await SaveFrame(Path.Combine(folder, "Kaleidoscope-Raster-Chase.png"));

        float sampleStation = track.Project(probe.GlobalPosition, station).Distance;
        var (point, normal) = SampleKaleidoscopeRasterWall(sampleStation);
        camera.Fov = 55;
        camera.GlobalPosition = point + normal * 11;
        camera.LookAt(point);

        // Match the renderer's parallax correction by tracing the reflected ray
        // to the probe's box. Place the marker along the equivalent cubemap ray.
        Vector3 localPoint = probe.ToLocal(point);
        Vector3 direction = (probe.GlobalBasis.Inverse() * normal).Normalized();
        Vector3 boxHalf = probe.Size * .5f;
        float distance = Math.Min(ExitDistance(0), Math.Min(ExitDistance(1), ExitDistance(2)));
        Require(distance > 0 && float.IsFinite(distance), "the sampled wall point is inside the probe");
        Vector3 captureDirection = (localPoint + direction * distance - probe.OriginOffset).Normalized();
        var marker = new MeshInstance3D
        {
            Name = "Off-camera raster reflection target",
            Mesh = new SphereMesh { Radius = 2.8f, Height = 5.6f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = Marker(new Color(1, .01f, .78f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        course.AddChild(marker);
        marker.GlobalPosition = probe.ToGlobal(probe.OriginOffset + captureDirection * 9);
        try
        {
            Require(camera.IsPositionBehind(marker.GlobalPosition),
                "the target cannot appear directly in the camera");
            UpdateKaleidoscopeProbe(0, refresh: true);
            await WaitForCapture();
            Color magenta = ReadWall();
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Raster-Wall-Magenta.png"));

            marker.MaterialOverride = Marker(new Color(.03f, 1, .09f));
            UpdateKaleidoscopeProbe(0, refresh: true);
            await WaitForCapture();
            Color green = ReadWall();
            await SaveFrame(Path.Combine(folder, "Kaleidoscope-Raster-Wall-Green.png"));
            GD.Print($"KALEIDOSCOPE RASTER PROBE | magenta RGB={magenta.R:F3},{magenta.G:F3},{magenta.B:F3} " +
                $"| green RGB={green.R:F3},{green.G:F3},{green.B:F3}");
            Require(magenta.R - magenta.G > .08f && magenta.B - magenta.G > .08f &&
                green.G - green.R > .08f && green.G - green.B > .08f && green.G - magenta.G > .12f,
                "the raster wall reflects a changing object behind the camera");
        }
        finally
        {
            marker.Hide();
            marker.QueueFree();
        }

        GD.Print("KALEIDOSCOPE RASTER PASS | changing off-camera target reflected by the tunnel wall.");

        float ExitDistance(int axis)
        {
            float step = direction[axis];
            return Math.Abs(step) <= .0001f ? float.PositiveInfinity
                : (boxHalf[axis] * Math.Sign(step) - localPoint[axis]) / step;
        }

        async Task WaitForCapture()
        {
            for (int frame = 0; frame < 24; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        Color ReadWall()
        {
            using var image = GetViewport().GetTexture().GetImage();
            int centerX = image.GetWidth() / 2, centerY = image.GetHeight() / 2;
            const int half = 5;
            var total = new Color(0, 0, 0, 0);
            for (int y = centerY - half; y <= centerY + half; y++)
            for (int x = centerX - half; x <= centerX + half; x++)
                total += image.GetPixel(x, y);
            return total / ((2 * half + 1) * (2 * half + 1));
        }

        static StandardMaterial3D Marker(Color color) => new()
        {
            AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 2
        };
    }

    private (Vector3 Point, Vector3 Normal) SampleKaleidoscopeRasterWall(float station)
    {
        var tunnel = course.GetNode<Node3D>("Kaleidoscope/Mirrored tunnel");
        Vector3 center = track.Point(station);
        Vector3 target = center + track.Right(station) * 49 + Vector3.Up * 13;
        Vector3 point = Vector3.Zero, normal = Vector3.Zero;
        float nearest = float.PositiveInfinity;
        foreach (var section in tunnel.GetChildren().OfType<MeshInstance3D>())
        {
            var world = section.GlobalTransform;
            var surface = section.Mesh.SurfaceGetArrays(0);
            var positions = surface[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = surface[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var indices = surface[(int)Mesh.ArrayType.Index].AsInt32Array();
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                Vector3 triangle = world * ((positions[a] + positions[b] + positions[c]) / 3);
                float distance = triangle.DistanceSquaredTo(target);
                if (distance >= nearest) continue;
                Vector3 inward = (world.Basis * (normals[a] + normals[b] + normals[c])).Normalized();
                if (Math.Abs(inward.Y) > .88f || inward.Dot(center - triangle) <= 0) continue;
                (point, normal, nearest) = (triangle, inward, distance);
            }
        }
        Require(float.IsFinite(nearest), "the tunnel contains an inward-facing side mirror");
        return (point, normal);
    }
}
