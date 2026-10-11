using ChaoGarden.Content.Loading;
using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.ModelBuilder.Chao;

namespace ChaoGarden.DesktopChao;

public sealed class AppearanceMenu
{
    private readonly string resources;
    private readonly Dictionary<(BodyPart, bool), SA2BAnimalType[]> available = new();
    public AppearanceMenu(string resources)
    {
        this.resources = resources;
    }

    public ToolStripMenuItem Parts(ChaoEntity chao, Action changed)
    {
        ToolStripMenuItem root = new("Animal parts");
        foreach (BodyPart slot in Enum.GetValues<BodyPart>())
        {
            ToolStripMenuItem part = new(slot.ToString());
            bool child = chao.ChaoType == ChaoType.Child;
            if (!available.TryGetValue((slot, child), out SA2BAnimalType[]? animals))
            {
                animals = Enum.GetValues<SA2BAnimalType>().Where(animal =>
                    animal == SA2BAnimalType.None || AnimalPartResolver.GetSlotMeshKeys(animal, slot, child)
                    .Any(key => HasMesh(Path.Combine(resources, key)))).ToArray();
                available[(slot, child)] = animals;
            }
            foreach (SA2BAnimalType animal in animals)
            {
                ToolStripMenuItem choice = new(animal.ToString()) { Checked = Get(chao.Appearance, slot) == animal };
                choice.Click += (_, _) => { Set(chao.Appearance, slot, animal); changed(); };
                part.DropDownItems.Add(choice);
            }
            root.DropDownItems.Add(part);
        }
        return root;
    }

    public static ToolStripMenuItem Accessories(ChaoEntity chao, Action changed)
    {
        ToolStripMenuItem root = new("Accessories");
        ToolStripMenuItem hats = new("Hat");
        foreach (Hat hat in Enum.GetValues<Hat>())
        {
            ToolStripMenuItem choice = new(hat.ToString()) { Checked = chao.Appearance.Hat == hat };
            choice.Click += (_, _) => { chao.Appearance.Hat = hat; changed(); };
            hats.DropDownItems.Add(choice);
        }
        ToolStripMenuItem medals = new("Medals");
        foreach (Medals medal in Enum.GetValues<Medals>())
        {
            ToolStripMenuItem choice = new(medal.ToString())
            {
                Checked = medal == Medals.None ? chao.Appearance.Medal == Medals.None : chao.Appearance.Medal.HasFlag(medal)
            };
            choice.Click += (_, _) =>
            {
                chao.Appearance.Medal = medal == Medals.None ? Medals.None : chao.Appearance.Medal ^ medal;
                changed();
            };
            medals.DropDownItems.Add(choice);
        }
        root.DropDownItems.Add(hats);
        root.DropDownItems.Add(medals);
        return root;
    }

    private static bool HasMesh(string path)
    {
        if (!File.Exists(path)) return false;
        try { return GlbParser.Parse(path).Primitives.Count > 0; }
        catch (InvalidDataException) { return false; }
    }

    private static SA2BAnimalType Get(ChaoAppearance a, BodyPart slot) => slot switch
    {
        BodyPart.Arms => a.ArmsPart, BodyPart.Ears => a.EarsPart, BodyPart.Forehead => a.ForeheadPart,
        BodyPart.Face => a.FacePart, BodyPart.Horns => a.HornsPart, BodyPart.Feet => a.LegsPart,
        BodyPart.Tail => a.TailPart, _ => a.WingsPart
    };
    private static void Set(ChaoAppearance a, BodyPart slot, SA2BAnimalType animal)
    {
        switch (slot)
        {
            case BodyPart.Arms: a.ArmsPart = animal; break;
            case BodyPart.Ears: a.EarsPart = animal; break;
            case BodyPart.Forehead: a.ForeheadPart = animal; break;
            case BodyPart.Face: a.FacePart = animal; break;
            case BodyPart.Horns: a.HornsPart = animal; break;
            case BodyPart.Feet: a.LegsPart = animal; break;
            case BodyPart.Tail: a.TailPart = animal; break;
            case BodyPart.Wings: a.WingsPart = animal; break;
        }
    }
}
