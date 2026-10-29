"""Dump the merged Brave New World gameplay database to TSV.

usage: python3 -I extract.py <Civ5DebugDatabase.db> <Localization-Merged.db> <outdir>

Civ5DebugDatabase.db is written by the game to "My Games/Sid Meier's Civilization 5/cache/" after a
game is started with the Brave New World expansion active; it is the fully merged (base + G&K + BNW +
DLC) ruleset, so no XML <Update>/<Delete> merging is needed here.

Output: one TSV per table. Columns whose value is the schema default for every row are dropped, text
columns that hold prose (Civilopedia, Strategy, Help, Quote, ...) and art/audio columns are dropped, and
a `Name` column with the English text of `Description`/`ShortDescription` is prepended when present.
Only numbers and identifiers are exported; no Firaxis prose, art or audio.
"""
import csv, os, re, sqlite3, sys

PROSE = re.compile(r'^(Civilopedia|Strategy|Help|Quote|Pedia|ThemingBonusHelp|DisabledHelp|AdvisorQuestion|'
                   r'Summary|Title|Splash|EmptyToolTipText|SlotsToolTipText|.*ToolTip.*|LeaderQuote|'
                   r'VictoryStatement|DawnOfManQuote|DawnOfManImage|DawnOfManAudio|Audio.*|.*Image|.*Icon.*|'
                   r'PortraitIndex|.*Atlas.*|ArtDefineTag|.*ArtDefine.*|ArtStyle.*|.*Sound.*|.*Movie.*|'
                   r'Button|EffectTypeTag|WorldSoundscapeAudioScript|MapImage|Background.*|UnitArtInfo.*|'
                   r'UnitFlag.*|.*Scene.*|ArtInfo.*|VictoryBackground|NotificationSound|SessionSplashImage|'
                   r'ProjectSplashImage|HistoricLeader)$')

# Gameplay tables to export. BNW-only tables are exported in full; shared tables are the seed for the
# ruleset generator (see SPEC.md §3).
TABLES = """
Buildings BuildingClasses Building_YieldChanges Building_YieldModifiers Building_ThemingBonuses
Building_DomainFreeExperiencePerGreatWork Building_BuildingClassHappiness Building_ClassesNeededInCity
Building_PrereqBuildingClasses Building_ResourceYieldChanges Building_SpecialistYieldChanges
Building_TechEnhancedYieldChanges Building_YieldChangesPerPop Building_YieldChangesPerReligion
Building_FreeUnits Building_LocalResourceOrs Building_LocalResourceAnds Building_ResourceQuantityRequirements
Building_UnitCombatProductionModifiers Building_DomainFreeExperiences Building_FeatureYieldChanges
Building_SeaPlotYieldChanges Building_SeaResourceYieldChanges Building_TerrainYieldChanges
Building_RiverPlotYieldChanges Building_LakePlotYieldChanges Building_GlobalYieldModifiers
Building_BuildingClassYieldChanges Building_HurryModifiers Building_DomainProductionModifiers
Building_UnitCombatFreeExperiences Building_ResourceQuantity
Units UnitClasses Unit_FreePromotions Unit_ClassUpgrades Unit_ResourceQuantityRequirements Unit_Builds
Unit_BuildingClassRequireds Unit_YieldFromKills Unit_UniqueNames
UnitPromotions UnitPromotions_UnitCombatMods UnitPromotions_UnitCombats UnitPromotions_Domains
UnitPromotions_Terrains UnitPromotions_Features UnitPromotions_UnitClasses UnitPromotions_CivilianUnitType
UnitPromotions_PostCombatRandomPromotion
Policies PolicyBranchTypes Policy_PrereqPolicies Policy_BuildingClassHappiness Policy_BuildingClassYieldChanges
Policy_BuildingClassYieldModifiers Policy_BuildingClassProductionModifiers Policy_BuildingClassCultureChanges
Policy_BuildingClassTourismModifiers Policy_CapitalYieldChanges Policy_CapitalYieldModifiers
Policy_CapitalYieldPerPopChanges Policy_CityYieldChanges Policy_CoastalCityYieldChanges Policy_FreePromotions
Policy_FreeUnitClasses Policy_GreatWorkYieldChanges Policy_HurryModifiers Policy_ImprovementCultureChanges
Policy_ImprovementYieldChanges Policy_SpecialistExtraYields Policy_TourismOnUnitCreation
Policy_UnitCombatProductionModifiers
Beliefs Belief_BuildingClassFaithPurchase Belief_BuildingClassHappiness Belief_BuildingClassTourism
Belief_BuildingClassYieldChanges Belief_CityYieldChanges Belief_EraFaithUnitPurchase Belief_FeatureYieldChanges
Belief_ImprovementYieldChanges Belief_MaxYieldModifierPerFollower Belief_ResourceYieldChanges
Belief_TerrainYieldChanges Belief_YieldChangeAnySpecialist Belief_YieldChangeNaturalWonder
Belief_YieldChangePerForeignCity Belief_YieldChangePerXForeignFollowers Belief_YieldChangeTradeRoute
Belief_YieldChangeWorldWonder Religions
Technologies Technology_PrereqTechs Technology_TradeRouteDomainExtraRange Eras GameSpeeds GameSpeed_Turns
HandicapInfos Worlds Specialists SpecialistYields Projects Project_Prereqs Project_VictoryThresholds Victories
VictoryPointAwards Processes Process_ProductionYields
Improvements Improvement_Yields Improvement_TechYieldChanges Improvement_ResourceTypes
Improvement_ResourceType_Yields Improvement_ValidTerrains Improvement_ValidFeatures Improvement_ValidImprovements
Improvement_YieldPerEra Improvement_AdjacentMountainYieldChanges Improvement_HillsYields
Improvement_TechFreshWaterYieldChanges Improvement_TechNoFreshWaterYieldChanges Builds BuildFeatures
Resources Resource_YieldChanges Resource_QuantityTypes Resource_TerrainBooleans Resource_FeatureBooleans
Resource_FeatureTerrainBooleans Features Feature_YieldChanges Terrains Terrain_Yields Terrain_HillsYieldChanges
Natural_Wonder_Placement
Civilizations Civilization_Leaders Civilization_BuildingClassOverrides Civilization_UnitClassOverrides
Civilization_FreeBuildingClasses Civilization_FreeTechs Civilization_FreeUnits Civilization_Start_Along_Ocean
Civilization_Start_Place_First_Along_Ocean Civilization_Start_Region_Priority Civilization_Start_Region_Avoid
Leaders Leader_Traits Leader_Flavors Traits Trait_ExtraYieldThresholds Trait_FreePromotionUnitCombats
Trait_ImprovementYieldChanges Trait_MaintenanceModifierUnitCombats Trait_MovesChangeUnitCombats Trait_NoTrain
Trait_ResourceQuantityModifiers Trait_SpecialistYieldChanges Trait_UnimprovedFeatureYieldChanges
Trait_YieldChanges Trait_YieldChangesIncomingTradeRoute Trait_YieldChangesPerTradePartner
Trait_YieldChangesStrategicResources MinorCivilizations MinorCivTraits
GreatWorks GreatWorkClasses GreatWorkSlots GreatWorkArtifactClasses
LeagueSpecialSessions LeagueProjects LeagueProjectRewards LeagueNames Resolutions ResolutionDecisions
HistoricRankings GoodyHuts Defines PostDefines
""".split()

