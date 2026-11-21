using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private Label colorLabel = null!;
    private ScrollContainer mountColorScroll = null!;
    private HBoxContainer mountColorRow = null!;
    private RaceMountKind? visibleMountPalette;
    private readonly List<Button> swatches = [];

    private static int ClampMountColor(RaceMountKind kind, int index) =>
        Math.Clamp(index, 0, RaceMount.ColorsFor(kind).Count - 1);

    private int MountColorFor(RaceMountKind kind) => ClampMountColor(kind,
        kind == RaceMountKind.Gromnie ? skinIndex : mountColors.GetValueOrDefault(kind));

    // Flights use the Gromnie body regardless of the active land mount.
    private RareCatalog.Creature GromnieLook(int index) =>
        RaceMount.Get(RaceMountKind.Gromnie).ResolveLook(skins[Math.Clamp(index, 0, skins.Length - 1)], index);

    private void RestoreMountColors(GarageSave save)
    {
        skinIndex = save.Skin;
        mountColors.Clear();
        mountColors[RaceMountKind.Siraluun] = save.SiraluunSkin;
        if (save.MountColors is null) return;
        foreach (var (kind, index) in save.MountColors)
            if (kind != RaceMountKind.Gromnie && Enum.IsDefined(kind)) mountColors[kind] = index;
    }

    private void BuildMountColorPicker()
    {
        colorLabel = Text(stableUi, "MOUNT COLOR", 53, 274, 176, 16, Cream);
        stableName = Text(stableUi, "", 230, 274, 238, 16, Gold, HorizontalAlignment.Right);
        stableName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        mountColorScroll = new ScrollContainer
        {
            Name = "MountColors", Position = new(53, 302), Size = new(415, 47),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, Theme = ThemeForUi()
        };
        stableUi.AddChild(mountColorScroll);
        mountColorRow = new HBoxContainer();
        mountColorRow.AddThemeConstantOverride("separation", 8);
        mountColorScroll.AddChild(mountColorRow);
    }

    private void SelectSkin(int index)
    {
        int color = ClampMountColor(selectedMount, index);
        if (color == SelectedMountColor) return;
        if (selectedMount == RaceMountKind.Gromnie) skinIndex = color;
        else mountColors[selectedMount] = color;
        ApplyLook(false);
    }

    private void UpdateMountColorPicker()
    {
        var colors = RaceMount.ColorsFor(selectedMount);
        int selected = SelectedMountColor;
        colorLabel.Text = selectedMount == RaceMountKind.Siraluun ? "SIRALUUN PLUMAGE" : "MOUNT COLOR";
        stableName.Text = colors[selected].Name;
        string mountName = RaceMount.Get(selectedMount).Name;
        portraitName.Text = selectedMount is RaceMountKind.Gromnie or RaceMountKind.Siraluun
            ? $"{colors[selected].Name} {mountName}" : mountName;

        while (swatches.Count < colors.Count)
        {
            int index = swatches.Count;
            var button = new Button { CustomMinimumSize = new(44, 38), Theme = ThemeForUi() };
            button.Pressed += () => SelectSkin(index);
            mountColorRow.AddChild(button);
            swatches.Add(button);
        }
        for (int i = 0; i < swatches.Count; i++)
        {
            var button = swatches[i];
            button.Visible = i < colors.Count;
            if (!button.Visible) continue;
            var color = colors[i];
            string rim = i == selected ? "ffe5a0" : "88a37a";
            button.TooltipText = color.Name;
            button.AddThemeStyleboxOverride("normal", Surface(color.Hex, rim, 8, i == selected ? 3 : 1));
            button.AddThemeStyleboxOverride("hover", Surface(color.Hex, "fff2c9", 8, 3));
            button.AddThemeStyleboxOverride("pressed", Surface(color.Hex, "fff2c9", 8, 3));
            button.Text = i == selected ? "✓" : "";
        }
        if (visibleMountPalette == selectedMount) return;
        visibleMountPalette = selectedMount;
        mountColorScroll.ScrollHorizontal = 0;
        mountColorScroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, swatches[selected]);
    }
}
