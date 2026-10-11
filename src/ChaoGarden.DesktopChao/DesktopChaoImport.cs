using ChaoGarden.Core.Enums;
using ChaoGarden.Core.Models.Entities;
using ChaoGarden.Core.SaveFormat;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;
using ChaoGarden.Tools.Migration;

namespace ChaoGarden.DesktopChao;

public static class DesktopChaoImport
{
    public static ChaoEntity ReadFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        // Same raw SA2 block and ECW tail formats supported by the engine serializer.
        if (stream.Length is not (2048 or 2112))
            throw new InvalidDataException("Select a single SA2 .chao file (2048 bytes, or 2112 bytes with an ECW extension).");
        byte[] bytes = new byte[SA2MemoryConstants.ChaoBlockSize];
        stream.ReadExactly(bytes);
        return Parse(bytes);
    }
    public static ChaoEntity Parse(byte[] bytes)
    {
        SA2ChaoBlock block = new(bytes, 0);
        ChaoType type = (ChaoType)block.ReadByte(SA2ChaoOffsets.ChaoType);
        if (!Enum.IsDefined(type) || type < ChaoType.Child)
            throw new InvalidDataException("The file does not contain a supported hatched SA2 Chao.");
        ChaoEntity chao = SA2ToChaoWorldMapper.Map(SA2ChaoParser.Parse(block));
        PetSaveStore.Validate(new DesktopChaoState { Chao = chao });
        return chao;
    }
    public static DesktopChaoState Copy(ChaoEntity chao, IEnumerable<int> existingSeeds)
    {
        PetSaveStore.Validate(new DesktopChaoState { Chao = chao });
        DesktopChaoState state = PetRosterStore.NewChao(existingSeeds);
        Guid id = state.Chao.Id;
        state.Chao = BoothJson.Clone(chao);
        state.Chao.Id = id;
        // A one-time import has no SA2 slot binding; subsequent game changes cannot replace it.
        return state;
    }
}
