using Godot;

public partial class GrandPrix
{
    private void BuildHybridTravelMarkers(Node3D scene, BogCircuit route)
    {
        var markers = new Node3D { Name = "Mount change signs and route markers" };
        scene.AddChild(markers);
        var plate = RegattaPlateTexture();
        var port = RegattaFinish(plate, "ff7955", .49f, .35f);
        var starboard = RegattaFinish(plate, "54e5c0", .44f, .43f);
        var ringFinish = RegattaFinish(plate, "c39dff", .43f, .50f);
        var buoy = new CylinderMesh { TopRadius = .45f, BottomRadius = .84f, Height = 1.32f, RadialSegments = 10 };
        var hoop = new TorusMesh { InnerRadius = 5.2f, OuterRadius = 5.48f, Rings = 26, RingSegments = 6 };
        var portMarkers = new List<Transform3D>();
        var starboardMarkers = new List<Transform3D>();
        var airMarkers = new List<Transform3D>();
        foreach (var section in route.TravelSections)
        {
            if (route.TravelSections.Count > 1) AddChangeSign(section);
            if (section.Mode == TrackTravelMode.JetSki)
            {
                int count = Math.Max(1, Mathf.CeilToInt((section.End - section.Start) / 42));
                for (int i = 0; i < count; i++)
                {
                    float at = Mathf.Lerp(section.Start, section.End, (i + .5f) / count);
                    float lane = route.WidthAt(at) * .94f;
                    portMarkers.Add(new Transform3D(Basis.Identity, route.Surface(at, -lane) + Vector3.Up * .44f));
                    starboardMarkers.Add(new Transform3D(Basis.Identity, route.Surface(at, lane) + Vector3.Up * .44f));
                }
            }
            else if (section.Mode == TrackTravelMode.Flight)
            {
                int count = Math.Max(1, Mathf.CeilToInt((section.End - section.Start) / 105));
                for (int i = 0; i < count; i++)
                {
                    float at = Mathf.Lerp(section.Start, section.End, (i + .5f) / count);
                    var frame = Basis.FromEuler(new Vector3(Mathf.Pi * .5f, BogCircuit.Yaw(route.Forward(at)), 0));
                    airMarkers.Add(new Transform3D(frame, HybridFlightCenter(route, section, at) + Vector3.Up * 1.6f));
                }
            }
        }
        if (portMarkers.Count > 0) AddMarkerBatch(markers, buoy, port, portMarkers);
        if (starboardMarkers.Count > 0) AddMarkerBatch(markers, buoy, starboard, starboardMarkers);
        if (airMarkers.Count > 0) AddMarkerBatch(markers, hoop, ringFinish, airMarkers);

        void AddChangeSign(TrackTravelSection section)
        {
            float at = section.Start;
            string caption = section.Mode switch
            {
                TrackTravelMode.JetSki => "JET SKI",
                TrackTravelMode.Flight => "TAKE FLIGHT",
                _ => "GROUND MOUNTS"
            };
            string tint = section.Mode switch
            {
                TrackTravelMode.JetSki => "5de8cf",
                TrackTravelMode.Flight => "c39dff",
                _ => "ffd17f"
            };
            var sign = new Node3D
            {
                Name = caption,
                Position = route.Surface(at, route.WidthAt(at) + .8f),
                Rotation = new Vector3(0, BogCircuit.Yaw(route.Forward(at)) + Mathf.Pi, 0)
            };
            markers.AddChild(sign);
            var signMaterial = RegattaFinish(plate, tint, .48f, .29f);
            BogCircuit.Box(sign, Vector3.Up * 1.58f, new Vector3(.23f, 3.12f, .25f), signMaterial);
            BogCircuit.Box(sign, Vector3.Up * 3.05f, new Vector3(4.62f, 1.14f, .22f), signMaterial);
            sign.AddChild(new Label3D
            {
                Text = caption, FontSize = 60, PixelSize = .012f, OutlineSize = 8,
                Position = new Vector3(0, 3.05f, .128f), DoubleSided = false
            });
        }
    }
}
