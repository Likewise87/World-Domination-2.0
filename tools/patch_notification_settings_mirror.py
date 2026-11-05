# One-off mirror for Settings_Window_Notifications.cs keyed strings (EN -> ES, zh-CN, zh-TW).
# Updates one XML element at a time; no bulk regex across folders.
from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

MOD = Path(__file__).resolve().parents[1]
LANG = MOD / "Common" / "Languages"

SCOPED_KEYS: list[str] = [
    "TSA_WD_Difficulty_NotifyOutpostInc",
    "TSA_WD_Difficulty_NotifyOutpostIncTooltip",
    "TSA_WD_Notify_AntiAirHit",
    "TSA_WD_Notify_AntiAirHitTooltip",
    "TSA_WD_Notify_AntiLeaderCoalition",
    "TSA_WD_Notify_AntiLeaderCoalitionTooltip",
    "TSA_WD_Notify_BribeCeasefireExpired",
    "TSA_WD_Notify_BribeCeasefireExpiredTooltip",
    "TSA_WD_Notify_BribeLostInTransit",
    "TSA_WD_Notify_BribeLostInTransitTooltip",
    "TSA_WD_Notify_BribeRaidAborted",
    "TSA_WD_Notify_BribeRaidAbortedTooltip",
    "TSA_WD_Notify_BribeRaidCompleted",
    "TSA_WD_Notify_BribeRaidCompletedTooltip",
    "TSA_WD_Notify_BribeSettlementAborted",
    "TSA_WD_Notify_BribeSettlementAbortedTooltip",
    "TSA_WD_Notify_BribeSettlementCompleted",
    "TSA_WD_Notify_BribeSettlementCompletedTooltip",
    "TSA_WD_Notify_BridgeDeconstructBlocked",
    "TSA_WD_Notify_BridgeDeconstructBlockedTooltip",
    "TSA_WD_Notify_ConstructionInsufficientMaterials",
    "TSA_WD_Notify_ConstructionInsufficientMaterialsTooltip",
    "TSA_WD_Notify_ConstructionInsufficientStrength",
    "TSA_WD_Notify_ConstructionInsufficientStrengthTooltip",
    "TSA_WD_Notify_CriticalFood",
    "TSA_WD_Notify_CriticalFoodTooltip",
    "TSA_WD_Notify_DesperationRaid",
    "TSA_WD_Notify_DesperationRaidTooltip",
    "TSA_WD_Notify_DropPodDeliveryInAaRange",
    "TSA_WD_Notify_DropPodDeliveryInAaRangeTooltip",
    "TSA_WD_Notify_ExpansionistZeal",
    "TSA_WD_Notify_ExpansionistZealTooltip",
    "TSA_WD_Notify_HeaderAlerts",
    "TSA_WD_Notify_HeaderAtTurrets",
    "TSA_WD_Notify_HeaderDiplomacyBuffs",
    "TSA_WD_Notify_HeaderMortarAntiAir",
    "TSA_WD_Notify_HeaderNearby",
    "TSA_WD_Notify_HeaderOutpostsRaids",
    "TSA_WD_Notify_HeaderWorldThreats",
    "TSA_WD_Notify_HeaderWorldThreatsTip",
    "TSA_WD_Notify_IncomingRaidColony",
    "TSA_WD_Notify_IncomingRaidColonyTooltip",
    "TSA_WD_Notify_IncomingRaidOutpost",
    "TSA_WD_Notify_IncomingRaidOutpostTooltip",
    "TSA_WD_Notify_LateGameActive",
    "TSA_WD_Notify_LateGameActiveTooltip",
    "TSA_WD_Notify_LeaderHandicap",
    "TSA_WD_Notify_LeaderHandicapTooltip",
    "TSA_WD_Notify_MidGameActive",
    "TSA_WD_Notify_MidGameActiveTooltip",
    "TSA_WD_Notify_MortarHit",
    "TSA_WD_Notify_MortarHitTooltip",
    "TSA_WD_Notify_MortarMiss",
    "TSA_WD_Notify_MortarMissTooltip",
    "TSA_WD_Notify_NegotiateAborted",
    "TSA_WD_Notify_NegotiateAbortedTooltip",
    "TSA_WD_Notify_NegotiateCompleted",
    "TSA_WD_Notify_NegotiateCompletedTooltip",
    "TSA_WD_Notify_NegotiateStarted",
    "TSA_WD_Notify_NegotiateStartedTooltip",
    "TSA_WD_Notify_NewSettlement",
    "TSA_WD_Notify_NewSettlementTooltip",
    "TSA_WD_Notify_NotificationRadius",
    "TSA_WD_Notify_NotificationRadiusTooltip",
    "TSA_WD_Notify_NpcAtTurretDamagedPlayer",
    "TSA_WD_Notify_NpcAtTurretDamagedPlayerTooltip",
    "TSA_WD_Notify_NpcAtTurretKilledPlayer",
    "TSA_WD_Notify_NpcAtTurretKilledPlayerTooltip",
    "TSA_WD_Notify_NpcConquestSettlement",
    "TSA_WD_Notify_NpcConquestSettlementTooltip",
    "TSA_WD_Notify_NpcMortarHitNpc",
    "TSA_WD_Notify_NpcMortarHitNpcTooltip",
    "TSA_WD_Notify_NpcMortarHitPlayer",
    "TSA_WD_Notify_NpcMortarHitPlayerTooltip",
    "TSA_WD_Notify_OutpostDeliveryToColonyArrived",
    "TSA_WD_Notify_OutpostDeliveryToColonyArrivedTooltip",
    "TSA_WD_Notify_OutpostDestroyed",
    "TSA_WD_Notify_OutpostDestroyedTooltip",
    "TSA_WD_Notify_OutpostNoProduction",
    "TSA_WD_Notify_OutpostNoProductionTooltip",
    "TSA_WD_Notify_OutpostPollutionDamage",
    "TSA_WD_Notify_OutpostPollutionDamageTooltip",
    "TSA_WD_Notify_OutpostUnusedExperts",
    "TSA_WD_Notify_OutpostUnusedExpertsTooltip",
    "TSA_WD_Notify_OutpostUpkeep",
    "TSA_WD_Notify_OutpostUpkeepTooltip",
    "TSA_WD_Notify_PlayerAntiAirVsHostileMortarShell",
    "TSA_WD_Notify_PlayerAntiAirVsHostileMortarShellTooltip",
    "TSA_WD_Notify_PlayerAtTurretDamagedTarget",
    "TSA_WD_Notify_PlayerAtTurretDamagedTargetTooltip",
    "TSA_WD_Notify_PlayerAtTurretDestroyed",
    "TSA_WD_Notify_PlayerAtTurretDestroyedTooltip",
    "TSA_WD_Notify_PlayerAtTurretKilledTarget",
    "TSA_WD_Notify_PlayerAtTurretKilledTargetTooltip",
    "TSA_WD_Notify_PlayerCaravanClash",
    "TSA_WD_Notify_PlayerCaravanClashTooltip",
    "TSA_WD_Notify_PlayerMortarShellShotDown",
    "TSA_WD_Notify_PlayerMortarShellShotDownTooltip",
    "TSA_WD_Notify_PrisonerRecruitedUnderway",
    "TSA_WD_Notify_PrisonerRecruitedUnderwayTooltip",
    "TSA_WD_Notify_RaidArrivalColony",
    "TSA_WD_Notify_RaidArrivalColonyTooltip",
    "TSA_WD_Notify_RaidArrivalOutpost",
    "TSA_WD_Notify_RaidArrivalOutpostTooltip",
    "TSA_WD_Notify_RaidDivertedFromPlayer",
    "TSA_WD_Notify_RaidDivertedFromPlayerTooltip",
    "TSA_WD_Notify_RaidPartyDestroyed",
    "TSA_WD_Notify_RaidPartyDestroyedTooltip",
    "TSA_WD_Notify_RandomDiplomacy",
    "TSA_WD_Notify_RandomDiplomacyTooltip",
    "TSA_WD_Notify_RapidResponseCaravanClash",
    "TSA_WD_Notify_RapidResponseCaravanClashTooltip",
    "TSA_WD_Notify_RoadCancelledByTrench",
    "TSA_WD_Notify_RoadCancelledByTrenchTooltip",
    "TSA_WD_Notify_SettlementBuyAborted",
    "TSA_WD_Notify_SettlementBuyAbortedTooltip",
    "TSA_WD_Notify_SettlementBuyCompleted",
    "TSA_WD_Notify_SettlementBuyCompletedTooltip",
    "TSA_WD_Notify_SettlementRaided",
    "TSA_WD_Notify_SettlementRaidedTooltip",
    "TSA_WD_Notify_SettlementRazed",
    "TSA_WD_Notify_SettlementRazedTooltip",
    "TSA_WD_Notify_StrongFactionWar",
    "TSA_WD_Notify_StrongFactionWarTooltip",
    "TSA_WD_Notify_T4AntiAirHitPlayer",
    "TSA_WD_Notify_T4AntiAirHitPlayerTooltip",
    "TSA_WD_Notify_ThreatLevel",
    "TSA_WD_Notify_ThreatLevelTooltip",
    "TSA_WD_Notify_TradeAllyDiplomacy",
    "TSA_WD_Notify_TradeAllyDiplomacyTooltip",
    "TSA_WD_Notify_TravelerPollutionDamage",
    "TSA_WD_Notify_TravelerPollutionDamageTooltip",
    "TSA_WD_Notify_TrenchCancelledByRoad",
    "TSA_WD_Notify_TrenchCancelledByRoadTooltip",
    "TSA_WD_Notify_Turtle",
    "TSA_WD_Notify_TurtleTooltip",
    "TSA_WD_Notify_UnderdogBuff",
    "TSA_WD_Notify_UnderdogBuffTooltip",
    "TSA_WD_Notify_WarehouseGoodsArrived",
    "TSA_WD_Notify_WarehouseGoodsArrivedTooltip",
]

