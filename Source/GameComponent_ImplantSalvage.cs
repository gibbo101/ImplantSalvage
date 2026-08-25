using System.Collections.Generic;
using Verse;

namespace ImplantSalvage;

/// <summary>
/// The extraction rules that the *simulation* reads, stored in the save rather than in ModSettings.
///
/// This exists for one reason: ModSettings are per-client. DestroyChanceFor feeds a Rand.Chance
/// roll inside the JobDriver, so if two Multiplayer players had different sliders they would roll
/// different outcomes for the same extraction and desync. Keeping the numbers in the save makes
/// every client read identical values by construction - a joining client loads the host's game and
/// therefore the host's rules.
///
/// ModSettings still exist and still matter; they are now the *defaults a new game starts with*,
/// plus the purely visual options (the corpse marker, the intact-chance label) which never touch
/// the simulation and are free to differ per player.
///
/// Auto-registers: Game.FillComponents() instantiates every non-abstract GameComponent subclass,
/// the same way maps pick up MapComponents.
/// </summary>
public class GameComponent_ImplantSalvage : GameComponent
{
    public float maxDestroyChance = 0.5f;
    public float minDestroyChance = 0.02f;

    /// <summary>
    /// False only on a game that has never run this mod. Seeding once, rather than every load,
    /// is what stops a client's local ModSettings from silently overwriting the host's rules when
    /// they load into a shared save.
    /// </summary>
    private bool seeded;

    /// <summary>
    /// Per-stockpile implant selections, keyed by their owner's stable save ID (see
    /// ImplantSalvageStorage.KeyFor). Absent key = never customised = no implant restriction, so
    /// an untouched stockpile behaves exactly as it did before this mod.
    ///
    /// Lives in the save rather than in ModSettings for the same reason the chances do: it decides
    /// what may be hauled where, which is simulation.
    /// </summary>
    public Dictionary<string, List<string>> storageImplantAllowed = new Dictionary<string, List<string>>();

    /// <summary>
    /// Implants queued for extraction, per corpse. Lives in the save so every Multiplayer client
    /// agrees on what is queued - see ImplantSalvagePending for why it is not inside the
    /// Designation itself.
    /// </summary>
    public List<PendingExtraction> pendingExtractions = new List<PendingExtraction>();

    public GameComponent_ImplantSalvage(Game game)
    {
    }

    public static GameComponent_ImplantSalvage Current =>
        Verse.Current.Game?.GetComponent<GameComponent_ImplantSalvage>();

    public override void FinalizeInit()
    {
        base.FinalizeInit();

        if (seeded)
        {
            return;
        }

        // A brand new game (or the first load after installing the mod) adopts this player's
        // preferred numbers. From then on they belong to the save.
        ImplantSalvageSettings settings = ImplantSalvageMod.Settings;
        if (settings != null)
        {
            maxDestroyChance = settings.maxDestroyChance;
            minDestroyChance = settings.minDestroyChance;
        }

        seeded = true;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref maxDestroyChance, "maxDestroyChance", 0.5f);
        Scribe_Values.Look(ref minDestroyChance, "minDestroyChance", 0.02f);
        Scribe_Values.Look(ref seeded, "seeded", defaultValue: false);
        Scribe_Collections.Look(ref storageImplantAllowed, "storageImplantAllowed",
            LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref pendingExtractions, "pendingExtractions", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            storageImplantAllowed ??= new Dictionary<string, List<string>>();
            pendingExtractions ??= new List<PendingExtraction>();
            pendingExtractions.RemoveAll(p => p == null || p.implantLoadIDs.NullOrEmpty());
        }
    }
}
