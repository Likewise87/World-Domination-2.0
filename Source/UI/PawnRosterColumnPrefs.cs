using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA_WorldDomination
{
    public enum PawnRosterColumnWindow
    {
        AllPlayerPawns,
        OutpostPawns,
        Prisoners,
        RemoteEstablish,
        OutpostArmory
    }

    /// <summary>Stable column ids for roster visibility prefs (per window, per save).</summary>
    public static class PawnRosterColumnIds
    {
        public const string Type = "Type";
        public const string Star = "Star";
        public const string New = "New";
        public const string Age = "Age";
        public const string Interaction = "Interaction";
        public const string Resistance = "Resistance";
        public const string Traits = "Traits";
        public const string Xenotype = "Xenotype";
        public const string Psycasts = "Psycasts";
        public const string Ideology = "Ideology";
        public const string Destination = "Destination";
        public const string Select = "Select";
        public const string Portrait = "Portrait";
        public const string Name = "Name";
        public const string Strength = "Strength";
        public const string Relevant = "Relevant";
        public const string Construction = "Construction";
        public const string DailyFood = "DailyFood";
        public const string Hurt = "Hurt";
        public const string Health = "Health";
        public const string Shooting = "Shooting";
        public const string Melee = "Melee";
        /// <summary>Armory-only: carried non-consumable inventory section.</summary>
        public const string OtherInventory = "OtherInventory";
        /// <summary>Armory-only: carry mass cur/cap (CE weight when CE is active, else MassUtility).</summary>
        public const string Mass = "Mass";
        /// <summary>Armory-only, CE: carry bulk cur/cap.</summary>
        public const string Bulk = "Bulk";

        /// <summary>Full skill-grid id (outpost tab), distinct from dedicated combat cols.</summary>
        public static string FullSkill(SkillDef skill) => "Skill_" + (skill?.defName ?? "");

        public static string Skill(SkillDef skill) => skill?.defName ?? "";
    }

    /// <summary>Column metadata for the Columns to Show dialog.</summary>
    public readonly struct PawnRosterColumnOption
    {
        public readonly string Id;
        public readonly string LabelKey;
        public readonly bool DefaultVisible;

        public PawnRosterColumnOption(string id, string labelKey, bool defaultVisible)
        {
            Id = id;
            LabelKey = labelKey;
            DefaultVisible = defaultVisible;
        }
    }

    public static class PawnRosterColumnCatalog
    {
        private static List<PawnRosterColumnOption> allPlayerPawns;
        private static List<PawnRosterColumnOption> outpostPawns;
        private static List<PawnRosterColumnOption> prisoners;
        private static List<PawnRosterColumnOption> outpostArmory;

        public static IReadOnlyList<PawnRosterColumnOption> OptionsFor(PawnRosterColumnWindow window)
        {
            EnsureBuilt();
            switch (window)
            {
                case PawnRosterColumnWindow.OutpostPawns: return outpostPawns;
                case PawnRosterColumnWindow.Prisoners: return prisoners;
                case PawnRosterColumnWindow.OutpostArmory: return outpostArmory;
                case PawnRosterColumnWindow.RemoteEstablish:
                case PawnRosterColumnWindow.AllPlayerPawns:
                default:
                    return allPlayerPawns;
            }
        }

        public static bool DefaultVisible(PawnRosterColumnWindow window, string id)
        {
            IReadOnlyList<PawnRosterColumnOption> opts = OptionsFor(window);
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Id == id)
                    return opts[i].DefaultVisible;
            }
            return false;
        }

        private static void EnsureBuilt()
        {
            if (allPlayerPawns != null) return;

            allPlayerPawns = new List<PawnRosterColumnOption>
            {
                new PawnRosterColumnOption(PawnRosterColumnIds.Type, "TSA_WD_AllPlayerPawns_ColPawnType", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Star, "TSA_WD_AllPlayerPawns_ColStar", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.New, "TSA_WD_PawnRoster_ColNew", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Health, "TSA_WD_PawnRoster_ColHealth", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", false),
            };
            AddDlcBioOptions(allPlayerPawns);
            AddSkillOptions(allPlayerPawns, prefixFullSkill: false, defaultOn: true);

            outpostPawns = new List<PawnRosterColumnOption>
            {
                new PawnRosterColumnOption(PawnRosterColumnIds.Portrait, "TSA_WD_PawnRoster_ColPortrait", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Type, "TSA_WD_AllPlayerPawns_ColPawnType", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Name, "TSA_WD_PawnCol_PawnName", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Star, "TSA_WD_AllPlayerPawns_ColStar", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.New, "TSA_WD_PawnRoster_ColNew", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Select, "TSA_WD_PawnRoster_ColSelect", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Resistance, "TSA_WD_Prisoners_ColResistance", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Shooting, "TSA_WD_PawnRoster_ColShooting", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Melee, "TSA_WD_PawnRoster_ColMelee", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Strength, "TSA_WD_PawnRoster_ColStrength", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Relevant, "TSA_WD_PawnRoster_ColRelevant", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Construction, "TSA_WD_PawnRoster_ColConstruction", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.DailyFood, "TSA_WD_PawnRoster_ColDailyFood", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Hurt, "TSA_WD_PawnRoster_ColHurt", true),
            };
            AddDlcBioOptions(outpostPawns);
            AddSkillOptions(outpostPawns, prefixFullSkill: true, defaultOn: false, skipShootingAndMelee: true);

            prisoners = new List<PawnRosterColumnOption>
            {
                new PawnRosterColumnOption(PawnRosterColumnIds.Interaction, "TSA_WD_Prisoners_ColInteraction", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Resistance, "TSA_WD_Prisoners_ColResistance", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Destination, "TSA_WD_Prisoners_ColDestination", true),
            };
            // Skills inserted before Destination in menu order: rebuild with skills before destination.
            prisoners = new List<PawnRosterColumnOption>
            {
                new PawnRosterColumnOption(PawnRosterColumnIds.Interaction, "TSA_WD_Prisoners_ColInteraction", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Resistance, "TSA_WD_Prisoners_ColResistance", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge", false),
            };
            AddDlcBioOptions(prisoners);
            prisoners.Add(new PawnRosterColumnOption(PawnRosterColumnIds.Health, "TSA_WD_PawnRoster_ColHealth", false));
            AddSkillOptions(prisoners, prefixFullSkill: false, defaultOn: true);
            prisoners.Add(new PawnRosterColumnOption(PawnRosterColumnIds.Destination, "TSA_WD_Prisoners_ColDestination", true));

            // Same option set as All Player Pawns, but defaults match the prior Armory layout:
            // only Shooting / Melee / Health on; everything else available but off.
            outpostArmory = new List<PawnRosterColumnOption>
            {
                new PawnRosterColumnOption(PawnRosterColumnIds.Type, "TSA_WD_AllPlayerPawns_ColPawnType", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Star, "TSA_WD_AllPlayerPawns_ColStar", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.New, "TSA_WD_PawnRoster_ColNew", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Age, "TSA_WD_PawnRoster_ColAge", false),
                new PawnRosterColumnOption(PawnRosterColumnIds.Health, "TSA_WD_PawnRoster_ColHealth", true),
                new PawnRosterColumnOption(PawnRosterColumnIds.Traits, "TSA_WD_Prisoners_ColTraits", false),
            };
            AddDlcBioOptions(outpostArmory);
            outpostArmory.Add(new PawnRosterColumnOption(
                PawnRosterColumnIds.OtherInventory, "TSA_WD_Armory_SecOtherInventory", true));
            outpostArmory.Add(new PawnRosterColumnOption(
                PawnRosterColumnIds.Mass, "TSA_WD_Armory_ColMass", false));
            if (OutpostCeAmmoCompat.IsCeActive)
            {
                outpostArmory.Add(new PawnRosterColumnOption(
                    PawnRosterColumnIds.Bulk, "TSA_WD_Armory_ColBulk", false));
            }
            AddSkillOptions(outpostArmory, prefixFullSkill: false, defaultOn: false);
            for (int i = 0; i < outpostArmory.Count; i++)
            {
                PawnRosterColumnOption opt = outpostArmory[i];
                if (opt.Id == PawnRosterColumnIds.Shooting || opt.Id == PawnRosterColumnIds.Melee)
                    outpostArmory[i] = new PawnRosterColumnOption(opt.Id, opt.LabelKey, true);
            }
        }

        private static void AddDlcBioOptions(List<PawnRosterColumnOption> list)
        {
            if (ModsConfig.BiotechActive)
                list.Add(new PawnRosterColumnOption(PawnRosterColumnIds.Xenotype, "TSA_WD_PawnRoster_ColXenotype", false));
            if (ModsConfig.RoyaltyActive)
                list.Add(new PawnRosterColumnOption(PawnRosterColumnIds.Psycasts, "TSA_WD_PawnRoster_ColPsycasts", false));
            if (ModsConfig.IdeologyActive)
                list.Add(new PawnRosterColumnOption(PawnRosterColumnIds.Ideology, "TSA_WD_PawnRoster_ColIdeology", false));
        }

        private static void AddSkillOptions(List<PawnRosterColumnOption> list, bool prefixFullSkill, bool defaultOn, bool skipShootingAndMelee = false)
        {
            SkillDef[] skills = PlayerPawnRosterUtility.AllSkillColumns;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef s = skills[i];
                if (s == null) continue;
                if (skipShootingAndMelee && (s == SkillDefOf.Shooting || s == SkillDefOf.Melee))
                    continue;
                string id = prefixFullSkill ? PawnRosterColumnIds.FullSkill(s) : PawnRosterColumnIds.Skill(s);
                list.Add(new PawnRosterColumnOption(id, null, defaultOn));
            }
        }

        public static string ResolveLabel(PawnRosterColumnOption opt)
        {
            if (!opt.LabelKey.NullOrEmpty())
                return opt.LabelKey.Translate().ToString();

            // Skill options: id is defName or Skill_defName
            string id = opt.Id ?? "";
            string defName = id.StartsWith("Skill_") ? id.Substring(6) : id;
            SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(defName);
            return skill != null ? skill.LabelCap.ToString() : id;
        }
    }
}
