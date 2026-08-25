using UnityEngine;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// Presentation options only.
///
/// There is deliberately no global "implants worth salvaging" list here. Which implants matter is a
/// per-stockpile decision, made in the Storage tab's implant picker, because one global answer is
/// useless the moment a colony wants a bionics pile and a scrap pile at the same time.
///
/// The two destroy-chance values are not preferences either - they are simulation rules that feed a
/// Rand roll, so while a game is loaded they live in the save (see GameComponent_ImplantSalvage) and
/// what is stored here is only the starting point a *new* game copies.
/// </summary>
public class ImplantSalvageSettings : ModSettings
{
    /// <summary>Destroy chance for a surgeon with no medical ability whatsoever.</summary>
    public float maxDestroyChance = 0.5f;

    /// <summary>Floor that skill cannot go below, mirroring vanilla's "always a small chance of failure".</summary>
    public float minDestroyChance = 0.02f;

    public bool showIntactChance = true;

    /// <summary>
    /// Draw a marker over corpses that still hold an implant. On by default: shipped off, it was
    /// invisible until someone went looking in the settings menu, and a feature nobody finds is
    /// worse than one they switch off.
    /// </summary>
    public bool showCorpseMarker = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref maxDestroyChance, "maxDestroyChance", 0.5f);
        Scribe_Values.Look(ref minDestroyChance, "minDestroyChance", 0.02f);
        Scribe_Values.Look(ref showIntactChance, "showIntactChance", defaultValue: true);
        Scribe_Values.Look(ref showCorpseMarker, "showCorpseMarker", defaultValue: true);
    }
}

public class ImplantSalvageMod : Mod
{
    /// <summary>
    /// Slider edits made while a game is running, held until the window closes so they can be sent
    /// as one synced change. Null means "not edited this session" - see WriteSettings.
    /// </summary>
    private float? pendingMaxDestroyChance;
    private float? pendingMinDestroyChance;

    public static ImplantSalvageSettings Settings { get; private set; }

    public ImplantSalvageMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<ImplantSalvageSettings>();
    }

    public override string SettingsCategory() => "Implant Salvage";

    /// <summary>
    /// Called when the settings window closes. The destroy chances are simulation rules, so an
    /// in-game edit is pushed through a synced method - one message per edit, rather than one per
    /// frame while a slider is dragged.
    ///
    /// Only an actual edit is pushed. That matters in Multiplayer: a client who merely opens and
    /// closes this window must not overwrite the host's rules with their own saved defaults.
    /// </summary>
    public override void WriteSettings()
    {
        base.WriteSettings();

        if (pendingMaxDestroyChance.HasValue && GameComponent_ImplantSalvage.Current != null)
        {
            ImplantSalvageActions.SetDestroyChances(
                pendingMaxDestroyChance.Value,
                pendingMinDestroyChance ?? Settings.minDestroyChance);

            // Keep the stored copy in step, so the next new game starts from what was last chosen.
            Settings.maxDestroyChance = pendingMaxDestroyChance.Value;
            Settings.minDestroyChance = pendingMinDestroyChance ?? Settings.minDestroyChance;
        }

        pendingMaxDestroyChance = null;
        pendingMinDestroyChance = null;
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);

        // While a game is loaded these sliders show and edit that game's rules, not the stored
        // defaults - the rules live in the save so every Multiplayer client reads the same numbers.
        // From the main menu there is no game, so they edit the defaults a new game will start from.
        GameComponent_ImplantSalvage rules = GameComponent_ImplantSalvage.Current;

        float maxDestroy = pendingMaxDestroyChance ?? rules?.maxDestroyChance ?? Settings.maxDestroyChance;
        float minDestroy = pendingMinDestroyChance ?? rules?.minDestroyChance ?? Settings.minDestroyChance;

        listing.Label("Luke_ImplantSalvageSettingsMaxDestroy".Translate() + ": " +
                      maxDestroy.ToStringPercent());
        listing.Label("Luke_ImplantSalvageSettingsMaxDestroyDesc".Translate(), -1f, null);
        float newMaxDestroy = listing.Slider(maxDestroy, 0f, 1f);

        listing.Gap();

        listing.Label("Luke_ImplantSalvageSettingsMinDestroy".Translate() + ": " +
                      minDestroy.ToStringPercent());
        listing.Label("Luke_ImplantSalvageSettingsMinDestroyDesc".Translate(), -1f, null);
        float newMinDestroy = listing.Slider(minDestroy, 0f, 0.25f);

        if (rules != null)
        {
            // Held rather than applied: a live edit here would be a local mutation of a shared rule,
            // which is the desync this whole arrangement exists to prevent. WriteSettings sends it.
            if (newMaxDestroy != maxDestroy || newMinDestroy != minDestroy)
            {
                pendingMaxDestroyChance = newMaxDestroy;
                pendingMinDestroyChance = newMinDestroy;
            }
        }
        else
        {
            Settings.maxDestroyChance = newMaxDestroy;
            Settings.minDestroyChance = newMinDestroy;
        }

        listing.Gap();

        bool show = Settings.showIntactChance;
        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowChance".Translate(), ref show);
        Settings.showIntactChance = show;

        bool marker = Settings.showCorpseMarker;
        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowMarker".Translate(), ref marker,
            "Luke_ImplantSalvageSettingsShowMarkerDesc".Translate());
        Settings.showCorpseMarker = marker;

        listing.Gap();

        // Points at where the real per-stockpile choice is made, so nobody goes looking for it here.
        GUI.color = new Color(0.65f, 0.65f, 0.65f);
        listing.Label("Luke_ImplantSalvageSettingsPickerHint".Translate());
        GUI.color = Color.white;

        listing.End();
        base.DoSettingsWindowContents(inRect);
    }
}
