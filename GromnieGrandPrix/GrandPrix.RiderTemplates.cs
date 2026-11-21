using System.Text.Json;
using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const string RiderTemplateFile = "user://rider-templates.json";
    private const int RiderTemplateNameLength = 56;
    private sealed record RiderTemplate(string Name, AceRiderSnapshot? Imported = null, CharacterAppearance? Custom = null);
    private List<RiderTemplate> riderTemplates = [];
    private bool riderTemplatesLoaded;
    private Control templatePane = null!;
    private ItemList riderTemplateList = null!;
    private Label riderTemplateCurrent = null!, riderTemplateStatus = null!;
    private LineEdit riderTemplateName = null!;
    private Button riderTemplateUse = null!, riderTemplateRemove = null!;

    private void BuildRiderTemplates()
    {
        templatePane = RiderImportPane("Templates");
        riderTemplateCurrent = Text(templatePane, "", 420, 232, 440, 15, Soft);
        riderTemplateCurrent.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        riderTemplateList = RiderImportList(templatePane, new(420, 259), new(440, 170));
        riderTemplateList.ItemSelected += _ => UpdateRiderTemplateButtons();
        riderTemplateList.ItemActivated += _ => UseRiderTemplate();

        riderTemplateUse = Action(templatePane, "USE SELECTED", 420, 442, 310, 42, UseRiderTemplate);
        riderTemplateRemove = Action(templatePane, "REMOVE", 742, 442, 118, 42, RemoveRiderTemplate);
        riderTemplateName = new LineEdit
        {
            Position = new(420, 497), Size = new(254, 40),
            PlaceholderText = "Name for current appearance", MaxLength = RiderTemplateNameLength, Theme = ThemeForUi()
        };
        riderTemplateName.TextSubmitted += _ => SaveCurrentRiderTemplate();
        templatePane.AddChild(riderTemplateName);
        Action(templatePane, "SAVE CURRENT", 686, 497, 174, 40, SaveCurrentRiderTemplate, true);
        riderTemplateStatus = Text(templatePane, "", 420, 548, 440, 15, Soft);
        riderTemplateStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        templatePane.Hide();
    }

    private void LoadRiderTemplates()
    {
        if (riderTemplatesLoaded) return;
        riderTemplatesLoaded = true;
        if (testMode || !Godot.FileAccess.FileExists(RiderTemplateFile))
        {
            // Preserve the previously imported rider as the first reusable template.
            if (aceRider is { } previous) RememberImportedRiderTemplate(previous);
            return;
        }
        try
        {
            riderTemplates = ParseRiderTemplates(Godot.FileAccess.GetFileAsString(RiderTemplateFile));
        }
        catch (JsonException)
        {
            riderTemplateStatus.Text = "Could not read the local rider templates.";
        }
    }

    private static List<RiderTemplate> ParseRiderTemplates(string json) =>
        (JsonSerializer.Deserialize<RiderTemplate[]>(json) ?? [])
            .Where(template => template is not null && ValidRiderTemplateName(template.Name) &&
                (template.Imported is null) != (template.Custom is null))
            .DistinctBy(template => template.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool ValidRiderTemplateName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= RiderTemplateNameLength && !name.Any(char.IsControl);

    private bool StoreRiderTemplates(List<RiderTemplate> next, string? selected = null)
    {
        if (!testMode)
        {
            string path = ProjectSettings.GlobalizePath(RiderTemplateFile);
            try
            {
                System.IO.File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next));
                System.IO.File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        riderTemplates = next;
        RefreshRiderTemplates(selected);
        return true;
    }

    private bool RememberImportedRiderTemplate(AceRiderSnapshot snapshot)
    {
        var sameLook = riderTemplates.Find(template => template.Imported is { } imported &&
            imported.Name == snapshot.Name && imported.Setup == snapshot.Setup && imported.Motion == snapshot.Motion &&
            imported.Sound == snapshot.Sound && imported.Scale == snapshot.Scale && imported.Palette == snapshot.Palette &&
            imported.Palettes is not null && imported.Palettes.SequenceEqual(snapshot.Palettes) &&
            imported.Textures is not null && imported.Textures.SequenceEqual(snapshot.Textures) &&
            imported.Parts is not null && imported.Parts.SequenceEqual(snapshot.Parts));
        if (sameLook is not null)
        {
            RefreshRiderTemplates(sameLook.Name);
            return true;
        }
        string baseName = snapshot.Name.Trim();
        baseName = baseName[..Math.Min(baseName.Length, RiderTemplateNameLength - 8)];
        string name = baseName;
        for (int copy = 2; riderTemplates.Any(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)); copy++)
            name = $"{baseName} ({copy})";
        return StoreRiderTemplates([.. riderTemplates, new(name, Imported: snapshot)], name);
    }

    private void RefreshRiderTemplates(string? select = null)
    {
        riderTemplateCurrent.Text = "Current rider · " + (aceRider?.Name ?? "Custom rider");
        riderTemplateList.Clear();
        foreach (var template in riderTemplates)
        {
            int row = riderTemplateList.AddItem(template.Name);
            riderTemplateList.SetItemTooltip(row, template.Imported?.Name ?? "Custom rider");
            if (template.Name == select) riderTemplateList.Select(row);
        }
        if (riderTemplateList.ItemCount > 0 && riderTemplateList.GetSelectedItems().Length == 0)
            riderTemplateList.Select(0);
        UpdateRiderTemplateButtons();
    }

    private void UpdateRiderTemplateButtons()
    {
        bool canUse = riderTemplateList.GetSelectedItems().Length != 0;
        riderTemplateUse.Disabled = riderTemplateRemove.Disabled = !canUse;
    }

    private void SaveCurrentRiderTemplate()
    {
        string name = riderTemplateName.Text.Trim();
        if (!ValidRiderTemplateName(name))
        {
            riderTemplateStatus.Text = "Enter a name for this appearance.";
            return;
        }
        if (riderTemplates.Any(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            riderTemplateStatus.Text = "That name is already in use.";
            return;
        }
        var row = aceRider is { } imported ? new RiderTemplate(name, Imported: imported) :
            new RiderTemplate(name, Custom: appearance.Copy());
        if (StoreRiderTemplates([.. riderTemplates, row], name))
        {
            riderTemplateName.Clear();
            riderTemplateStatus.Text = $"Saved {name}.";
        }
        else riderTemplateStatus.Text = "Could not save the template to this device.";
    }

    private RiderTemplate? SelectedRiderTemplate()
    {
        int[] selection = riderTemplateList.GetSelectedItems();
        return selection.Length == 0 ? null : riderTemplates[selection[0]];
    }

    private void UseRiderTemplate()
    {
        if (SelectedRiderTemplate() is not { } template) return;
        if (template.Imported is { } imported)
        {
            if (!TryUseImportedRider(imported, out string failure))
            {
                riderTemplateStatus.Text = failure;
                return;
            }
        }
        else if (template.Custom is { } custom && creation.Validate(custom, out _))
        {
            RebuildShowcase(creation.Resolve(custom), "You");
            appearance = custom.Copy();
            aceRider = null;
            FillStable();
            SaveGarage();
        }
        else
        {
            riderTemplateStatus.Text = "This saved rider is unavailable with the current DATs.";
            return;
        }
        CloseAceRider();
    }

    private void RemoveRiderTemplate()
    {
        if (SelectedRiderTemplate() is not { } template) return;
        riderTemplateStatus.Text = StoreRiderTemplates(riderTemplates.Where(row => row != template).ToList())
            ? $"Removed {template.Name}." : "Could not update the templates on this device.";
    }
}
