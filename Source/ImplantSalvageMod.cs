using UnityEngine;
using Verse;

namespace ImplantSalvage;

public class ImplantSalvageSettings : ModSettings
{
    /// <summary>Destroy chance for a surgeon with no medical ability whatsoever.</summary>
    public float maxDestroyChance = 0.5f;

    /// <summary>Floor that skill cannot go below, mirroring vanilla's "always a small chance of failure".</summary>
    public float minDestroyChance = 0.02f;

    public bool showIntactChance = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref maxDestroyChance, "maxDestroyChance", 0.5f);
        Scribe_Values.Look(ref minDestroyChance, "minDestroyChance", 0.02f);
        Scribe_Values.Look(ref showIntactChance, "showIntactChance", defaultValue: true);
    }
}

public class ImplantSalvageMod : Mod
{
    public static ImplantSalvageSettings Settings { get; private set; }

    public ImplantSalvageMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<ImplantSalvageSettings>();
    }

    public override string SettingsCategory() => "Implant Salvage";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.Label("Luke_ImplantSalvageSettingsMaxDestroy".Translate() + ": " +
                      Settings.maxDestroyChance.ToStringPercent());
        listing.Label("Luke_ImplantSalvageSettingsMaxDestroyDesc".Translate(), -1f, null);
        Settings.maxDestroyChance = listing.Slider(Settings.maxDestroyChance, 0f, 1f);

        listing.Gap();

        listing.Label("Luke_ImplantSalvageSettingsMinDestroy".Translate() + ": " +
                      Settings.minDestroyChance.ToStringPercent());
        listing.Label("Luke_ImplantSalvageSettingsMinDestroyDesc".Translate(), -1f, null);
        Settings.minDestroyChance = listing.Slider(Settings.minDestroyChance, 0f, 0.25f);

        listing.Gap();

        bool show = Settings.showIntactChance;
        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowChance".Translate(), ref show);
        Settings.showIntactChance = show;

        listing.End();
        base.DoSettingsWindowContents(inRect);
    }
}
