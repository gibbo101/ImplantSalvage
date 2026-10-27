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
    public const float DefaultMaxDestroyChance = 0.5f;
    public const float DefaultMinDestroyChance = 0.02f;

    /// <summary>Destroy chance for a surgeon with no medical ability whatsoever.</summary>
    public float maxDestroyChance = DefaultMaxDestroyChance;

    /// <summary>Floor that skill cannot go below, mirroring vanilla's "always a small chance of failure".</summary>
    public float minDestroyChance = DefaultMinDestroyChance;

    public bool showIntactChance = true;

    /// <summary>
    /// Draw a marker over corpses that still hold an implant. On by default, so the feature is
    /// found in play rather than only by someone browsing the settings menu.
    /// </summary>
    public bool showCorpseMarker = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref maxDestroyChance, "maxDestroyChance", DefaultMaxDestroyChance);
        Scribe_Values.Look(ref minDestroyChance, "minDestroyChance", DefaultMinDestroyChance);
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
            float maxDestroy = pendingMaxDestroyChance.Value;
            float minDestroy = pendingMinDestroyChance ?? Settings.minDestroyChance;

            ImplantSalvageActions.SetDestroyChances(maxDestroy, minDestroy);

            // Keep the stored copy in step, so the next new game starts from what was last chosen.
            Settings.maxDestroyChance = maxDestroy;
            Settings.minDestroyChance = minDestroy;
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

        float newMaxDestroy = ChanceSlider(listing, "Luke_ImplantSalvageSettingsMaxDestroy",
            "Luke_ImplantSalvageSettingsMaxDestroyDesc", maxDestroy, 1f);

        listing.Gap();

        float newMinDestroy = ChanceSlider(listing, "Luke_ImplantSalvageSettingsMinDestroy",
            "Luke_ImplantSalvageSettingsMinDestroyDesc", minDestroy, 0.25f);

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

        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowChance".Translate(), ref Settings.showIntactChance);
        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowMarker".Translate(), ref Settings.showCorpseMarker,
            "Luke_ImplantSalvageSettingsShowMarkerDesc".Translate());

        listing.Gap();

        // Points at where the real per-stockpile choice is made, so nobody goes looking for it here.
        GUI.color = new Color(0.65f, 0.65f, 0.65f);
        listing.Label("Luke_ImplantSalvageSettingsPickerHint".Translate());
        GUI.color = Color.white;

        listing.End();
        base.DoSettingsWindowContents(inRect);
    }

    /// <summary>A labelled percentage slider from 0 to <paramref name="max"/>; returns the new value.</summary>
    private static float ChanceSlider(Listing_Standard listing, string labelKey, string descKey, float value, float max)
    {
        listing.Label(labelKey.Translate() + ": " + value.ToStringPercent());
        listing.Label(descKey.Translate(), -1f, null);
        return listing.Slider(value, 0f, max);
    }
}
