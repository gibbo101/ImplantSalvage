using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ImplantSalvage;

/// <summary>
/// Which storage the filter tree currently being drawn belongs to.
///
/// Listing_TreeThingFilter knows its ThingFilter but not who owns it, and there is no reverse
/// lookup from a filter back to its stockpile. So the Storage tab records the owner on the way in
/// and clears it on the way out - the same trick Multiplayer itself uses (ThingFilterMarkers) to
/// work out which filter a click belongs to.
/// </summary>
public static class StorageFilterContext
{
    public static IStoreSettingsParent Current;
}

/// <summary>
/// Draws the implant picker as part of the storage filter tree, nested under Corpses.
///
/// Why this is hand-drawn rather than defs: vanilla's tree only nests real ThingCategoryDefs that
/// contain ThingDefs (Listing_TreeThingFilter.Visible checks DescendantThingDefs), so a category
/// holding only filters is never shown - special filters always render flat. And special filters
/// are OR'd exclusions, which cannot express "accept if at least one implant is allowed": a filter
/// worker sees only the corpse, never which other boxes are ticked, so it could not tell that a
/// bionic arm had rescued a body whose peg leg was denied. Drawing our own rows against our own
/// per-stockpile filter is the only way to get both the nesting and the rule.
///
/// The rows deliberately copy Listing_Tree's geometry exactly - same indent step, same 18px
/// open/close widget, same checkbox column at ColumnWidth - 26 - so they read as part of the tree
/// rather than as something bolted underneath it.
/// </summary>
public static class ImplantTreeInStorage
{
    private static readonly HashSet<ThingCategoryDef> OpenCategories = new HashSet<ThingCategoryDef>();
    private static bool rootOpen;

    private static List<ThingCategoryDef> groupOrder;
    private static Dictionary<ThingCategoryDef, List<ThingDef>> groups;

    public static void Draw(Listing_TreeThingFilter listing, int indentLevel)
    {
        IStoreSettingsParent parent = StorageFilterContext.Current;
        if (parent == null)
        {
            return;
        }

        string key = ImplantSalvageStorage.KeyFor(parent);
        if (key == null)
        {
            return;
        }

        EnsureGroups();
        if (groupOrder.Count == 0)
        {
            return;
        }

        // The "allow corpses with implants" filter is this picker's parent switch. With it off the
        // stockpile refuses every implanted body anyway, so choosing WHICH implants is meaningless -
        // show the row greyed rather than hiding it, so it stays discoverable.
        StorageSettings settings = parent.GetStoreSettings();
        bool enabled = settings?.filter?.Allows(ImplantSalvageDefOf.Luke_AllowCorpsesWithImplants) ?? true;

        // Null means never customised, which is the same as everything allowed.
        List<string> filter = ImplantSalvageStorage.AllowedFor(parent);

        if (!enabled)
        {
            DrawRow(listing, indentLevel, "Luke_ImplantTreeRoot".Translate(),
                "Luke_ImplantTreeDisabledDesc".Translate(), openable: false, isOpen: false,
                StateOf(filter, AllProducts()), out _, enabled: false);
            return;
        }

        MultiCheckboxState rootState = StateOf(filter, AllProducts());
        bool rootToggled = DrawRow(listing, indentLevel, "Luke_ImplantTreeRoot".Translate(),
            "Luke_ImplantTreeRootDesc".Translate(), openable: true, rootOpen,
            rootState, out MultiCheckboxState newRootState);

        if (rootToggled)
        {
            rootOpen = !rootOpen;
        }

        // Only on an actual click. Applying whenever the state merely *is* On would re-allow
        // everything every frame, which silently undid each individual tick a frame after it landed.
        if (newRootState != rootState)
        {
            Apply(key, filter, AllProducts(), newRootState == MultiCheckboxState.On);
        }

        if (!rootOpen)
        {
            return;
        }

        for (int i = 0; i < groupOrder.Count; i++)
        {
            ThingCategoryDef category = groupOrder[i];
            List<ThingDef> members = groups[category];
            bool open = OpenCategories.Contains(category);

            MultiCheckboxState state = StateOf(filter, members);
            bool toggled = DrawRow(listing, indentLevel + 1, category.LabelCap, category.description,
                openable: true, open, state, out MultiCheckboxState newState);

            if (toggled)
            {
                if (open)
                {
                    OpenCategories.Remove(category);
                }
                else
                {
                    OpenCategories.Add(category);
                }
            }

            if (newState != state)
            {
                Apply(key, filter, members, newState == MultiCheckboxState.On);
            }

            if (!OpenCategories.Contains(category))
            {
                continue;
            }

            for (int m = 0; m < members.Count; m++)
            {
                ThingDef product = members[m];
                bool allowed = ImplantSalvageStorage.Allows(filter, product);

                DrawRow(listing, indentLevel + 2, product.LabelCap, product.description,
                    openable: false, isOpen: false,
                    allowed ? MultiCheckboxState.On : MultiCheckboxState.Off,
                    out MultiCheckboxState newProductState);

                if ((newProductState == MultiCheckboxState.On) != allowed)
                {
                    Apply(key, filter, new List<ThingDef> { product }, newProductState == MultiCheckboxState.On);
                }
            }
        }
    }

