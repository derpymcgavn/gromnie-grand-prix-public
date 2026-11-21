using DerethSurvivors;
using Godot;

internal sealed record OnlineLoadout(string Name, int Mount, int Skin, int Weapon, int Shield,
    CharacterAppearance Look, AceRiderSnapshot? Imported = null);
internal sealed record OnlineSlot(int Peer, OnlineLoadout Loadout);
internal sealed record OnlinePlan(int Epoch, int Course, int Weather, int Seed, OnlineSlot[] Slots, CustomTrack? Route = null);
internal sealed record OnlineLoaded(int Epoch, string? Error = null);
internal sealed record OnlineInput(int Epoch, int Serial, int Steer, int Held, int Actions);

internal readonly record struct OnlinePoint(float X, float Y, float Z)
{
    internal OnlinePoint(Vector3 value) : this(value.X, value.Y, value.Z) { }
    internal Vector3 Vector => new(X, Y, Z);
}

internal sealed record OnlineFlight(int Kind, OnlinePoint Position, float Yaw, float Age, float Pitch, float Bank,
    float Roll, float Recharge);
internal sealed record OnlineEjection(OnlinePoint Position, OnlinePoint Rotation, float Scale);
internal sealed record OnlineRacer(float Distance, float Lane, float Speed, float Lean, float Finish, float Checkpoint,
    int Gates, int Stability, int Item, int Charges, int Pyreals, float Boost, float Frost, float Ward, float Guard,
    float ShieldRecharge, float Reset, float Leap, float Spin, float Swing, int SwingSide, float Might, int Form,
    bool OffRoad, OnlinePoint Position, float Yaw, float Drift, bool Sudsed, OnlineFlight? Flight, OnlineEjection? Ejected,
    float Fall = 0, bool GroundDrift = false, float SuperDrift = 0, float[]? LapCrossings = null);
internal sealed record OnlineWave(OnlinePoint Position, uint Tint, float Radius, float Duration, OnlinePoint Tilt);
internal sealed record OnlineProp(ulong Id, int Kind, OnlinePoint Position, float Yaw);
internal sealed record OnlineState(int Epoch, int Frame, float Countdown, float Time, bool Complete, OnlineRacer[] Racers,
    int[] HiddenPickups, OnlineWave[] Waves, OnlineProp[] Props, int BattlePattern = 0);