EN_FILES = [
    LANG / "English/Keyed/Translations_UI_Threat_Notifications_English.xml",
    LANG / "English/Keyed/Translations_Settings_English.xml",
    LANG / "English/Keyed/Translations_Diplomacy_SpyOps_English.xml",
]

TARGETS = {
    "Spanish": [
        LANG / "Spanish/Keyed/Translations_UI_Threat_Notifications_Spanish.xml",
        LANG / "Spanish/Keyed/Translations_Settings_Spanish.xml",
        LANG / "Spanish/Keyed/Translations_Diplomacy_SpyOps_Spanish.xml",
    ],
    "ChineseSimplified": [
        LANG / "ChineseSimplified/Keyed/Translations_UI_Threat_Notifications_ChineseSimplified.xml",
        LANG / "ChineseSimplified/Keyed/Translations_Settings_ChineseSimplified.xml",
        LANG / "ChineseSimplified/Keyed/Translations_Diplomacy_SpyOps_ChineseSimplified.xml",
    ],
    "ChineseTraditional": [
        LANG / "ChineseTraditional/Keyed/Translations_UI_Threat_Notifications_ChineseTraditional.xml",
        LANG / "ChineseTraditional/Keyed/Translations_Settings_ChineseTraditional.xml",
        LANG / "ChineseTraditional/Keyed/Translations_Diplomacy_SpyOps_ChineseTraditional.xml",
    ],
}

