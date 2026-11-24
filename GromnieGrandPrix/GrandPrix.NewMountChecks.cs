using Godot;

public partial class GrandPrix
{
    private async Task CaptureNewMounts(string folder)
    {
        foreach (var (kind, name) in new[]
        {
            (RaceMountKind.Turkey, "Turkey"), (RaceMountKind.Tusker, "Tusker"), (RaceMountKind.SnowTusker, "Snow-Tusker")
        })
        {
            OpenStable();
            SelectMount(kind);
            ChooseCourse(0);
            StartRace();
            Require(racing && Player.Rig.Kind == kind, $"{name} loads in the race");
            paused = true;
            countdown = 0;
            raceUi.Hide();
            Player.Distance = 78;
            Player.Lane = -.5f;
            Player.Speed = 24;
            Player.Draw(track, .26f, true, -.15f);
            float head = Player.Mount.BodyMeshes.Concat(Player.Rider.BodyMeshes)
                .Where(mesh => mesh.Mesh is not null)
                .Max(mesh => (mesh.GlobalTransform * mesh.Mesh!.GetAabb()).End.Y) - Player.Root.GlobalPosition.Y;
            head = Mathf.Clamp(head, 2.8f, 5.8f);
            camera.Fov = 44;
            camera.Position = Player.Root.ToGlobal(new Vector3(head * 1.45f, head, -head * 1.36f));
            camera.LookAt(Player.Root.ToGlobal(new Vector3(0, head * .48f, -.20f)));
            await SaveFrame(Path.Combine(folder, $"{name}-Racing.png"));
        }
        GD.Print("NEW MOUNTS CAPTURE PASS | native Turkey, Tusker, and Snow Tusker.");
    }
}
