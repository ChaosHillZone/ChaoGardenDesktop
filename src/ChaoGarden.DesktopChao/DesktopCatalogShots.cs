using ChaoGarden.Core.Enums;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public static class DesktopCatalogShots
{
    public static ShotDefinition Create(DesktopAnimationDefinition animation)
    {
        ShotDefinition shot = new() { Name = animation.Clip, Animation = animation.Clip,
            Frames = 7201, FramesPerSecond = 60, Playback = Playback.Once };
        string[] instruments = animation.Clip switch
        {
            "alm_bell" => ["Bell"], "alm_kasutanetto" => ["Castanet", "Castanet"],
            "alm_sinbal" => ["Cymbals1", "Cymbals2"], "alm_dram" or "alm_draming" => ["Drum_Stick1", "Drum_Stick2", "Drum"],
            "alm_fue" => ["Flute"], "alm_maracas" => ["Maracas1", "Maracas2"],
            "alm_rappa" => ["Trumpet"], "alm_tambarin" => ["Tambourine"],
            "alm_kureyon" => ["Crayon", "ChaoDrawing"], _ => []
        };
        for (int i = 0; i < instruments.Length; i++)
        {
            string name = instruments[i];
            PropDefinition prop = new() { Model = $"glb/items/toys/{name}.glb",
                Attachment = i == 1 ? Attachment.LeftHand : Attachment.RightHand, CenterModel = true,
                TextureList = "data/texlist_al_toy.satex", TextureDirectory = "textures-hq/chao/toys" };
            if (name == "Drum") { prop.Attachment = Attachment.World; prop.RotationDegrees = [0, 0, 90]; prop.Offset = [0, .6f, 1.7f]; }
            if (name == "ChaoDrawing")
            {
                prop.Attachment = Attachment.World; prop.CenterModel = false; prop.Scale = .35f; prop.Offset = [0, .035f, 2];
                prop.TextureList = "data/texlist_al_object.satex"; prop.TextureDirectory = "textures-hq/items/al_object";
            }
            shot.Props.Add(prop);
        }
        (string? toy, Attachment attachment) = animation.Clip switch
        {
            "alm_book" => ("Book", Attachment.RightHand), "alm_garagara" => ("Rattle", Attachment.RightHand),
            "alm_houki" => ("Broom", Attachment.RightHand), "alm_johro" => ("WateringCan", Attachment.RightHand),
            "alm_minicar" => ("ToyCar", Attachment.World), "alm_piano" => ("Piano", Attachment.World),
            "alm_organ" => ("Organ", Attachment.World), "alm_horse" => ("alo_horse", Attachment.World),
            "alm_syabon" => ("BubbleBlower", Attachment.RightHand), _ => (null, Attachment.World)
        };
        if (toy != null) shot.Props.Add(new PropDefinition { Model = $"glb/items/toys/{toy}.glb", Attachment = attachment,
            CenterModel = attachment != Attachment.World, Offset = attachment == Attachment.World ? [0, 0, 1.5f] : [0, 0, 0] });
        if (animation.Clip == "alm_syabon") shot.Props.Add(new PropDefinition { Model = "glb/items/toys/BubbleBottle.glb",
            Attachment = Attachment.LeftHand, CenterModel = true });
        foreach (PropDefinition prop in shot.Props)
            if (prop.Model.EndsWith("/ToyCar.glb") || prop.Model.EndsWith("/Piano.glb")
                || prop.Model.EndsWith("/Organ.glb") || prop.Model.EndsWith("/BubbleBlower.glb") || prop.Model.EndsWith("/BubbleBottle.glb"))
            {
                prop.TextureList = "data/texlist_al_toy.satex";
                prop.TextureDirectory = "textures-hq/chao/toys";
            }
        if (animation.Clip.Contains("sleep", StringComparison.Ordinal) || animation.Clip.StartsWith("alm_neru", StringComparison.Ordinal))
            shot.Eyes = EyeType.ClosedDown;
        return shot;
    }
}
