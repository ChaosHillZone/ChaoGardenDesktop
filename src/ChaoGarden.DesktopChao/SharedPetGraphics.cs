using ChaoGarden.ChaoPresentation;
namespace ChaoGarden.DesktopChao;

/// <summary>One UI-thread GPU device and asset cache for the entire desktop roster.</summary>
public sealed class SharedPetGraphics : IDisposable
{
    private readonly HiddenGraphicsHost graphics = new();
    public ChaoRenderSession Renderer { get; }
    public SharedPetGraphics(string resources)
    {
        try { Renderer = new ChaoRenderSession(graphics.Device, resources); }
        catch { graphics.Dispose(); throw; }
    }
    public void Dispose() { Renderer.Dispose(); graphics.Dispose(); }
}
