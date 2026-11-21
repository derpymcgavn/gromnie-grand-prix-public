using Godot;
using DerethSurvivors;
using System.Text.Json;

public partial class GrandPrix
{
    private void CheckMountColors()
    {
        Require(loaded, "native mount palettes are loaded");
        RestoreMountColors(new GarageSave(7, appearance, -1, datFolder, SiraluunSkin: 4));
        Require(MountColorFor(RaceMountKind.Gromnie) == 7 && MountColorFor(RaceMountKind.Siraluun) == 4 &&
                MountColorFor(RaceMountKind.CarenziSkates) == 0, "legacy Gromnie and Siraluun colors migrate");

        foreach (var mount in RaceMount.All)
        {
            var colors = RaceMount.ColorsFor(mount.Kind);
            Require(colors.Count > 1, $"{mount.Name} offers multiple colors");
            SelectMount(mount.Kind);
            Require(swatches.Count(swatch => swatch.Visible) == colors.Count &&
                    colors.Select(color => color.Name).SequenceEqual(swatches.Take(colors.Count).Select(swatch => swatch.TooltipText)),
                $"every {mount.Name} color is visible in the stable picker");
            string previous = MountColorFingerprint(showcase!.Mount);
            int index = SelectedMountColor == colors.Count - 1 ? 0 : colors.Count - 1;
            swatches[index].EmitSignal(Button.SignalName.Pressed);
            Require(SelectedMountColor == index && stableName.Text == colors[index].Name && swatches[index].Text == "✓" &&
                    showcase!.Mount.AppearanceWarnings.Count == 0 && MountColorFingerprint(showcase.Mount) != previous,
                $"{mount.Name} swatch changes the rendered preview");
        }

        int selected = MountColorFor(RaceMountKind.CarenziSkates);
        SelectMount(RaceMountKind.CarenziSkates);
        Require(SelectedMountColor == selected && swatches[selected].Text == "✓",
            "switching back keeps the previous mount color");
        var file = new GarageSave(skinIndex, appearance, -1, datFolder,
            SiraluunSkin: MountColorFor(RaceMountKind.Siraluun), MountColors: mountColors);
        var saved = JsonSerializer.Deserialize<GarageSave>(JsonSerializer.Serialize(file))!;
        RestoreMountColors(saved);
        Require(MountColorFor(RaceMountKind.CarenziSkates) == selected &&
                RaceMount.All.All(mount => mount.Kind == RaceMountKind.Gromnie || mountColors.ContainsKey(mount.Kind)),
            "garage save round-trips independent mount colors");

        var local = showcase!;
        var profile = new OnlineLoadout("Skate racer", (int)RaceMountKind.CarenziSkates, selected, weaponIndex, shieldIndex, appearance.Copy());
        var remote = CreateOnlineRacer(JsonSerializer.Deserialize<OnlineLoadout>(JsonSerializer.Serialize(profile))!);
        try
        {
            Require(MountColorFingerprint(remote.Mount) == MountColorFingerprint(local.Mount) &&
                    MountColorFingerprint(remote.CompanionSkate!) == MountColorFingerprint(local.CompanionSkate!),
                "online racers render the chosen color on both Carenzi");
        }
        finally { remote.Root.Free(); }
        StartRace();
        Require(racing && MountColorFingerprint(Player.Mount) == MountColorFingerprint(local.Mount) &&
                MountColorFingerprint(Player.CompanionSkate!) == MountColorFingerprint(local.CompanionSkate!),
            "the color of both Carenzi survives entering a race");
        OpenStable();
        GD.Print($"MOUNT COLORS PASS | {RaceMount.All.Length} mounts, {RaceMount.All.Sum(mount => RaceMount.ColorsFor(mount.Kind).Count)} choices, legacy saves, local race and online replication.");
    }

    private static string MountColorFingerprint(AcActor actor)
    {
        var textures = Enumerable.Range(0, actor.Setup.Parts.Length).Select(actor.PartTextureFingerprint);
        var materials = actor.BodyMeshes.SelectMany(mesh =>
            Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0).Select(index =>
                (mesh.GetActiveMaterial(index) as StandardMaterial3D)?.AlbedoColor.ToHtml() ?? ""));
        return $"{string.Join(',', textures)}/{string.Join(',', materials)}";
    }

    private async Task CaptureMountColors(string folder)
    {
        Require(loaded, "DAT load for mount colors preview");
        SelectMount(RaceMountKind.Gromnie);
        SelectSkin(RaceMount.ColorsFor(RaceMountKind.Gromnie).Count - 1);
        SelectMount(RaceMountKind.CarenziSkates);
        SelectSkin(RaceMount.ColorsFor(RaceMountKind.CarenziSkates).Count - 1);
        SelectMount(RaceMountKind.Gromnie);
        await SaveFrame(Path.Combine(folder, "Gromnie-Colors.png"));
        SelectMount(RaceMountKind.CarenziSkates);
        await SaveFrame(Path.Combine(folder, "Carenzi-Colors.png"));
        GD.Print("MOUNT COLORS CAPTURE PASS");
    }
}
