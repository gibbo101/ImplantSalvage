using RimWorld;
using Verse;

namespace ImplantSalvage;

[DefOf]
public static class ImplantSalvageDefOf
{
    public static JobDef Luke_ExtractImplant;

    public static DesignationDef Luke_ExtractImplantMark;

    public static SpecialThingFilterDef Luke_AllowCorpsesWithImplants;
    public static SpecialThingFilterDef Luke_AllowCorpsesNoImplants;

    static ImplantSalvageDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(ImplantSalvageDefOf));
    }
}
