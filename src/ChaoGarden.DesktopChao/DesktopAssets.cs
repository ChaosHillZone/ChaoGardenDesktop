namespace ChaoGarden.DesktopChao;

internal static class DesktopAssets
{
    public static Icon Icon()
    {
        using Stream input = typeof(DesktopAssets).Assembly.GetManifestResourceStream("DesktopChao.Icon")!;
        using Icon icon = new(input); return (Icon)icon.Clone();
    }
}