def main(dbp, locp, out):
    db = sqlite3.connect(f'file:{dbp}?mode=ro', uri=True)
    loc = sqlite3.connect(f'file:{locp}?mode=ro', uri=True)
    tx = dict(loc.execute("select Tag, Text from LocalizedText where Language='en_US'"))
    def en(k):
        s = re.sub(r"\[[^\]]*\]", "", tx.get(k) or "").strip() if k else ""
        return s if len(s) <= 48 else ""  # names only, never prose
    os.makedirs(out, exist_ok=True)
    for t in TABLES:
        info = db.execute(f'pragma table_info("{t}")').fetchall()
        if not info:
            print('missing table', t, file=sys.stderr); continue
        cols = [c[1] for c in info]; dflt = {c[1]: c[4] for c in info}
        rows = db.execute(f'select * from "{t}" order by rowid').fetchall()
        keep = []
        for i, c in enumerate(cols):
            if PROSE.match(c) or c == 'ID': continue
            d = dflt[c]
            d = None if d in (None, 'NULL') else str(d).strip("'")
            vals = {None if r[i] is None else str(r[i]) for r in rows}
            if c in ('Type', 'Description', 'ShortDescription') or vals - {d, None, ''} or (d is None and vals - {None, ''}):
                keep.append(i)
        named = 'Description' in cols or 'ShortDescription' in cols
        with open(os.path.join(out, t + '.tsv'), 'w', newline='', encoding='utf-8') as f:
            w = csv.writer(f, delimiter='\t', lineterminator='\n')
            w.writerow((['Name'] if named else []) + [cols[i] for i in keep])
            for r in rows:
                k = r[cols.index('ShortDescription')] if 'ShortDescription' in cols and r[cols.index('ShortDescription')] else (r[cols.index('Description')] if 'Description' in cols else None)
                w.writerow(([en(k)] if named else []) + ['' if r[i] is None else r[i] for i in keep])
    print('wrote', len(TABLES), 'tables to', out)

if __name__ == '__main__':
    main(*sys.argv[1:4])
