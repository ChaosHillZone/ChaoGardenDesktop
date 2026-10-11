using System;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Simulation.Models.World;

namespace ChaoGarden.Simulation.Desktop;

public enum DesktopActivity
{
    Idle, WalkLeft, WalkRight, SitDown, Sitting, LieDown, Sleeping, WakeUp, Petting, Eating, Satisfied,
    StandUp, SittingLook, SittingScratch, SleepStart, RollToBelly, SleepBelly, RollToBack,
    EatSitDown, EatStart, EatFast, EatLook, EatResume,
    CrawlSitDown, CrawlStart, CrawlGetUp, TurnLeft, TurnRight, Flying, Climbing, Falling, Landing, LandGetUp, GameAnimation, Held, FruitPickup
}

/// <summary>Desktop-only persisted state. The garden's data and rules remain reusable.</summary>
public sealed class DesktopChaoState
{
    public DesktopPastimeState Pastime { get; set; } = new();
    public bool CatalogReturnSeated { get; set; }
    public string CatalogActivityId { get; set; } = "";
    public string CatalogClip { get; set; } = "";
    public int? Sa2Slot { get; set; }
    public string? LocalName { get; set; }
    public ChaoType? LocalType { get; set; }
    public int SchemaVersion { get; set; } = 2;
    public ChaoEntity Chao { get; set; } = new()
    {
        Name = "Chao",
        ChaoType = ChaoType.Child,
        Alignment = 0,
        Lifespan1 = 3000,
        Lifespan2 = 3000
    };
    public DesktopChaoSettings Settings { get; set; } = new();
    public DesktopActivity Activity { get; set; }
    public int ActivityTicks { get; set; }
    public bool MotionInitialized { get; set; }
    public int ActivityDuration { get; set; } = 5400;
    public int RestTicksRemaining { get; set; }
    public int SleepCycles { get; set; }
    public bool WakeRequested { get; set; }
    public DesktopActivity AfterStanding { get; set; }
    public long TotalTicks { get; set; }
    public double Position { get; set; } = .5;
    public double Altitude { get; set; }
    public float FacingYaw { get; set; }
    public ChaoAnimation TravelGait { get; set; } = ChaoAnimation.LandWalk;
    public DesktopActivity TravelDirection { get; set; } = DesktopActivity.WalkRight;
    public int TravelTicksRemaining { get; set; } = 2100;
    public DesktopActivity AfterCrawling { get; set; } = DesktopActivity.Sitting;
    public DesktopActivity AfterLanding { get; set; } = DesktopActivity.Idle;
    public FruitType EatingFruitType { get; set; } = FruitType.ChaoGardenFruit;
    public Guid? LastAcquiredFruitId { get; set; }
    public Guid? TargetFruitId { get; set; }
    public int ForageTicks { get; set; }
    public int RemainingBites { get; set; }
    public int BiteTicks { get; set; }
    public RandomState Random { get; set; } = new() { Seed = 0x4348414f };
}

public enum DesktopRenderingMode { SpriteSheets, Live3D }

public sealed class DesktopChaoSettings
{
    // Retained for legacy JSON compatibility. The desktop always renders live 3D.
    public DesktopRenderingMode RenderingMode { get; set; } = DesktopRenderingMode.Live3D;
    public bool AlwaysOnTop { get; set; }
    public bool FollowCursor { get; set; } = true;
    public bool PauseWhenMinimized { get; set; } = true;
    public bool Paused { get; set; }
    public int Size { get; set; } = 240;
    public string Monitor { get; set; } = "";
    // Retain the JSON field for older saves, but playback is always 60 fps.
    public int RenderFramesPerSecond => 60;
}
