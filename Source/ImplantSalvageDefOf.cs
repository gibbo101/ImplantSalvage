using RimWorld;
using Verse;

namespace ImplantSalvage;

[DefOf]
public static class ImplantSalvageDefOf
{
    public static JobDef Luke_ExtractImplant;

    static ImplantSalvageDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ImplantSalvageDefOf));
    }
}