# Full string overrides (label + tooltip) keyed by translation key name.
from notification_mirror_strings import STRINGS  # noqa: E402


def load_en() -> dict[str, str]:
    out: dict[str, str] = {}
    for path in EN_FILES:
        tree = ET.parse(path)
        for el in tree.getroot():
            if el.tag in SCOPED_KEYS:
                out[el.tag] = el.text or ""
    return out


def find_home_file(key: str, paths: list[Path]) -> Path | None:
    for path in paths:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8")
        if f"<{key}>" in text:
            return path
    return None


def set_key_in_file(path: Path, key: str, value: str) -> bool:
    tree = ET.parse(path)
    root = tree.getroot()
    changed = False
    for el in root:
        if el.tag == key:
            if (el.text or "") != value:
                el.text = value
                changed = True
            break
    else:
        # insert before closing LanguageData, after last Notify key if any
        el = ET.Element(key)
        el.text = value
        insert_at = len(root)
        for i, child in enumerate(root):
            if child.tag.startswith("TSA_WD_Notify_") or child.tag.startswith("TSA_WD_Difficulty_Notify"):
                insert_at = i + 1
        root.insert(insert_at, el)
        changed = True
    if changed:
        tree.write(path, encoding="utf-8", xml_declaration=True)
    return changed


def main() -> None:
    en = load_en()
    missing_en = [k for k in SCOPED_KEYS if k not in en]
    if missing_en:
        raise SystemExit(f"Missing EN keys: {missing_en[:5]} ... ({len(missing_en)} total)")

    stats: dict[str, int] = {}
    missing_loc: dict[str, list[str]] = {lang: [] for lang in TARGETS}

    for lang, paths in TARGETS.items():
        updated = 0
        for key in SCOPED_KEYS:
            if key not in STRINGS.get(lang, {}):
                missing_loc[lang].append(key)
                continue
            value = STRINGS[lang][key]
            home = find_home_file(key, paths)
            if home is None:
                # default file by EN source
                if key.startswith("TSA_WD_Notify_Settlement") or "Negotiate" in key or "Bribe" in key:
                    home = paths[2]
                elif key in (
                    "TSA_WD_Notify_HeaderDiplomacyBuffs",
                    "TSA_WD_Notify_LeaderHandicap",
                    "TSA_WD_Notify_LeaderHandicapTooltip",
                    "TSA_WD_Notify_UnderdogBuff",
                    "TSA_WD_Notify_UnderdogBuffTooltip",
                    "TSA_WD_Notify_ExpansionistZeal",
                    "TSA_WD_Notify_ExpansionistZealTooltip",
                    "TSA_WD_Notify_AntiLeaderCoalition",
                    "TSA_WD_Notify_AntiLeaderCoalitionTooltip",
                    "TSA_WD_Notify_RandomDiplomacy",
                    "TSA_WD_Notify_RandomDiplomacyTooltip",
                    "TSA_WD_Notify_TradeAllyDiplomacy",
                    "TSA_WD_Notify_TradeAllyDiplomacyTooltip",
                    "TSA_WD_Notify_StrongFactionWar",
                    "TSA_WD_Notify_StrongFactionWarTooltip",
                ):
                    home = paths[1]
                else:
                    home = paths[0]
            if set_key_in_file(home, key, value):
                updated += 1
        stats[lang] = updated

    print("Updated per language:", stats)
    for lang, keys in missing_loc.items():
        if keys:
            print(f"Missing strings in data for {lang}:", len(keys), keys[:8])


if __name__ == "__main__":
    main()
