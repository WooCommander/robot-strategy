using System;
using System.Diagnostics;
using Game.Content.Definitions;
using Game.Core.Models;
using Game.Core.Types;
using Game.Simulation.Systems;

var map = DefaultMapFactory.CreatePrototypeMap();
var state = MatchSimulation.CreateInitialState(map);
for (int i = 0; i < 80; i++)
{
    state.Robots[1000 + i] = new RobotState
    {
        Id = new EntityId(1000 + i),
        Owner = i < 40 ? PlayerId.Player1 : PlayerId.Player2,
        CurrentHealth = 100, MaxHealth = 100,
        Position = new SimVector2(12f + i % 20 * 5f, 8f + i / 20 * 18f)
    };
}

var vision = new FogOfWarSystem(map);
for (int i = 0; i < 20; i++) vision.Update(state);
for (int run = 0; run < 3; run++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var watch = Stopwatch.StartNew();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int tick = 0; tick < 200; tick++) vision.Update(state);
    watch.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"run {run + 1}: {allocated} bytes/200 ticks; {watch.Elapsed.TotalMilliseconds / 200:0.000} ms/tick");
}
