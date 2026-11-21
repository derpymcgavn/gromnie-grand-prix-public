using Godot;

public partial class GrandPrix
{
    private void CheckRaceLoadouts()
    {
        Require(loaded && weaponPicker.ItemCount == 7 && shieldPicker.ItemCount == 4, "the stable lists all racing loadouts");
        for (int i = 0; i < RaceWeapon.All.Length; i++)
        {
            weaponIndex = i;
            RebuildShowcase();
            Require(showcase!.Rider.HasVisibleHeldMesh && showcase.Rider.HeldSetup == RaceWeapon.All[i].Setup &&
                weaponPicker.GetItemIcon(i) is not null, $"the native {RaceWeapon.All[i].Name} renders in the stable");
        }
        for (int i = 1; i < RaceShield.All.Length; i++)
        {
            shieldIndex = i;
            RebuildShowcase();
            Require(showcase!.Rider.HasVisibleShieldMesh && showcase.Rider.ShieldAttachmentMatchesNative() &&
                shieldPicker.GetItemIcon(i) is not null, $"the native {RaceShield.All[i].Name} uses the off-hand attachment");
        }

        var spearTarget = SetUpLoadoutRace(3, 0, -3.85f);
        TrySwipe(Player, 1);
        TickRacerSwipe(Player, .21f);
        Require(spearTarget.Stability == 100, "a spear swung to the wrong side misses");
        Player.SwipeCooldown = 0;
        TrySwipe(Player, -1);
        TickRacerSwipe(Player, .12f);
        Require(spearTarget.Stability == 100, "the spear has a visible windup");
        TickRacerSwipe(Player, .07f);
        Require(spearTarget.Stability == 66 && spearTarget.Speed > 14, "a long spear thrust hits without a large speed penalty");

        var axeTarget = SetUpLoadoutRace(4, 0, 1.9f, 3.02f);
        TrySwipe(Player, 1);
        TickRacerSwipe(Player, .23f);
        Require(axeTarget.Stability == 57, "the greataxe hits a rival slightly ahead");

        var javelinTarget = SetUpLoadoutRace(5, 0, 6.4f);
        TrySwipe(Player, 1);
        TickRacerSwipe(Player, .20f);
        Require(sideShots.Count == 1 && javelinTarget.Stability == 100, "a javelin travels across the track before impact");
        for (int frame = 0; frame < 24 && sideShots.Count > 0; frame++) TickSideShots(1f / 60);
        Require(sideShots.Count == 0 && javelinTarget.Stability == 62, "the javelin hits a rival outside melee range");

        var staffTarget = SetUpLoadoutRace(6, 0, -5.8f);
        staffTarget.Boost = 3;
        TrySwipe(Player, -1);
        TickRacerSwipe(Player, .20f);
        for (int frame = 0; frame < 24 && sideShots.Count > 0; frame++) TickSideShots(1f / 60);
        Require(staffTarget.Stability == 72 && staffTarget.Boost <= .18f, "the Storm Staff jolts a rival out of boost");

        var attacker = SetUpLoadoutRace(0, 1, 2);
        PressGuard();
        Require(Player.GuardWindow > 0 && AcceptHit(Player, impact: ShieldImpact.Spell),
            "C opens the Round Shield guard against melee");
        TrySwipe(attacker, -1);
        TickRacerSwipe(attacker, .15f);
        Require(Player.Stability == 100 && Player.GuardWindow == 0 && Player.Boost > 0,
            "a real sideswipe is parried and rewards a boost");
        PressGuard();
        Require(Player.GuardWindow == 0, "a shield cannot rearm during recharge");
        TickShields(Player.Shield.Recharge + .01f);
        PressGuard();
        Require(Player.GuardWindow > 0, "a shield rearms after its recharge time");
        TickShields(Player.Shield.Window + .01f);
        Require(Player.GuardWindow == 0 && AcceptHit(Player, impact: ShieldImpact.Swipe), "a late melee hit gets through");

        SetUpLoadoutRace(0, 2, 8);
        PressGuard();
        Require(!AcceptHit(Player, impact: ShieldImpact.Collision) && AcceptHit(Player, impact: ShieldImpact.Collision),
            "the Tower Shield blocks exactly one collision");

        SetUpLoadoutRace(0, 3, 7);
        var caster = racers[4];
        caster.Finish = -1;
        caster.Distance = Player.Distance;
        caster.Lane = -5.3f;
        caster.Draw(track, 0, false);
        PressGuard();
        TrySwipe(caster, 1);
        TickRacerSwipe(caster, .22f);
        Require(sideShots.Count == 1, "rivals can launch the same native spell weapon");
        for (int frame = 0; frame < 24 && sideShots.Count > 0; frame++) TickSideShots(1f / 60);
        Require(Player.Stability == 100 && Player.GuardWindow == 0 && Player.Boost > 0,
            "the Prismatic Diamond Shield absorbs a real lightning projectile and converts it into boost");
        UpdateRaceUi();
        Require(shieldHud.Visible && shieldStatus.Text.Contains('s'), "the race HUD reports shield recharge");

        var defender = SetUpLoadoutRace(6, 0, 6.1f);
        defender.Speed = 0;
        TrySwipe(Player, 1);
        TickRacerSwipe(Player, .22f);
        PlanRival(defender, rivalDrivers[0], 1);
        Require(defender.GuardWindow > 0, "a rival notices a live incoming bolt and guards with its Tower Shield");
        for (int frame = 0; frame < 24 && sideShots.Count > 0; frame++) TickSideShots(1f / 60);
        Require(defender.Stability == 100 && defender.GuardWindow == 0, "the rival's guard blocks the bolt");

        shieldIndex = weaponIndex = 0;
        OpenStable();
        GD.Print("RACE LOADOUTS PASS | seven native weapons, three native shields, windups, side shots, timed guards and counter-boost.");
    }

    private MountedRacer SetUpLoadoutRace(int weapon, int shield, float lane, float ahead = 0)
    {
        weaponIndex = weapon;
        shieldIndex = shield;
        ChooseCourse(0);
        StartRace();
        countdown = 0;
        const float station = 68;
        Player.Distance = station;
        Player.Lane = 0;
        Player.Draw(track, 0, false);
        var target = racers[1];
        target.Distance = station + ahead;
        target.Lane = lane;
        target.Speed = 15;
        target.Draw(track, 0, false);
        foreach (var racer in racers.Skip(2)) racer.Finish = 1;
        return target;
    }

    private void PressGuard()
    {
        using var press = new InputEventKey { PhysicalKeycode = Key.C, Pressed = true };
        using var release = new InputEventKey { PhysicalKeycode = Key.C, Pressed = false };
        _Input(press);
        _Input(release);
        TickShields(1f / 60);
    }

    private async Task CaptureRaceLoadouts(string folder)
    {
        weaponIndex = 6;
        shieldIndex = 3;
        previewYaw = -2.42f;
        appearance = RivalLook(4);
        FillStable();
        RebuildShowcase();
        await SaveFrame(Path.Combine(folder, "Dereth-Race-Loadouts.png"));

        var rival = SetUpLoadoutRace(6, 3, 6.6f);
        Player.Speed = rival.Speed = 21;
        paused = true;
        TrySwipe(Player, 1);
        TickRacerSwipe(Player, .23f);
        TickSideShots(.07f);
        SetRaceCamera(1);
        UpdateRaceUi();
        await SaveFrame(Path.Combine(folder, "Dereth-Storm-Staff.png"));
    }
}
