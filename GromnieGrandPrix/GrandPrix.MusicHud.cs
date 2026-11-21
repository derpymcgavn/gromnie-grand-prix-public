using Godot;

public partial class GrandPrix
{
    private NowPlayingHud musicHud = null!;

    private void MakeNowPlayingHud()
    {
        musicHud = new NowPlayingHud(this)
        {
            Name = "Now Playing", Position = new(28, 222), Size = new(248, 62),
            MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Theme = ThemeForUi()
        };
        raceUi.AddChild(musicHud);
    }

    private sealed partial class NowPlayingHud(GrandPrix game) : Control
    {
        private const int SampleCount = 80;
        private const double UpdateInterval = 1.0 / 30;
        private static readonly Vector2 RaceSlot = new(28, 222), RabbitCameraSlot = new(28, 415);
        private static readonly Color Screen = new("091a17d8"), Grid = new("73d8a522"),
            Baseline = new("73d8a553"), Trace = new("a9f4cf"), TraceGlow = new("7ce7ae50");
        private readonly Vector2[] trace = new Vector2[SampleCount];
        private Label title = null!;
        private Control scope = null!;
        private double accumulator;
        internal string TrackTitle => title.Text;
        internal bool HasAudioSignal { get; private set; }

        public override void _Ready()
        {
            Plate(this, 0, 0, Size.X, Size.Y, "172821ec", "637951");
            title = game.Text(this, "", 12, 6, 224, 15);
            title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            game.Text(this, $"© {SoundtrackArtist}", 12, 34, 82, 12, Soft);
            scope = new Control { Position = new(91, 31), Size = new(145, 22), MouseFilter = MouseFilterEnum.Ignore };
            scope.Draw += DrawScope;
            AddChild(scope);
            Plot([]);
        }

        public override void _Process(double delta)
        {
            accumulator += delta;
            if (accumulator < UpdateInterval) return;
            accumulator %= UpdateInterval;

            string trackTitle = game.CurrentMusicTitle;
            var capture = game.MusicCapture;
            Visible = game.ShowMusicHud && game.raceUi.Visible && !game.paused && trackTitle.Length > 0;
            if (!IsVisibleInTree())
            {
                capture?.ClearBuffer();
                return;
            }
            Position = game.rabbitCameraPanel.Visible ? RabbitCameraSlot : RaceSlot;
            if (title.Text != trackTitle)
            {
                title.Text = trackTitle;
                capture?.ClearBuffer();
            }
            int available = capture?.GetFramesAvailable() ?? 0;
            int musicBus = AudioServer.GetBusIndex(MusicBusName);
            if (available <= 0 || musicBus < 0 || AudioServer.IsBusMute(musicBus) || AudioServer.IsBusMute(0))
            {
                capture?.ClearBuffer();
                Plot([]);
                return;
            }

            var stereo = capture!.GetBuffer(available);
            int window = Math.Min(stereo.Length, (int)(AudioServer.GetMixRate() * .028f));
            Plot(stereo.AsSpan(stereo.Length - window));
        }

        private void Plot(ReadOnlySpan<Vector2> stereo)
        {
            HasAudioSignal = false;
            float midline = scope.Size.Y * .5f;
            for (int i = 0; i < trace.Length; i++)
            {
                float x = i / (float)(trace.Length - 1);
                float amplitude = 0;
                if (!stereo.IsEmpty)
                {
                    var frame = stereo[(int)(x * (stereo.Length - 1))];
                    amplitude = (frame.X + frame.Y) * .5f;
                    HasAudioSignal |= Mathf.Abs(amplitude) > .004f;
                }
                trace[i] = new Vector2(3 + x * (scope.Size.X - 6), midline - Mathf.Clamp(amplitude * 2.2f, -1, 1) * (midline - 3));
            }
            scope.QueueRedraw();
        }

        private void DrawScope()
        {
            scope.DrawRect(new Rect2(Vector2.Zero, scope.Size), Screen);
            for (int i = 1; i < 5; i++)
            {
                float x = scope.Size.X * i / 5;
                scope.DrawLine(new(x, 1), new(x, scope.Size.Y - 1), Grid);
            }
            float y = scope.Size.Y * .5f;
            scope.DrawLine(new(3, y), new(scope.Size.X - 3, y), Baseline);
            if (!HasAudioSignal) return;
            scope.DrawPolyline(trace, TraceGlow, 3.6f, true);
            scope.DrawPolyline(trace, Trace, 1.15f, true);
        }
    }
}
