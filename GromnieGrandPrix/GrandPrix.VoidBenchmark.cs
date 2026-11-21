using System.Diagnostics;
using Godot;

public partial class GrandPrix
{
    private async Task BenchmarkVoidCourse()
    {
        StartVoidCheck();
        SetPhysicsProcess(false);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        GD.Print($"VOID BENCHMARK | window {DisplayServer.WindowGetSize()} | viewport {GetViewport().GetVisibleRect().Size} | milliseconds");

        var redSite = voidBattle!.Attacks[0].Sites[0];
        long drawStart = 0;
        void BeginDraw() => drawStart = Stopwatch.GetTimestamp();
        RenderingServer.Singleton.FramePreDraw += BeginDraw;
        try
        {
            await Measure("battle approach", redSite.Station - 80, .85f);
            await Measure("outer orbit", track.Length * .513f, 7.5f);
            await Measure("flyover", VoidFlyover(track).Start, 7.8f, fly: true);
        }
        finally
        {
            RenderingServer.Singleton.FramePreDraw -= BeginDraw;
        }

        async Task Measure(string name, float station, float age, bool fly = false)
        {
            const int warmup = 60, samples = 240;
            const float step = 1f / 60;
            ResetRace();
            countdown = 0;
            elapsed = age;
            raceView = RaceView.Chase;
            for (int i = 0; i < racers.Count; i++)
            {
                var racer = racers[i];
                racer.Distance = station + i * 8.4f;
                racer.Lane = i == 0 ? 0 : (i - 2.5f) * 4.4f;
                racer.Speed = 29 + i * .32f;
                racer.GatesCleared = Enumerable.Range(0, track.GateCount).Count(gate => track.Gate(gate) <= racer.Distance);
                racer.Checkpoint = racer.GatesCleared == 0 ? 0 : track.Gate(racer.GatesCleared - 1);
                racer.Draw(track, 0, true);
            }
            if (fly) Require(LaunchFlight(Player, VoidFlyover(track)), "benchmark starts the flyover");
            SetRaceCamera(1);

            var tick = new double[samples];
            var cameraAndHud = new double[samples];
            var frameTimes = new double[samples];
            var stalls = new List<string>();
            var heldBefore = new RaceItem[racers.Count];
            for (int frame = -warmup; frame < samples; frame++)
            {
                for (int i = 0; i < racers.Count; i++) heldBefore[i] = racers[i].Item;
                long assetMisses = assets.ColdCacheMisses;
                int collections = GC.CollectionCount(0);
                long start = Stopwatch.GetTimestamp();
                worldTime += step;
                float lane = name == "battle approach" ? 9f : 1.75f;
                float steer = Mathf.Clamp((lane - Player.Lane) * .39f, -.9f, .9f);
                bool sudsed = IsSudsed(Player);
                TickRace(step, !sudsed, sudsed, sudsed ? -steer : steer, false, false);
                long tickEnd = Stopwatch.GetTimestamp();
                SetRaceCamera(1 - Mathf.Exp(-8.2f * step));
                UpdateSpanReflection();
                UpdateSpeedBlur(step);
                UpdateRaceUi();
                long updated = Stopwatch.GetTimestamp();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                long end = Stopwatch.GetTimestamp();
                if (frame < 0) continue;
                tick[frame] = Stopwatch.GetElapsedTime(start, tickEnd).TotalMilliseconds;
                cameraAndHud[frame] = Stopwatch.GetElapsedTime(tickEnd, updated).TotalMilliseconds;
                frameTimes[frame] = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
                if (frameTimes[frame] > 22)
                {
                    double dispatch = drawStart >= updated ? Stopwatch.GetElapsedTime(updated, drawStart).TotalMilliseconds : 0;
                    double render = drawStart >= updated ? Stopwatch.GetElapsedTime(drawStart, end).TotalMilliseconds : 0;
                    string items = string.Join(", ", racers.Select((racer, i) => racer.Item == heldBefore[i] ? "" : $"{heldBefore[i]} → {racer.Item}")
                        .Where(change => change.Length > 0));
                    stalls.Add(FormattableString.Invariant($"frame {frame} clock {elapsed:F2} total {frameTimes[frame]:F1} tick {tick[frame]:F1} dispatch {dispatch:F1} draw {render:F1} GC {GC.CollectionCount(0) - collections} cold DAT reads {assets.ColdCacheMisses - assetMisses} {items}"));
                }
            }
            GD.Print($"VOID BENCHMARK | {name} | {Stats("race tick", tick)} | {Stats("camera/HUD", cameraAndHud)} | {Stats("frame", frameTimes)}");
            foreach (string stall in stalls) GD.Print($"VOID STALL | {name} | {stall}");
        }

        static string Stats(string label, double[] durations)
        {
            Array.Sort(durations);
            int overBudget = durations.Count(value => value > 1000d / 60);
            return FormattableString.Invariant($"{label} p50={durations[durations.Length / 2]:F2} p95={durations[(int)(durations.Length * .95)]:F2} p99={durations[(int)(durations.Length * .99)]:F2} >16.7ms={overBudget}/{durations.Length}");
        }
    }
}
