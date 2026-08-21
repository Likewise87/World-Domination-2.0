using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace TSA_WorldDomination
{
    /// <summary>Road-block then spike-trap lines on the world tile inspect pane.</summary>
    [HarmonyPatch(typeof(WorldInspectPane), "get_TileInspectString")]
    public static class Patch_WorldInspectPane_TileInspectString_TileHazards
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (__result.NullOrEmpty()) return;

            PlanetTile tile = Find.WorldSelector?.SelectedTile ?? PlanetTile.Invalid;
            if (!tile.Valid) return;
            int tileId = tile.tileId;

            string roadLine = TryFormatRoadBlockLine(tileId);
            string trapLine = TryFormatSpikeTrapLine(tileId);
            if (roadLine == null && trapLine == null) return;

            var sb = new StringBuilder(__result);
            if (!__result.EndsWith("\n"))
                sb.AppendLine();
            if (roadLine != null)
                sb.Append(roadLine);
            if (trapLine != null)
            {
                if (roadLine != null)
                    sb.AppendLine();
                sb.Append(trapLine);
            }
            __result = sb.ToString();
        }

        private static string TryFormatRoadBlockLine(int tileId)
        {
            float penalty = WorldComponent_RoadBlocks.GetFlatPenalty(tileId);
            if (penalty <= 0f) return null;

            float curHp = 0f;
            float maxHp = WorldDominationSettings.DefRoadBlockNormalMaxHealth;
            string kindLabel = "TSA_WD_RoadBlockNormal".Translate();
            if (WorldComponent_RoadBlocks.Get()?.TryGet(tileId, out RoadBlockRecord rec) == true && rec != null)
            {
                curHp = rec.health;
                kindLabel = RoadBlockKindUtil.LabelKey(rec.kind).Translate();
                maxHp = WorldDominationMod.settings != null
                    ? WorldDominationMod.settings.GetRoadBlockMaxHealth(rec.kind)
                    : WorldDominationSettings.DefRoadBlockNormalMaxHealth;
            }

            return "TSA_WD_RoadBlock_TileInspect".Translate(
                kindLabel,
                penalty.ToString("0.#"),
                curHp.ToString("F0"),
                maxHp.ToString("F0")).ToString();
        }

        private static string TryFormatSpikeTrapLine(int tileId)
        {
            WorldComponent_SpikeTraps traps = WorldComponent_SpikeTraps.Get();
            if (traps == null || !traps.TryGet(tileId, out SpikeTrapRecord rec) || rec == null)
                return null;

            var s = WorldDominationMod.settings;
            float damage = s != null ? s.GetSpikeTrapDamage(rec.kind) : WorldDominationSettings.DefSpikeTrapSpikeDamage;
            float maxHp = s != null ? s.GetSpikeTrapMaxHealth(rec.kind) : WorldDominationSettings.DefSpikeTrapSpikeMaxHealth;
            string kindLabel = SpikeTrapKindUtil.LabelKey(rec.kind).Translate();

            string line = "TSA_WD_SpikeTrap_TileInspect".Translate(
                kindLabel,
                damage.ToString("F0"),
                rec.health.ToString("F0"),
                maxHp.ToString("F0")).ToString();
            string builderLabel = rec.builtByFaction?.Name;
            if (!builderLabel.NullOrEmpty())
                line += "\n" + "TSA_WD_Inspect_AT_TurretBuilder".Translate(builderLabel);
            return line;
        }
    }
}
