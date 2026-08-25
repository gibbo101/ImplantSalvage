using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Draw a marker over corpses holding a salvageable implant. Off by default: it is an extra
    /// layer of icons on an already busy map, so the player opts into it.
    /// </summary>
    public bool showCorpseMarker;

    /// <summary>
    /// Implants the player has decided are not worth salvaging, by ThingDef defName - e.g. switch
    /// off prosthetic legs while leaving bionic legs on.
    ///
    /// Stored as *exclusions* rather than as an allow-list on purpose. A newly installed mod's
    /// implants are therefore salvage-worthy by default rather than silently switched off, which
    /// is what someone adding a bionics mod expects.
    ///
    /// Stored as raw strings rather than as a ThingFilter or a List of ThingDef because
    /// ModSettings are loaded by GetSettings in the Mod constructor, which runs before the
    /// DefDatabase is populated - any Def-mode scribe here would resolve to null. It also means
    /// uninstalling the mod an implant came from degrades quietly: the defName stops matching
    /// anything and everything else still works.
    /// </summary>
    public List<string> disallowedImplants = new List<string>();

    [Unsaved(false)]
    private HashSet<string> disallowedLookup;

    private HashSet<string> DisallowedLookup
    {
        get
        {
            if (disallowedLookup == null)
            {
                disallowedLookup = new HashSet<string>(disallowedImplants ?? new List<string>());
            }

            return disallowedLookup;
        }
    }

    /// <summary>
    /// Whether this implant is one the player still wants salvaged. Drives the corpse marker and
    /// the storage/bill filters; deliberately does NOT hide right-click options (see the float
    /// menu provider).
    /// </summary>
    public bool ImplantIsWanted(ThingDef product)
    {
        return product != null && !DisallowedLookup.Contains(product.defName);
    }

    public void SetImplantWanted(ThingDef product, bool wanted)
    {
        if (product == null)
        {
            return;
        }

        disallowedImplants ??= new List<string>();

        if (wanted)
        {
            disallowedImplants.Remove(product.defName);
            DisallowedLookup.Remove(product.defName);
        }
        else if (DisallowedLookup.Add(product.defName))
        {
            disallowedImplants.Add(product.defName);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref maxDestroyChance, "maxDestroyChance", 0.5f);
        Scribe_Values.Look(ref minDestroyChance, "minDestroyChance", 0.02f);
        Scribe_Values.Look(ref showIntactChance, "showIntactChance", defaultValue: true);
        Scribe_Values.Look(ref showCorpseMarker, "showCorpseMarker", defaultValue: false);
        Scribe_Collections.Look(ref disallowedImplants, "disallowedImplants", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            disallowedImplants ??= new List<string>();
            disallowedLookup = null;
        }
    }
}

public class ImplantSalvageMod : Mod
{
    private const float RowHeight = 28f;

    private static readonly Color DescColor = new Color(0.65f, 0.65f, 0.65f);

    private Vector2 implantScrollPosition;
    private string implantSearch = "";

    public static ImplantSalvageSettings Settings { get; private set; }

    public ImplantSalvageMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<ImplantSalvageSettings>();
    }

    public override string SettingsCategory() => "Implant Salvage";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        // Fixed block on top, scrolling implant list underneath. The list can run to hundreds of
        // rows once bionics mods are installed, so it cannot share the Listing_Standard.
        Rect topRect = new Rect(inRect.x, inRect.y, inRect.width, 268f);

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(topRect);

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

        bool marker = Settings.showCorpseMarker;
        listing.CheckboxLabeled("Luke_ImplantSalvageSettingsShowMarker".Translate(), ref marker,
            "Luke_ImplantSalvageSettingsShowMarkerDesc".Translate());
        Settings.showCorpseMarker = marker;

        listing.End();

        Rect listRect = new Rect(inRect.x, topRect.yMax + 8f, inRect.width,
            inRect.height - topRect.height - 8f);
        DrawImplantList(listRect);

        base.DoSettingsWindowContents(inRect);
    }

    /// <summary>
    /// Per-implant opt-out. Every implant any loaded mod defines shows up here automatically: the
    /// list comes from HediffDef.spawnThingOnRemoved, the same one-field test that decides what is
    /// extractable at all, so modded bionics need no support code and no per-mod patch.
    /// </summary>
    private void DrawImplantList(Rect rect)
    {
        List<ThingDef> products = ImplantSalvageUtility.AllImplantProducts();

        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
            "Luke_ImplantSalvageSettingsImplantList".Translate());

        Rect descRect = new Rect(rect.x, rect.y + 24f, rect.width, 34f);
        GUI.color = DescColor;
        Text.Font = GameFont.Tiny;
        Widgets.Label(descRect, "Luke_ImplantSalvageSettingsImplantListDesc".Translate());
        Text.Font = GameFont.Small;
        GUI.color = Color.white;

        float controlsY = descRect.yMax + 2f;
        Rect searchRect = new Rect(rect.x, controlsY, Mathf.Max(120f, rect.width - 224f), 28f);
        implantSearch = Widgets.TextField(searchRect, implantSearch);

        if (Widgets.ButtonText(new Rect(searchRect.xMax + 8f, controlsY, 100f, 28f),
                "Luke_ImplantSalvageSettingsAll".Translate()))
        {
            foreach (ThingDef product in products)
            {
                Settings.SetImplantWanted(product, wanted: true);
            }
        }

        if (Widgets.ButtonText(new Rect(searchRect.xMax + 116f, controlsY, 100f, 28f),
                "Luke_ImplantSalvageSettingsNone".Translate()))
        {
            foreach (ThingDef product in products)
            {
                Settings.SetImplantWanted(product, wanted: false);
            }
        }

        List<ThingDef> shown = new List<ThingDef>();
        foreach (ThingDef product in products)
        {
            if (implantSearch.NullOrEmpty() ||
                product.label.IndexOf(implantSearch, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                shown.Add(product);
            }
        }

        Rect outRect = new Rect(rect.x, controlsY + 34f, rect.width, rect.yMax - controlsY - 34f);
        Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, shown.Count * RowHeight);

        Widgets.BeginScrollView(outRect, ref implantScrollPosition, viewRect);

        float curY = 0f;
        foreach (ThingDef product in shown)
        {
            Rect row = new Rect(0f, curY, viewRect.width, RowHeight);
            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
            }

            Widgets.ThingIcon(new Rect(row.x, row.y, RowHeight, RowHeight), product);

            bool wanted = Settings.ImplantIsWanted(product);
            bool newWanted = wanted;
            Rect checkRect = new Rect(row.x + RowHeight + 6f, row.y, row.width - RowHeight - 6f, row.height);
            Widgets.CheckboxLabeled(checkRect, product.LabelCap, ref newWanted);
            if (newWanted != wanted)
            {
                Settings.SetImplantWanted(product, newWanted);
            }

            curY += RowHeight;
        }

        Widgets.EndScrollView();
    }
}
