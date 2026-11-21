using Godot;

public partial class GrandPrix
{
    private const float RabbitCheckStep = 1f / 60f;

    private void CheckWhiteRabbitNavigation()
    {
        Require(loaded, "DAT load for White Rabbit navigation");
        ChooseCourse(0);
        StartRace();
        CheckWhiteRabbit();
        CheckRabbitOffRoadPursuit();
        CheckRabbitRetargets();

        StartVoidCheck();
        var top = track.Project(new Vector3(-298, 162, 133), 825);
        var bottom = track.Project(new Vector3(-298, 40, 133), 3790);
        CheckRabbitOnDeck(top.Distance);
        CheckRabbitOnDeck(bottom.Distance);
        CheckRabbitOnDeck(track.Length);
        CheckRabbitWaitsAtLanding();
        GD.Print("WHITE RABBIT PASS | banked upper and lower decks, lap seam, off-road chase and rejoin, retargeting, landing intercept, and slow-motion smite.");
    }

    private MountedRacer SetRabbitCheckGrid(float station)
    {
        ResetRace();
        for (int index = 0; index < racers.Count; index++)
        {
            var racer = racers[index];
            racer.Distance = station + (index == 0 ? -11 : 15 + (index - 1) * 14);
            racer.GatesCleared = 1;
            while (racer.GatesCleared < track.GateCount * track.Laps && track.Gate(racer.GatesCleared) <= racer.Distance)
                racer.GatesCleared++;
            racer.Checkpoint = track.Gate(racer.GatesCleared - 1);
            racer.Draw(track, 0, false);
        }
        return racers[1];
    }

    private RabbitChase LaunchRabbitCheck(MountedRacer expected)
    {
        HoldItem(Player, RaceItem.WhiteRabbit);
        UseItem(Player);
        var chase = rabbits.Single();
        Require(chase.Target == expected && filmedRabbit == chase, "White Rabbit locks onto the next ranked racer");
        return chase;
    }

    private void CheckRabbitOnDeck(float station)
    {
        var target = SetRabbitCheckGrid(station);
        Player.Lane = -track.HalfWidth * .65f;
        target.Lane = track.HalfWidth * .63f;
        Player.Draw(track, 0, false);
        target.Draw(track, 0, false);
        var chase = LaunchRabbitCheck(target);
        bool crossed = false;
        for (int frame = 0; frame < 300 && rabbits.Count > 0; frame++)
        {
            TickItemEffects(RabbitCheckStep);
            if (!chase.Rabbit.Visible || chase.Smote) continue;
            Vector3 surface = track.Surface(chase.Distance, chase.Lane);
            Vector3 visible = chase.Root.Position;
            Require((surface + Vector3.Up * RabbitClearance).DistanceTo(visible) < .035f &&
                Math.Abs(track.RoadHeight(visible) - surface.Y) < .10f,
                "White Rabbit follows the station's actual banked deck through the crossing");
            crossed |= chase.Distance >= station;
        }
        Require(crossed && chase.Smote && target.EjectTime > 0 && rabbitReplay is not null,
            "the deck chase reaches its rival and plays Bunny Smite footage");
    }

    private void CheckRabbitOffRoadPursuit()
    {
        var target = SetRabbitCheckGrid(76);
        target.FreeDriving = true;
        target.Lane = track.WidthAt(target.Distance) + 4;
        target.FreePosition = track.FreeGround(track.Surface(target.Distance, target.Lane));
        target.Draw(track, 0, false);
        var chase = LaunchRabbitCheck(target);
        bool leftRoad = false;
        for (int frame = 0; frame < 300 && rabbits.Count > 0; frame++)
        {
            TickItemEffects(RabbitCheckStep);
            if (chase.Rabbit.Visible && chase.OffRoad) leftRoad = true;
        }
        Require(leftRoad && chase.Smote, "White Rabbit reaches a grounded rival beyond the road shoulder");

        target = SetRabbitCheckGrid(76);
        Player.FreeDriving = true;
        Player.Lane = track.HalfWidth + 4;
        Player.FreePosition = track.FreeGround(track.Surface(Player.Distance, Player.Lane));
        Player.Draw(track, 0, false);
        chase = LaunchRabbitCheck(target);
        Require(chase.OffRoad, "White Rabbit can be summoned beside the road");
        for (int frame = 0; frame < 300 && rabbits.Count > 0; frame++) TickItemEffects(RabbitCheckStep);
        Require(!chase.OffRoad && chase.Smote, "White Rabbit rejoins the road to catch a racer");
    }

    private void CheckRabbitWaitsAtLanding()
    {
        var route = VoidFlyover(track);
        var target = SetRabbitCheckGrid(route.Start - 14);
        Require(LaunchFlight(target, route), "the rabbit's rival launches on the real flyover");
        var ride = target.Flight!;
        var chase = LaunchRabbitCheck(target);
        for (int frame = 0; frame < 500 && Math.Abs(chase.Distance - ride.End) > .01f; frame++)
            TickItemEffects(RabbitCheckStep);
        Require(rabbits.Contains(chase) && Math.Abs(chase.Distance - ride.End) < .01f && !chase.Smote,
            "the rabbit runs the road to the flight landing");
        ride.Position = track.Surface(ride.End, 0) + Vector3.Up;
        for (int frame = 0; frame < 24; frame++) TickItemEffects(RabbitCheckStep);
        Require(rabbits.Contains(chase) && chase.Bite == 0, "the rabbit waits for the airborne target to land");
        target.Distance = ride.End;
        target.Land();
        target.Draw(track, 0, false);
        for (int frame = 0; frame < 60 && rabbits.Count > 0; frame++) TickItemEffects(RabbitCheckStep);
        Require(chase.Smote && target.EjectTime > 0, "landing puts the rival back in the rabbit's reach");
    }

    private void CheckRabbitRetargets()
    {
        var first = SetRabbitCheckGrid(76);
        var next = racers[2];
        var chase = LaunchRabbitCheck(first);
        first.Finish = 10;
        TickItemEffects(RabbitCheckStep);
        Require(rabbits.Single() == chase && chase.Target == next, "the rabbit finds another ranked rival if its target finishes");
        for (int frame = 0; frame < 300 && rabbits.Count > 0; frame++) TickItemEffects(RabbitCheckStep);
        Require(chase.Smote && first.EjectTime == 0 && next.EjectTime > 0, "the rabbit smites the new rival");
    }
}
