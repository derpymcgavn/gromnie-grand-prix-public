using Godot;

public partial class GrandPrix
{
    private int shieldIndex;
    private bool requestGuard;

    private void LoadCombatIcons()
    {
        for (int i = 0; i < RaceWeapon.All.Length; i++) SetIcon(weaponPicker, i, RaceWeapon.All[i].Icon);
        for (int i = 1; i < RaceShield.All.Length; i++) SetIcon(shieldPicker, i, RaceShield.All[i].Icon);

        void SetIcon(OptionButton picker, int index, uint icon)
        {
            if (archive!.Entries.ContainsKey(icon) && assets.Texture(icon) is { } texture)
                picker.SetItemIcon(index, texture);
        }
    }

    private void UpdateShieldUi()
    {
        var shield = Player.Shield;
        shieldHud.Visible = shield.Look is not null;
        if (!shieldHud.Visible) return;
        shieldStatus.Text = Player.GuardWindow > 0 ? "SHIELD   ·   GUARDING"
            : Player.GuardCooldown > 0 ? $"SHIELD   ·   {Player.GuardCooldown:0.0}s"
            : raceTouchUi is null ? "SHIELD   ·   C READY" : "SHIELD   ·   READY";
        shieldMeter.Color = Player.GuardWindow > 0 ? shield.Tint : new Color("84eac6");
        float progress = Player.GuardWindow > 0 ? Player.GuardWindow / shield.Window
            : 1 - Player.GuardCooldown / shield.Recharge;
        shieldMeter.Size = new Vector2(149 * Mathf.Clamp(progress, 0, 1), 8);
    }

    private void TryGuard(MountedRacer racer)
    {
        var shield = racer.Shield;
        if (shield.Look is null || racer.GuardCooldown > 0 || racer.SwingTime > 0 || racer.Hexed > 0 ||
            racer.DodgesItems || racer.ResetHold > 0 || racer.EjectTime > 0 || racer.Finish >= 0) return;
        racer.GuardWindow = shield.Window;
        racer.GuardCooldown = shield.Recharge;
        Wave(RiderTarget(racer), shield.Tint, 2.4f, shield.Window, new(.45f, 0, .18f));
    }

    private bool BlockWithShield(MountedRacer racer, ShieldImpact impact)
    {
        var shield = racer.Shield;
        if (racer.GuardWindow <= 0 || !shield.Blocks(impact)) return false;
        racer.GuardWindow = 0;
        racer.GuardFlash = .22f;
        if (shield.CounterBoost > 0) GrantBoost(racer, shield.CounterBoost);
        Vector3 spot = RiderTarget(racer);
        Wave(spot, shield.Tint, 2.8f, .48f);
        Wave(spot, Colors.White, 1.65f, .24f, new(.32f, 0, .58f));
        if (racer == Player)
            Callout(shield.CounterBoost > 0 ? impact == ShieldImpact.Spell ? "SPELL ABSORBED · BOOST!" : "PARRY BOOST!" : "SHIELD BLOCK!");
        return true;
    }

    private void TickShields(float dt)
    {
        foreach (var racer in racers)
        {
            racer.GuardWindow = Math.Max(0, racer.GuardWindow - dt);
            racer.GuardCooldown = Math.Max(0, racer.GuardCooldown - dt);
            racer.GuardFlash = Math.Max(0, racer.GuardFlash - dt);
        }
        if (requestGuard) TryGuard(Player);
        requestGuard = false;
    }

    private void DecideRivalGuard(MountedRacer racer, RivalDriver driver, RivalContact[] contacts, List<RivalObstacle> obstacles)
    {
        var shield = racer.Shield;
        if (shield.Look is null || racer.GuardCooldown > 0 || racer.Ward > 0 || racer.SwingTime > 0) return;
        bool swing = shield.Blocks(ShieldImpact.Swipe) && contacts.Any(c => c.Range < 5 &&
            c.Racer.SwingTime > 0 && !c.Racer.SwingLanded && c.Racer.Weapon.Shot == SideShotKind.None &&
            FindSwipeTarget(c.Racer, c.Racer.SwingSide) == racer);
        bool danger = IncomingRivalThreat(racer, shield) <= 7.5f ||
            (shield.Blocks(ShieldImpact.Collision) && driver.Style.Nerve < .72f &&
             obstacles.Any(o => o.Ahead < 5 && Math.Abs(o.Lane - racer.Lane) < o.Radius));
        if (swing || danger) TryGuard(racer);
    }
}
