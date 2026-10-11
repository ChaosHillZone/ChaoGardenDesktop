using System;
using System.Collections.Generic;

namespace ChaoGarden.Simulation.Desktop;

public enum DesktopLoop { Once, Loop, PingPong }

/// <summary>SA2 source-frame ranges and rates, sampled at one output frame per simulation tick.</summary>
public sealed record DesktopMotion(string Name, string Clip, float Start, float End, float Rate,
    DesktopLoop Loop = DesktopLoop.Once, bool Cursor = false, float Yaw = 0)
{
    public int Frames => Math.Max(2, (int)Math.Ceiling(Math.Abs(End - Start) / Rate
        * (Loop == DesktopLoop.PingPong ? 2 : 1)) + (Loop == DesktopLoop.Once ? 1 : 0));
}

public static class DesktopMotions
{
    // Ranges/rates follow CoreChaoMotionTable's native rest, sleep and feeding profiles.
    // Walk uses native land motion 100 (.23 source frames/tick), not the generic HD walk rate.
    public static readonly IReadOnlyDictionary<string, DesktopMotion> All = Build();
    private static Dictionary<string, DesktopMotion> Build()
    {
        DesktopMotion[] entries =
        [
            new("idle", "alm_tati", 0, 13, .12f, DesktopLoop.Loop, true),
            new("walk-left", "alm_aruki", 0, 11, .23f, DesktopLoop.Loop, true, -65),
            new("walk-right", "alm_aruki", 0, 11, .23f, DesktopLoop.Loop, true, 65),
            new("crawl-start", "alm_haihai", 0, 15, .15f),
            new("crawl-left", "alm_haihai", 15, 17, .07f, DesktopLoop.PingPong, false, -65),
            new("crawl-right", "alm_haihai", 15, 17, .07f, DesktopLoop.PingPong, false, 65),
            new("run-left", "alm_hasiru", 12, 23, .18f, DesktopLoop.Loop, false, -65),
            new("run-right", "alm_hasiru", 12, 23, .18f, DesktopLoop.Loop, false, 65),
            new("run-fast-left", "alm_hasiru", 24, 35, .18f, DesktopLoop.Loop, false, -65),
            new("run-fast-right", "alm_hasiru", 24, 35, .18f, DesktopLoop.Loop, false, 65),
            new("flying", "alm_tobu", 0, 13, .2f, DesktopLoop.Loop),
            new("climbing", "alm_noboru", 0, 7, .15f, DesktopLoop.Loop),
            new("falling", "alm_habatobi", 0, 9, .15f),
            new("landing", "alm_tati", 0, 13, .12f),
            new("eat-pickup", "alm_hirou", 0, 13, .15f),
            new("eat-sit-down", "alm_osuwari", 0, 12, .17f),
            new("land-get-up", "alm_kokeoki", 0, 9, .17f),
            new("sit-down", "alm_osuwari", 0, 12, .17f),
            new("stand-up", "alm_osuwari", 12, 0, .17f),
            new("sitting", "alm_hima_suwari", 0, .5f, .01f, DesktopLoop.PingPong, true),
            new("sit-look", "alm_hima_suwari", 14, 24, .08f),
            new("sit-scratch", "alm_hima_suwari", 28, 31, .1f, DesktopLoop.PingPong),
            new("lie-down", "alm_neru", 0, 13, .2f, Yaw: 30),
            new("sleep-start", "alm_sleep", 0, 6, .1f, Yaw: 30),
            new("sleeping", "alm_sleep", 6, 20, .07f, DesktopLoop.Loop, Yaw: 30),
            new("roll-belly", "alm_negaeri_l", 0, 12, .1f, Yaw: 30),
            new("sleep-belly", "alm_negaeri_l", 12, 15, .05f, DesktopLoop.PingPong, Yaw: 30),
            new("roll-back", "alm_negaeri_l", 12, 0, .1f, Yaw: 30),
            new("wake-up", "alm_okiru", 0, 15, .1f, Yaw: 30),
            new("petting", "alm_nade_kimotii", 0, 7, .08f, DesktopLoop.Loop, true),
            new("satisfied", "alm_manpuku", 4, 5, .006f, DesktopLoop.PingPong),
            new("eat-start", "alm_taberu", 0, 4, .15f),
            new("eat-slow", "alm_eat_kyoro", 9, 15, .13f, DesktopLoop.Loop),
            new("eat-fast", "alm_eat_gatugatu", 0, 16, .16f, DesktopLoop.Loop),
            new("eat-look", "alm_eat_kyoro", 15, 22, .06f, DesktopLoop.PingPong),
            new("eat-resume", "alm_taberu", 19, 16, .1f)
        ];
        Dictionary<string, DesktopMotion> result = new();
        foreach (DesktopMotion entry in entries) result.Add(entry.Name, entry);
        return result;
    }
}