    /// <summary>
    /// One row, laid out exactly as Listing_Tree lays out its own: indent step of nestIndentWidth,
    /// an 18px open/close widget at the indent, the label starting 18px further in, and the
    /// checkbox in the column at ColumnWidth - 26.
    /// </summary>
    private static bool DrawRow(Listing_TreeThingFilter listing, int indentLevel, string label,
        string tooltip, bool openable, bool isOpen, MultiCheckboxState state,
        out MultiCheckboxState newState, bool enabled = true)
    {
        float lineHeight = listing.lineHeight;
        Rect row = listing.GetRect(lineHeight);
        float indentX = indentLevel * listing.nestIndentWidth;
        bool toggled = false;

        if (openable)
        {
            Rect widgetRect = new Rect(indentX, row.y + lineHeight / 2f - 9f, 18f, 18f);
            if (Widgets.ButtonImage(widgetRect, isOpen ? TexButton.Collapse : TexButton.Reveal))
            {
                (isOpen ? SoundDefOf.TabClose : SoundDefOf.TabOpen).PlayOneShotOnCamera();
                toggled = true;
            }
        }

        float labelWidth = listing.ColumnWidth - 26f;
        Rect labelRect = new Rect(indentX + 18f, row.y, labelWidth - indentX - 18f, lineHeight);
        Widgets.DrawHighlightIfMouseover(labelRect);
        if (!tooltip.NullOrEmpty())
        {
            TooltipHandler.TipRegion(labelRect, tooltip);
        }

        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = enabled ? Color.white : Color.grey;
        Widgets.Label(labelRect, label.Truncate(labelRect.width));
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;

        MultiCheckboxState drawnState = Widgets.CheckboxMulti(
            new Rect(labelWidth, row.y, lineHeight, lineHeight), state, paintable: enabled);

        // Clicks are ignored while disabled - the row is showing state, not offering a choice.
        newState = enabled ? drawnState : state;

        // GetRect advanced curY by the row height; Listing_Lines.EndLine also adds verticalSpacing,
        // so match it or our rows creep out of step with the rest of the tree.
        listing.Gap(listing.verticalSpacing);

        return toggled;
    }

    private static MultiCheckboxState StateOf(List<string> filter, List<ThingDef> products)
    {
        bool anyOn = false;
        bool anyOff = false;

        for (int i = 0; i < products.Count; i++)
        {
            if (ImplantSalvageStorage.Allows(filter, products[i]))
            {
                anyOn = true;
            }
            else
            {
                anyOff = true;
            }

            if (anyOn && anyOff)
            {
                return MultiCheckboxState.Partial;
            }
        }

        return anyOn ? MultiCheckboxState.On : MultiCheckboxState.Off;
    }

    /// <summary>
    /// Push a change through the synced setter. Sends the whole allowed set rather than a delta so
    /// the message is self-contained and cannot half-apply on another client.
    /// </summary>
    private static void Apply(string key, List<string> filter, List<ThingDef> changed, bool allow)
    {
        HashSet<ThingDef> changing = new HashSet<ThingDef>(changed);
        List<string> allowed = new List<string>();

        foreach (ThingDef product in AllProducts())
        {
            bool value = changing.Contains(product) ? allow : ImplantSalvageStorage.Allows(filter, product);
            if (value)
            {
                allowed.Add(product.defName);
            }
        }

        ImplantSalvageStorage.SetStorageImplantFilter(key, allowed);
    }

    private static List<ThingDef> AllProducts() => ImplantSalvageUtility.AllImplantProducts();

    /// <summary>
    /// Implants grouped by the category their own def declares - which is how the tiers come free:
    /// vanilla files bionics under BodyPartsBionic, archotech under BodyPartsArchotech and so on,
    /// and bionics mods file theirs the same way. Anything uncategorised falls into its own group
    /// rather than being dropped.
    /// </summary>
    private static void EnsureGroups()
    {
        if (groups != null)
        {
            return;
        }

        groups = new Dictionary<ThingCategoryDef, List<ThingDef>>();
        groupOrder = new List<ThingCategoryDef>();

        foreach (ThingDef product in AllProducts())
        {
            ThingCategoryDef category = product.thingCategories?.Count > 0
                ? product.thingCategories[0]
                : ThingCategoryDefOf.BodyParts;

            if (!groups.TryGetValue(category, out List<ThingDef> members))
            {
                members = new List<ThingDef>();
                groups[category] = members;
                groupOrder.Add(category);
            }

            members.Add(product);
        }

        // Most valuable tier first, so archotech and bionics sit at the top where the decision is.
        groupOrder.Sort(delegate(ThingCategoryDef a, ThingCategoryDef b)
        {
            int byValue = MaxValue(groups[b]).CompareTo(MaxValue(groups[a]));
            return byValue != 0 ? byValue : string.CompareOrdinal(a.defName, b.defName);
        });
    }

    private static float MaxValue(List<ThingDef> products)
    {
        float max = 0f;
        for (int i = 0; i < products.Count; i++)
        {
            if (products[i].BaseMarketValue > max)
            {
                max = products[i].BaseMarketValue;
            }
        }

        return max;
    }
}
