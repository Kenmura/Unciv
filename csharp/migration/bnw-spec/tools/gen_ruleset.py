#!/usr/bin/env python3
"""Generate the Civ V - Brave New World Unciv ruleset (bnw-spec/SPEC.md section 3.1, ticket B002).

Run with `python3 -I gen_ruleset.py` (stdlib only). Input: `../data/*.tsv`, the Gods & Kings JSON of this repository
and the hand-maintained `overrides.json` next to this script. Output: every JSON file of
`android/assets/jsons/Civ V - Brave New World/` and the report `../data/derived/gen_ruleset_report.txt`.

    python3 -I gen_ruleset.py            write the ruleset and the report
    python3 -I gen_ruleset.py --check    exit 1 when the files on disk differ from what would be generated

Pipeline (idempotent, a pure function of its inputs):
  1. load the G&K JSON (lenient: comments and trailing commas are allowed, as in Unciv),
  2. apply the explicit rename table (section 3.3, never fuzzy matching) to every reference,
  3. rebuild techs, buildings and units from the TSV tables (BNW numbers replace G&K numbers, new rows become new
     objects, removed objects are dropped and reported, objects follow the BNW row order),
  4. apply `overrides.json` (hand-written content that cannot be generated: nations, uniques, new objects),
  5. fill the ModOptions gates and ModConstants from the BNW define values documented in `ModConstants.cs`.
"""
import argparse
import copy
import csv
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SPEC = HERE.parent
DATA = SPEC / "data"
REPO = HERE.parents[3]
GK_DIR = REPO / "android" / "assets" / "jsons" / "Civ V - Gods & Kings"
OUT_DIR = REPO / "android" / "assets" / "jsons" / "Civ V - Brave New World"
REPORT = DATA / "derived" / "gen_ruleset_report.txt"
MOD_CONSTANTS_CS = REPO / "csharp" / "Unciv.Core" / "Models" / "ModConstants.cs"
OVERRIDES = HERE / "overrides.json"

# Section 3.3: BNW names that differ from the Unciv G&K names. The map goes BNW -> Unciv, or Unciv -> BNW (RENAMES).
KEEP_UNCIV_NAME = {  # BNW TSV name -> name kept in the Unciv ruleset (the "The" prefix)
    "Pyramids": "The Pyramids", "Oracle": "The Oracle", "Great Library": "The Great Library",
    "Great Lighthouse": "The Great Lighthouse", "Louvre": "The Louvre",
}
RENAMES = {  # Unciv G&K name -> BNW name, applied to every exact-match reference and every [Name] unique parameter
    "Railroads": "Railroad",                 # tech (the improvement of that name is singular and unaffected)
    "Pharmaceuticals": "Penicillin",         # tech
    "Persian Immortal": "Immortal",          # unit
    "Work Boats": "Work Boat",               # unit
    "Theatre": "Zoo",                        # building (BNW renames the class)
    "National Treasury": "East India Company",  # building (the class is kept, SPEC 3.3)
    "M'Banza-Kongo": "M'banza-Kongo",        # city-state
    "Divine inspiration": "Divine Inspiration", "God of craftsmen": "God of Craftsmen",
    "Swords into Ploughshares": "Swords into Plowshares",
}
# Removed on purpose (SPEC 3.3): Utopia is not in BNW, the UN is a league session. Everything else that is Unciv-only is
# kept unless listed here or matched by the per-file rules below.
REMOVED = {
    "Buildings.json": {"Utopia Project", "United Nations"},
    "Nations.json": {"Dublin", "Edinburgh", "Helsinki", "Rio de Janeiro", "Venice"},  # Venice becomes a major civ (overrides)
    "VictoryTypes.json": {"Diplomatic"},  # rewritten by B063 (needs the World Congress vote); Cultural is rewritten by overrides.json (B032)
}
GK_BUILDINGS_KEPT_OUTSIDE_TSV = {"Apollo Program", "Manhattan Project"}  # Civ V projects that Unciv models as buildings


# ----------------------------------------------------------------------------------------------------------------------
# I/O helpers
# ----------------------------------------------------------------------------------------------------------------------
def lenient_loads(text):
    """json.loads that tolerates // and /* */ comments and trailing commas (what Unciv's parser accepts)."""
    out = []
    i, n, in_str = 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1])
                i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            out.append(c)
        elif text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
            continue
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[\]}])", r"\1", "".join(out)))


READ_COLUMNS = {}  # table -> columns the generator looked at (B033: the unread-column sweep of the report)


class Row(dict):
    """A TSV row that remembers which columns were read, so the report can list the non-empty ones nothing looked at."""

    def __init__(self, table, data):
        super().__init__(data)
        self.table = table

    def __getitem__(self, key):
        READ_COLUMNS.setdefault(self.table, set()).add(key)
        return super().__getitem__(key)

    def get(self, key, default=None):
        READ_COLUMNS.setdefault(self.table, set()).add(key)
        return super().get(key, default)


def tsv(name):
    with open(DATA / (name + ".tsv"), encoding="utf-8", newline="") as f:
        return [Row(name, r) for r in csv.DictReader(f, delimiter="\t")]


def dump_json(value, indent=0):
    """Deterministic pretty JSON: tab indentation, lists of scalars and short dicts on one line."""
    pad = "\t" * indent
    if isinstance(value, dict):
        if not value:
            return "{}"
        if all(not isinstance(v, (dict, list)) for v in value.values()) and len(value) <= 3 and indent > 1:
            return "{" + ", ".join(json.dumps(k, ensure_ascii=False) + ": " + dump_json(v) for k, v in value.items()) + "}"
        items = [pad + "\t" + json.dumps(k, ensure_ascii=False) + ": " + dump_json(v, indent + 1) for k, v in value.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(value, list):
        if not value:
            return "[]"
        if all(not isinstance(v, (dict, list)) for v in value) and len(json.dumps(value, ensure_ascii=False)) < 100:
            return "[" + ", ".join(json.dumps(v, ensure_ascii=False) for v in value) + "]"
        items = [pad + "\t" + dump_json(v, indent + 1) for v in value]
        return "[\n" + ",\n".join(items) + "\n" + pad + "]"
    return json.dumps(value, ensure_ascii=False)


def num(s, default=0):
    s = (s or "").strip()
    if s == "":
        return default
    f = float(s)
    return int(f) if f == int(f) else f


class Report:
    def __init__(self):
        self.lines = []

    def add(self, section, text):
        self.lines.append((section, text))

    def render(self):
        out = ["# Generated by gen_ruleset.py (B002). Reviewer-facing log: removed, added and changed objects.", ""]
        for section in sorted({s for s, _ in self.lines}):
            out.append("== " + section)
            out.extend("  " + t for s, t in self.lines if s == section)
            out.append("")
        return "\n".join(out)


# ----------------------------------------------------------------------------------------------------------------------
# Renames
# ----------------------------------------------------------------------------------------------------------------------
def apply_renames(node, renames):
    if isinstance(node, dict):
        return {k: apply_renames(v, renames) for k, v in node.items()}
    if isinstance(node, list):
        return [apply_renames(v, renames) for v in node]
    if isinstance(node, str):
        if node in renames:
            return renames[node]
        for old, new in renames.items():
            token = "[" + old + "]"
            if token in node:
                node = node.replace(token, "[" + new + "]")
        return node
    return node


# ----------------------------------------------------------------------------------------------------------------------
# Techs
# ----------------------------------------------------------------------------------------------------------------------
# B033: every non-empty Technologies.tsv column, and what happens to it. "emitted" columns are written below; "G&K" columns are the
# Civ V rules Unciv's G&K Techs.json already states with the same unique (BnwGeneratedRulesetTests proves each on the real ruleset);
# "skipped" columns have no Unciv or engine counterpart. A new non-empty column fails the generator until it is listed here.
TECH_COLUMNS = {
    "Name": "key", "Type": "key", "Cost": "emitted (cost)", "Era": "unused: Unciv derives the era from the column",
    "GridX": "emitted (column)", "GridY": "emitted (row)", "Description": "skipped: Civilopedia text",
    "InfluenceSpreadModifier": "emitted (The Internet, SPEC 4.3.4)", "TriggersArchaeologicalSites": "emitted (Archaeology, B010)",
    "AllowsWorldCongress": "emitted (Printing Press, B015)", "ExtraVotesPerDiplomat": "emitted (Globalization, B015)",
    "InternationalTradeRoutesChange": "emitted: one trade route slot per tech (B005, SPEC 3.2)",
    "EmbarkedMoveChange": "G&K: '[+1] Movement <for [Embarked] units>' (Astronomy, Steam Power)",
    "AllowsEmbarking": "G&K: 'Enables embarkation for land units' (Optics)",
    "EmbarkedAllWaterPassage": "G&K: 'Enables [Embarked] units to enter ocean tiles' (Astronomy)",
    "Repeat": "G&K: 'Can be continually researched' (Future Tech)",
    "MapVisible": "G&K: 'Reveals the entire map' (Satellites)",
    "AllowEmbassyTradingAllowed": "G&K: 'Enables establishment of embassies' (Writing)",
    "OpenBordersTradingAllowed": "G&K: 'Enables Open Borders agreements' (Civil Service)",
    "DefensivePactTradingAllowed": "G&K: 'Enables Defensive Pacts' (Chivalry)",
    "ResearchAgreementTradingAllowed": "G&K: 'Enables Research agreements' (Education)",
    "BridgeBuilding": "G&K: 'Roads connect tiles across rivers' (Engineering)",
    "Trade": "skipped: Civ V lets every tech be traded in a deal; Unciv has no per-tech switch",
    "GoodyTech": "skipped: Civ V's goody-hut tech pool; Unciv grants any unresearched tech",
}


def build_techs(gk_techs, report):
    cols = {c["columnNumber"]: {k: v for k, v in c.items() if k != "techs"} for c in gk_techs}
    old = {t["name"]: t for c in gk_techs for t in c["techs"]}
    by_type = {r["Type"]: r["Name"] for r in tsv("Technologies")}
    prereqs = {}
    for r in tsv("Technology_PrereqTechs"):
        prereqs.setdefault(r["TechType"], []).append(by_type[r["PrereqTech"]])
    trade_range = {}
    for dr in tsv("Technology_TradeRouteDomainExtraRange"):
        trade_range.setdefault(dr["TechType"], []).append(dr)
    grouped = {}
    for r in tsv("Technologies"):
        unlisted = [c for c, v in r.items() if v not in ("", "0") and c not in TECH_COLUMNS]
        if unlisted:
            raise SystemExit(f"Technologies.tsv column(s) {unlisted} are non-empty for {r['Name']} but not classified in TECH_COLUMNS")
        name = r["Name"]
        col = int(r["GridX"])
        if col not in cols:
            raise SystemExit(f"tech {name}: no G&K column {col}")
        t = dict(old.get(name, {}))
        is_new = name not in old
        t["name"] = name
        t["row"] = int(r["GridY"]) + 1
        # B015: the World Congress hooks of SPEC 3.2 (AllowsWorldCongress, ExtraVotesPerDiplomat)
        if num(r.get("AllowsWorldCongress")):
            t["uniques"] = list(t.get("uniques", [])) + ["Allows founding the World Congress"]
        if num(r.get("ExtraVotesPerDiplomat")):
            t["uniques"] = list(t.get("uniques", [])) + [f"[+{num(r['ExtraVotesPerDiplomat'])}] votes per Diplomat in the World Congress"]
        # B033: trade route slots (InternationalTradeRoutesChange) and the flat trade range of Technology_TradeRouteDomainExtraRange
        if num(r["InternationalTradeRoutesChange"]):
            t["uniques"] = list(t.get("uniques", [])) + [f"[+{num(r['InternationalTradeRoutesChange'])}] trade route slots"]
        for dr in trade_range.get(r["Type"], []):
            t["uniques"] = list(t.get("uniques", [])) + [f"[+{num(dr['Range'])}] {TRADE_DOMAIN[dr['DomainType']]} trade route range"]
        if prereqs.get(r["Type"]):
            t["prerequisites"] = prereqs[r["Type"]]
        else:
            t.pop("prerequisites", None)
        cost = int(r["Cost"])
        if cost != cols[col]["techCost"]:
            t["cost"] = cost
            report.add("techs", f"{name}: cost {cost} differs from column {col} ({cols[col]['techCost']}), per-tech cost set")
        else:
            t.pop("cost", None)
        spread = int(r["InfluenceSpreadModifier"] or 0)  # The Internet: +100% influence from tourism (SPEC 4.3.4)
        if spread:
            unique = f"[{spread:+d}]% influence spread from Tourism"
            t["uniques"] = [u for u in t.get("uniques", []) if u != unique] + [unique]
        archaeology = "Triggers the creation of archaeological sites"  # Archaeology: spawns the antiquity sites (SPEC 4.4.2, B010)
        if int(r["TriggersArchaeologicalSites"] or 0):
            t["uniques"] = [u for u in t.get("uniques", []) if u != archaeology] + [archaeology]
        if is_new:
            report.add("techs", f"{name}: new (column {col}, row {t['row']})")
        grouped.setdefault(col, []).append(t)
    for name in old:
        if name not in {r["Name"] for r in tsv("Technologies")}:
            report.add("techs", f"{name}: removed")
    return [dict(cols[c], techs=grouped[c]) for c in sorted(grouped)]


# ----------------------------------------------------------------------------------------------------------------------
# Shared lookups
# ----------------------------------------------------------------------------------------------------------------------
TRADE_DOMAIN = {"DOMAIN_LAND": "land", "DOMAIN_SEA": "sea"}
YIELDS = {"YIELD_FOOD": "food", "YIELD_PRODUCTION": "production", "YIELD_GOLD": "gold", "YIELD_SCIENCE": "science",
          "YIELD_CULTURE": "culture", "YIELD_FAITH": "faith"}
NATION_FOR_CIV = {  # BNW civilization type -> Unciv nation name (G&K names kept)
    "CIVILIZATION_SHOSHONE": "The Shoshone", "CIVILIZATION_ZULU": "The Zulus", "CIVILIZATION_OTTOMAN": "The Ottomans",
    "CIVILIZATION_HUNS": "The Huns", "CIVILIZATION_NETHERLANDS": "The Netherlands", "CIVILIZATION_MAYA": "The Maya",
    "CIVILIZATION_IROQUOIS": "Iroquois", "CIVILIZATION_AZTEC": "Aztecs", "CIVILIZATION_INCA": "Inca",
    "CIVILIZATION_CELTS": "Celts",
}


class Lookups:
    def __init__(self):
        self.tech_name = {r["Type"]: r["Name"] for r in tsv("Technologies")}
        self.civ_name = {}
        for r in tsv("Civilizations"):
            self.civ_name[r["Type"]] = NATION_FOR_CIV.get(r["Type"], r["Name"])
        self.building_class_default = {r["Type"]: r["DefaultBuilding"] for r in tsv("BuildingClasses")}
        self.building_class = {r["Type"]: r for r in tsv("BuildingClasses")}
        self.unit_class_default = {r["Type"]: r["DefaultUnit"] for r in tsv("UnitClasses")}
        self.building_by_type = {r["Type"]: r for r in tsv("Buildings")}
        self.unit_by_type = {r["Type"]: r for r in tsv("Units")}

    def building_unciv_name(self, type_):
        name = self.building_by_type[type_]["Name"]
        return KEEP_UNCIV_NAME.get(name, name)

    def unit_name(self, type_):
        return self.unit_by_type[type_]["Name"]


def table_by(name, key, value=None):
    out = {}
    for r in tsv(name):
        out.setdefault(r[key], []).append(r if value is None else r[value])
    return out


def era_costs(techs_json):
    """tech name -> (buildingCost, wonderCost) of its column."""
    out = {}
    for c in techs_json:
        for t in c["techs"]:
            out[t["name"]] = (c["buildingCost"], c["wonderCost"])
    return out


# ----------------------------------------------------------------------------------------------------------------------
# Great Works (B027, SPEC 4.2.1)
# ----------------------------------------------------------------------------------------------------------------------
SLOT_TYPES = {"GREAT_WORK_SLOT_ART_ARTIFACT": "Art/Artifact", "GREAT_WORK_SLOT_LITERATURE": "Writing", "GREAT_WORK_SLOT_MUSIC": "Music"}
WORK_CLASSES = {"GREAT_WORK_ART": "Art", "GREAT_WORK_ARTIFACT": "Artifact", "GREAT_WORK_LITERATURE": "Writing", "GREAT_WORK_MUSIC": "Music"}
THEMING_FLAGS = {"SameEra": "sameEra", "UniqueEras": "uniqueEras", "MustBeArt": "mustBeArt", "MustBeArtifact": "mustBeArtifact",
                 "MustBeEqualArtArtifact": "mustBeEqualArtArtifact", "RequiresOwner": "requiresOwner",
                 "RequiresAnyButOwner": "requiresAnyButOwner", "RequiresSamePlayer": "requiresSamePlayer",
                 "RequiresUniquePlayers": "requiresUniquePlayers"}


def title_words(key):
    return " ".join(w.capitalize() for w in key.split("_") if w)


def theming_row(r):
    row = {"bonus": num(r["Bonus"])}
    for column, field in THEMING_FLAGS.items():
        if num(r[column]):
            row[field] = True
    return row


def build_great_works(report):
    """GreatWorks.json: one entry per row of GreatWorks.tsv (table order). Blank and duplicate English names fall back to the type."""
    rows = tsv("GreatWorks")
    seen, names = set(), {}
    people = {}  # work type -> great person (Unit_UniqueNames.GreatWorkType; the key suffix of the person's text key)
    for r in tsv("Unit_UniqueNames"):
        if r["GreatWorkType"]:
            people[r["GreatWorkType"]] = title_words(r["UniqueName"][len("TXT_KEY_GREAT_PERSON_"):])
    out = []
    for r in rows:
        name = r["Name"] if r["Name"] and r["Name"] not in seen else title_words(r["Type"][len("GREAT_WORK_"):])
        if name in seen:
            raise SystemExit("duplicate Great Work name " + name)
        seen.add(name)
        names[r["Type"]] = name
        w = {"name": name, "greatWorkClass": WORK_CLASSES[r["GreatWorkClassType"]]}
        if r["EraType"]:
            w["era"] = era_name(r["EraType"])
        if r["ArtifactClassType"]:
            w["artifactClass"] = title_words(r["ArtifactClassType"][len("ARTIFACT_"):])
        if num(r["ArchaeologyOnly"]):
            w["archaeologyOnly"] = True
        if r["Type"] in people:
            w["greatPerson"] = people[r["Type"]]
        out.append(w)
    report.add("greatWorks", f"{len(out)} works ({sum(1 for w in out if 'greatPerson' in w)} named after a Great Person)")
    return out, names


NAME_GROUP_OF_CLASS = {"Art": "Artist", "Writing": "Writer", "Music": "Musician"}


def name_groups_from_works(files, report):
    """B011: the Artist, Writer and Musician name groups are the people the works are named after (Unit_UniqueNames), in table order, so every
    Great Artist, Writer or Musician unit can create the work of its name (GreatWork.greatPerson)."""
    groups = files["UnitNameGroups.json"]
    index = {g["name"]: g for g in groups}
    for cls, group in NAME_GROUP_OF_CLASS.items():
        names = [w["greatPerson"] for w in files["GreatWorks.json"] if w["greatWorkClass"] == cls and not w.get("archaeologyOnly") and "greatPerson" in w]
        if group not in index:
            index[group] = {"name": group}
            groups.append(index[group])
        index[group]["unitNames"] = names
        report.add("unitNames", f"{group}: {len(names)} names from the works named after a Great Person")


# ----------------------------------------------------------------------------------------------------------------------
# Buildings
# ----------------------------------------------------------------------------------------------------------------------
FREE_XP_DOMAIN = {"DOMAIN_LAND": "Land", "DOMAIN_SEA": "Water", "DOMAIN_AIR": "Air"}
HOOK_YIELD_NAMES = {"YIELD_FOOD": "Food", "YIELD_PRODUCTION": "Production", "YIELD_GOLD": "Gold", "YIELD_SCIENCE": "Science",
                    "YIELD_CULTURE": "Culture", "YIELD_FAITH": "Faith"}


def building_hook_uniques(r, lk, xp_rows, religion_rows):
    """B033: the trade route, tourism, Great Work and league columns of Buildings.tsv as the uniques B003 declared (SPEC 3.2).
    The trade gold bonuses are Times100 in the table and stay hundredths of Gold in the unique; the two recipient columns are whole Gold."""
    out = []

    def add(column, template):
        n = num(r[column])
        if n:
            out.append(template.format(n=n))

    add("GreatWorksTourismModifier", "[+{n}]% Tourism from Great Works [in this city]")
    add("LandmarksTourismPercent", "[{n}]% of Culture from Wonders, Landmarks and Natural Wonders added as Tourism")
    if num(r["TechEnhancedTourism"]):
        out.append(f"[+{num(r['TechEnhancedTourism'])}] Tourism <after discovering [{lk.tech_name[r['EnhancedYieldTech']]}]>")
    if num(r["AllowsFoodTradeRoutes"]):
        out.append("Allows domestic [Food] trade routes from this city")
    if num(r["AllowsProductionTradeRoutes"]):
        out.append("Allows domestic [Production] trade routes from this city")
    add("TradeRouteLandDistanceModifier", "[+{n}]% land trade route range [in this city]")
    add("TradeRouteSeaDistanceModifier", "[+{n}]% sea trade route range [in this city]")
    add("TradeRouteLandGoldBonus", "[+{n}] Gold (x1/100) from land trade routes [in this city]")
    add("TradeRouteSeaGoldBonus", "[+{n}] Gold (x1/100) from sea trade routes [in this city]")
    add("TradeRouteRecipientBonus", "[+{n}] Gold to the owner of an international trade route from this city")
    add("TradeRouteTargetBonus", "[+{n}] Gold to the owner of an international trade route to this city")
    add("NumTradeRouteBonus", "[+{n}] trade route slots")
    add("CityStateTradeRouteProductionModifier", "[+{n}]% Production for each trade route to a city-state")
    add("GreatScientistBeakerModifier", "[+{n}]% Science from Great Scientists' bulbs")
    if num(r["InstantMilitaryIncrease"]):
        out.append("Doubles the number of military units")
    for xr in xp_rows.get(r["Type"], []):
        out.append(f"[+{num(xr['Experience'])}] XP for [{FREE_XP_DOMAIN[xr['DomainType']]}] units per Great Work in this city")
    for yr in religion_rows.get(r["Type"], []):
        # [I]: Times100 like the other per-religion rates (Candi: 200 -> +2 Faith per religion); the effect is a later feature ticket
        if num(yr["Yield"]):
            out.append(f"[+{num(yr['Yield']) // 100} {HOOK_YIELD_NAMES[yr['YieldType']]}] for each religion in this city")
    return out


def build_buildings(gk, techs_json, lk, work_names, report):
    old = {b["name"]: b for b in gk}
    costs = era_costs(techs_json)
    ycs = table_by("Building_YieldChanges", "BuildingType")
    yms = table_by("Building_YieldModifiers", "BuildingType")
    needed = table_by("Building_ClassesNeededInCity", "BuildingType", "BuildingClassType")
    class_over = {r["BuildingType"]: r["CivilizationType"] for r in tsv("Civilization_BuildingClassOverrides")}
    theming = {}
    for tr in tsv("Building_ThemingBonuses"):
        theming.setdefault(tr["BuildingType"], []).append(tr)
    xp_rows, religion_rows = {}, {}
    for xr in tsv("Building_DomainFreeExperiencePerGreatWork"):
        xp_rows.setdefault(xr["BuildingType"], []).append(xr)
    for yr in tsv("Building_YieldChangesPerReligion"):
        religion_rows.setdefault(yr["BuildingType"], []).append(yr)
    out, seen = [], set()
    for r in tsv("Buildings"):
        bnw_name = r["Name"]
        name = KEEP_UNCIV_NAME.get(bnw_name, bnw_name)
        seen.add(name)
        b = dict(old.get(name, {}))
        is_new = name not in old
        b["name"] = name
        bc = lk.building_class[r["BuildingClass"]]
        wonder = bc["MaxGlobalInstances"] == "1"
        national = bc["MaxPlayerInstances"] == "1" and r["Type"] != "BUILDING_PALACE"
        if is_new:
            if wonder:
                b["isWonder"] = True
            elif national:
                b["isNationalWonder"] = True
        # Numbers replaced by BNW values.
        maint = num(r["GoldMaintenance"])
        if maint:
            b["maintenance"] = maint
        else:
            b.pop("maintenance", None)
        hurry = num(r["HurryCostModifier"])
        if hurry > 0:
            b["hurryCostModifier"] = hurry
        elif "hurryCostModifier" in b:
            del b["hurryCostModifier"]
        happy = num(r["Happiness"]) + num(r["UnmoddedHappiness"])
        if happy:
            b["happiness"] = happy
        elif not is_new and name != "Circus Maximus" and name != "Notre Dame" and name != "Eiffel Tower":
            b.pop("happiness", None)
        tech = lk.tech_name.get(r["PrereqTech"])
        if tech:
            b["requiredTech"] = tech
        else:
            b.pop("requiredTech", None)
        # Yields. Culture is kept from G&K for matched objects: Unciv models Great Work slots as flat culture, and the
        # Great Works subsystem (B030) takes that role in BNW (deliberate deviation, see SPEC 2.1 as built B002).
        yields = {YIELDS[x["YieldType"]]: int(x["Yield"]) for x in ycs.get(r["Type"], []) if x["YieldType"] in YIELDS and int(x["Yield"])}
        slotted = bool(r["GreatWorkSlotType"]) and num(r["GreatWorkCount"]) > 0  # the slots replace the flat G&K culture
        for stat in YIELDS.values():
            if stat == "culture" and not is_new and not slotted:
                continue
            if stat in yields:
                b[stat] = yields[stat]
            else:
                b.pop(stat, None)
        pct = {YIELDS[x["YieldType"]]: int(x["Yield"]) for x in yms.get(r["Type"], []) if x["YieldType"] in YIELDS and int(x["Yield"])}
        if pct:
            if is_new or slotted or "culture" not in b.get("percentStatBonus", {}):
                b["percentStatBonus"] = pct
            else:
                b["percentStatBonus"] = dict(pct, culture=b["percentStatBonus"]["culture"])
        elif is_new or slotted or "culture" not in b.get("percentStatBonus", {}):
            b.pop("percentStatBonus", None)
        if slotted:
            b["greatWorkSlotType"] = SLOT_TYPES[r["GreatWorkSlotType"]]
            b["greatWorkSlots"] = num(r["GreatWorkCount"])
            if r["Type"] in theming:
                b["themingBonuses"] = [theming_row(tr) for tr in theming[r["Type"]]]
            if num(r["ConquestProb"]) >= 100 and not b.get("isWonder") and not b.get("isNationalWonder"):
                # BNW keeps these (and the works in them) on capture: without the explicit unique Unciv destroys 34% of non-wonders
                uniques = [u for u in b.get("uniques", []) if u != "Destroyed when the city is captured"]
                b["uniques"] = uniques + ["Never destroyed when the city is captured"]
            if r["FreeGreatWork"]:
                b.setdefault("uniques", []).append(f"Contains the Great Work [{work_names[r['FreeGreatWork']]}] when built")
            report.add("buildings", f"{name}: {b['greatWorkSlots']} {b['greatWorkSlotType']} slot(s), {len(theming.get(r['Type'], []))} theming row(s)")
        for text in building_hook_uniques(r, lk, xp_rows, religion_rows):
            if text not in b.get("uniques", []):
                b.setdefault("uniques", []).append(text)
        # Cost: omit when it equals the era default of the required tech (Unciv derives it), set otherwise.
        cost = num(r["Cost"])
        default = None
        if b.get("requiredTech") in costs:
            default = costs[b["requiredTech"]][1 if (b.get("isWonder") or b.get("isNationalWonder")) else 0]
        if cost > 0 and cost == default:
            b.pop("cost", None)
        elif cost > 0:
            b["cost"] = cost
        elif is_new or "cost" in b:
            if r["Type"] == "BUILDING_PALACE":
                b["cost"] = 0
            else:
                b.pop("cost", None)
        # Specialists and Great People points of a new building.
        if is_new:
            spec = r["SpecialistType"]
            if spec and num(r["SpecialistCount"]):
                b["specialistSlots"] = {spec.replace("SPECIALIST_", "").capitalize(): num(r["SpecialistCount"])}
            gp = num(r["GreatPeopleRateChange"])
            if spec and gp:
                b["greatPersonPoints"] = {"Great " + spec.replace("SPECIALIST_", "").capitalize(): gp}
            if r["Type"] in needed:
                req = lk.building_class_default[needed[r["Type"]][0]]
                b["requiredBuilding"] = lk.building_unciv_name(req)
            if r["Type"] in class_over:
                nation = lk.civ_name[class_over[r["Type"]]]
                b["uniqueTo"] = nation
                b["replaces"] = lk.building_unciv_name(lk.building_class_default[r["BuildingClass"]])
        out.append(b)
        if is_new:
            report.add("buildings", f"{name}: new")
    # Unciv-only objects: keep the projects, report everything else as removed.
    for name, b in old.items():
        if name in seen:
            continue
        if name in GK_BUILDINGS_KEPT_OUTSIDE_TSV:
            out.append(b)
            report.add("buildings", f"{name}: kept (Civ V project that Unciv models as a building, not in the BNW table)")
        elif name in REMOVED["Buildings.json"]:
            report.add("buildings", f"{name}: removed (SPEC 3.3)")
        else:
            out.append(b)
            report.add("buildings", f"{name}: kept (Unciv-only object, not in the BNW tables)")
    return out


# ----------------------------------------------------------------------------------------------------------------------
# Units
# ----------------------------------------------------------------------------------------------------------------------
AIR_UNIT_TYPES = {"Fighter", "Bomber", "Air Missile", "Atomic bomb", "Missile", "Helicopter"}
# BNW units that are scenario-only content (SPEC 3.3 "check ... before adding"): no civ owns them and nothing builds them.
SCENARIO_ONLY_UNITS = {"Bazooka", "XCOM Squad", "Hand-Axe"}


def build_units(gk, lk, report):
    old = {u["name"]: u for u in gk}
    out, seen = [], set()
    for r in tsv("Units"):
        name = r["Name"]
        if r["Type"].startswith("UNIT_BARBARIAN_"):  # same English names as the normal units, would shadow them
            report.add("units", f"{r['Type']}: skipped (barbarian variant shares the name {name} with the normal unit)")
            continue
        if name in SCENARIO_ONLY_UNITS:
            report.add("units", f"{name}: skipped (scenario-only BNW unit, SPEC 3.3)")
            continue
        seen.add(name)
        u = dict(old.get(name, {}))
        is_new = name not in old
        u["name"] = name
        air = u.get("unitType") in AIR_UNIT_TYPES
        strength, ranged = num(r["Combat"]), num(r["RangedCombat"])
        if strength > 0 and not air:
            u["strength"] = strength
        if ranged > 0 and not air:
            u["rangedStrength"] = ranged
        if air and (strength > 0 or ranged > 0) and is_new:
            raise SystemExit("new air unit needs hand-written stats: " + name)
        moves = num(r["Moves"])
        if moves and not air:
            u["movement"] = moves
        rng = num(r["Range"])
        if rng and not air and rng != 2 and ranged > 0:
            u["range"] = rng
        cost = num(r["Cost"])
        tech = lk.tech_name.get(r["PrereqTech"])
        if tech:
            u["requiredTech"] = tech
        elif is_new:
            u.pop("requiredTech", None)
        obs = lk.tech_name.get(r["ObsoleteTech"])
        if obs:
            u["obsoleteTech"] = obs
        elif r["ObsoleteTech"] == "" and not is_new and name in old:
            u.pop("obsoleteTech", None)
        no_cost_unit = name.startswith("Great ") or name in ("Missionary", "Inquisitor")
        if cost > 0 and (is_new or u.get("cost") not in (None, cost) or ("cost" not in u and not no_cost_unit)):
            u["cost"] = cost
        out.append(u)
        if is_new:
            report.add("units", f"{name}: new (see overrides.json for type and uniques)")
    # Upgrades: unit type -> upgrade class -> default unit of that class (Unit_ClassUpgrades.tsv).
    class_default = {c["Type"]: lk.unit_by_type[c["DefaultUnit"]]["Name"] for c in tsv("UnitClasses") if c["DefaultUnit"] in lk.unit_by_type}
    upgrade_of = {}
    for r in tsv("Unit_ClassUpgrades"):
        if r["UnitType"].startswith("UNIT_BARBARIAN_") or r["UnitType"] not in lk.unit_by_type:
            continue
        target = class_default.get(r["UnitClassType"])
        upgrade_of.setdefault(lk.unit_by_type[r["UnitType"]]["Name"], target)
    for u in out:
        target = upgrade_of.get(u["name"])
        if target in seen and target != u["name"]:
            if u.get("upgradesTo") != target:
                report.add("units", f"{u['name']}: upgradesTo {u.get('upgradesTo')} -> {target}")
            u["upgradesTo"] = target
    for name, u in old.items():
        if name in seen:
            continue
        report.add("units", f"{name}: kept (Unciv-only object, not in the BNW tables)")
        out.append(u)
    return out


# ----------------------------------------------------------------------------------------------------------------------
# Overrides
# ----------------------------------------------------------------------------------------------------------------------
def apply_patch(obj, patch):
    for key, value in patch.items():
        if key.endswith("+") and isinstance(value, list):
            obj[key[:-1]] = list(obj.get(key[:-1], [])) + [v for v in value if v not in obj.get(key[:-1], [])]
        elif key.endswith("-") and isinstance(value, list):
            obj[key[:-1]] = [v for v in obj.get(key[:-1], []) if v not in value]
        elif value is None:
            obj.pop(key, None)
        else:
            obj[key] = value


def apply_overrides(files, overrides, report):
    for fname, ops in overrides.get("files", {}).items():
        data = files.setdefault(fname, [])
        if isinstance(data, dict):  # whole-object file (ModOptions)
            apply_patch(data, ops.get("patch", {}))
            continue
        index = {o["name"]: i for i, o in enumerate(data) if isinstance(o, dict) and "name" in o}
        for name in ops.get("remove", []):
            if name in index:
                data[index[name]] = None
                report.add("overrides", f"{fname}: removed {name}")
        files[fname] = data = [o for o in data if o is not None]
        index = {o["name"]: i for i, o in enumerate(data) if "name" in o}
        for name, patch in ops.get("patch", {}).items():
            if name not in index:
                raise SystemExit(f"overrides: {fname} has no object {name} to patch")
            patch = dict(patch)
            if "copyFrom" in patch:  # start from a copy of an analogous object, keep the generated numbers of this one
                analog = copy.deepcopy(data[index[patch.pop("copyFrom")]])
                for key in ("replaces", "uniqueTo", "civilopediaText", "quote", "upgradesTo", "obsoleteTech"):
                    analog.pop(key, None)
                analog.update(data[index[name]])
                data[index[name]] = analog
            apply_patch(data[index[name]], patch)
        for obj in ops.get("add", []):
            if "copyFrom" in obj:
                obj = dict(copy.deepcopy(data[index[obj["copyFrom"]]]), **{k: v for k, v in obj.items() if k != "copyFrom"})
            if obj["name"] in index:
                raise SystemExit(f"overrides: {fname} already has {obj['name']}; use patch")
            data.append(obj)
            index[obj["name"]] = len(data) - 1
            report.add("overrides", f"{fname}: added {obj['name']}")
    return files


# ----------------------------------------------------------------------------------------------------------------------
# ModOptions / ModConstants
# ----------------------------------------------------------------------------------------------------------------------
CONSTANT_RE = re.compile(r"// ([A-Z0-9_]+): BNW (-?[0-9.]+)(?:, G&K -?[0-9.]+)?\s*\n\s*\[JsonPropertyName\(\"(\w+)\"\)\]")


def mod_constants(report):
    text = MOD_CONSTANTS_CS.read_text(encoding="utf-8")
    defines = {r["Name"]: r["Value"] for r in tsv("Defines")}
    out = {}
    for define, value, prop in CONSTANT_RE.findall(text):
        if define in defines and float(defines[define]) != float(value):
            raise SystemExit(f"ModConstants comment for {define} says {value}, Defines.tsv says {defines[define]}")
        f = float(value)
        out[prop] = int(f) if f == int(f) and "." not in value else f
    if len(out) < 70:
        raise SystemExit(f"only {len(out)} BNW constants parsed from ModConstants.cs")
    report.add("modOptions", f"{len(out)} BNW tunables written to ModOptions.json constants (values from the ModConstants.cs comments, cross-checked with Defines.tsv)")
    return out


# ----------------------------------------------------------------------------------------------------------------------
# Policy tree (B050, SPEC 4.6.1)
# ----------------------------------------------------------------------------------------------------------------------
IDEOLOGY_BRANCHES = ("Freedom", "Order", "Autocracy")  # the three ideologies (B051): tenets by level, no opener or finisher
PRIORITY_ANALOG = {"Aesthetics": "Patronage", "Exploration": "Commerce"}  # AI weights of the two new branches


def era_name(code):
    return code[len("ERA_"):].capitalize() + " era" if code else "Ancient era"


def build_policies(gk, overrides, report):
    """The nine social branches and the three ideologies (B051) from the BNW tables."""
    uniques = overrides["policyUniques"]
    gk_by_name = {b["name"]: b for b in gk}
    policies = tsv("Policies")
    branch_rows = tsv("PolicyBranchTypes")
    type_name = {r["Type"]: r["Name"] for r in policies}
    prereqs = {}
    for r in tsv("Policy_PrereqPolicies"):
        prereqs.setdefault(r["PolicyType"], []).append(type_name[r["PrereqPolicy"]])
    used = set()

    def take(name):
        if name not in uniques:
            raise SystemExit(f"overrides.policyUniques has no entry for {name}")
        used.add(name)
        return list(uniques[name])

    out = []
    for br in branch_rows:
        name = br["Name"]
        members = sorted((r for r in policies if r["PolicyBranchType"] == br["Type"]),
                         key=lambda r: (int(r["GridY"]), int(r["GridX"])))
        gk_branch = gk_by_name.get(name) or gk_by_name[PRIORITY_ANALOG[name]]
        if name in IDEOLOGY_BRANCHES:
            out.append(build_ideology(br, members, gk_branch, uniques, report))
            continue
        branch = {"name": name, "era": era_name(br["EraPrereq"]),
                  "priorities": dict(gk_branch["priorities"]), "uniques": take(name), "policies": []}
        for r in members:
            p = {"name": r["Name"], "uniques": take(r["Name"]), "row": int(r["GridY"]), "column": int(r["GridX"])}
            if r["Type"] in prereqs:
                p["requires"] = prereqs[r["Type"]]
            branch["policies"].append(p)
        branch["policies"].append({"name": name + " Complete", "uniques": take(name + " Complete")})
        report.add("policies", f"{name}: {branch['era']}, {len(members)} policies + finisher")
        out.append(branch)
    unused = sorted(set(uniques) - used)
    if unused:
        raise SystemExit(f"overrides.policyUniques has unused entries: {unused}")
    return out


def build_ideology(br, members, gk_branch, uniques, report):
    """An ideology (SPEC 4.6.3): the branch policy is the free choice, its tenets are bought by Level (7 / 6 / 3), no finisher.
    PurchaseByLevel and the adopter counts come from PolicyBranchTypes.tsv; the Universal Healthcare tenet exists once per ideology, so the
    name is suffixed to stay unique; tenet effects (B010 and the subsystem tickets) are pending, the tenets carry the structure only."""
    name = br["Name"]
    counts = {}
    policies = []
    for r in sorted(members, key=lambda r: (int(r["Level"]), r["Type"])):
        level = int(r["Level"])
        counts[level] = counts.get(level, 0) + 1
        tenet = r["Name"] + f" ({name})" if r["Name"] == "Universal Healthcare" else r["Name"]
        policies.append({"name": tenet, "level": level, "row": level, "column": counts[level] - 1, "uniques": []})
    if counts != {1: 7, 2: 6, 3: 3}:
        raise SystemExit(f"{name}: tenet levels {counts}, expected 7/6/3")
    for r in [br]:
        if r["PurchaseByLevel"] != "1":
            raise SystemExit(f"{name}: PurchaseByLevel is not set in PolicyBranchTypes.tsv")
    report.add("policies", f"{name}: ideology, {era_name(br['EraPrereq'])}, 16 tenets (7/6/3), free tenets {br['FirstAdopterFreePolicies']} (first adopter) / {br['SecondAdopterFreePolicies']} (second)")
    return {"name": name, "era": era_name(br["EraPrereq"]), "priorities": dict(gk_branch["priorities"]), "uniques": [],
            "purchaseByLevel": True, "firstAdopterFreePolicies": int(br["FirstAdopterFreePolicies"]),
            "secondAdopterFreePolicies": int(br["SecondAdopterFreePolicies"]), "policies": policies}


def gate_wonders_by_branch(buildings, report):
    """Buildings.PolicyBranchType (SPEC 4.6.1): the wonder needs its branch adopted ([K] adopted, not merely open)."""
    branch_of = {r["Type"]: r["Name"] for r in tsv("PolicyBranchTypes")}
    by_name = {b["name"]: b for b in buildings}
    for r in tsv("Buildings"):
        if not r["PolicyBranchType"]:
            continue
        name = KEEP_UNCIV_NAME.get(r["Name"], r["Name"])
        branch = branch_of[r["PolicyBranchType"]]
        by_name[name].setdefault("uniques", []).append(f"Only available <after adopting [{branch}]>")
        report.add("policies", f"{name}: needs the {branch} branch adopted")


# ----------------------------------------------------------------------------------------------------------------------
SWEPT_TABLES = ["Technologies", "Buildings", "Units", "Policies", "Resources", "Improvements"]


# Columns the generator code does not read but overrides.json states by hand (checked on the real ruleset by BnwGeneratedRulesetTests).
OVERRIDE_COLUMNS = {
    "Buildings.NullifyInfluenceModifier": "Great Firewall: 'Other civs' cultural influence is nullified here'",
    "Buildings.ExtraLeagueVotes": "Forbidden Palace: '[+2] Delegates in the World Congress'",
    "Buildings.UnlockedByLeague": "International Space Station: 'Unbuildable' (the league project reward is B015)",
    "Units.OneShotTourism": "Great Musician: the tourism blast unique",
    "Units.OneShotTourismPercentOthers": "Great Musician: the tourism blast unique",
    "Units.Trade": "Caravan, Cargo Ship: 'Can establish trade routes' (they stay Unbuildable until B006)",
    "Units.BaseCultureTurnsToCount": "Great Writer: the Great Work culture turns",
}


def unread_columns_report(report):
    """B033: the non-empty columns of the swept tables that no generator code read, so a gap such as B027/B032 shows up in review.
    Most are Civ V columns Unciv has no counterpart for (art, AI, spaceship, advanced start), or are carried by the G&K objects the
    generator starts from; Policies are hand-curated in overrides.json (policyUniques), Units keep their G&K types and uniques."""
    for table in SWEPT_TABLES:
        with open(DATA / (table + ".tsv"), encoding="utf-8", newline="") as f:
            rows = list(csv.DictReader(f, delimiter="\t"))
        read = READ_COLUMNS.get(table, set())
        for column in rows[0]:
            count = sum(1 for r in rows if r[column] not in ("", "0"))
            key = f"{table}.{column}"
            if count and column not in read:
                if key in OVERRIDE_COLUMNS:
                    report.add("unread columns (stated in overrides.json)", f"{key}: {OVERRIDE_COLUMNS[key]}")
                else:
                    report.add("unread columns", f"{key}: {count} non-empty row(s)")


def generate():
    report = Report()
    READ_COLUMNS.clear()
    files = {p.name: lenient_loads(p.read_text(encoding="utf-8")) for p in sorted(GK_DIR.glob("*.json"))}
    files = {n: apply_renames(v, RENAMES) for n, v in files.items()}
    for old_name, new_name in RENAMES.items():
        report.add("renamed", f"{old_name} -> {new_name}")
    lk = Lookups()
    files["Techs.json"] = build_techs(files["Techs.json"], report)
    files["GreatWorks.json"], work_names = build_great_works(report)
    files["Buildings.json"] = build_buildings(files["Buildings.json"], files["Techs.json"], lk, work_names, report)
    files["Units.json"] = build_units(files["Units.json"], lk, report)
    for fname, names in REMOVED.items():
        if fname == "Buildings.json":
            continue
        for n in sorted(names):
            report.add(fname, f"{n}: removed")
        files[fname] = [o for o in files[fname] if o.get("name") not in names]
    removed_victories = REMOVED["VictoryTypes.json"]
    for p in files["Policies.json"]:
        for v in removed_victories:
            p.get("priorities", {}).pop(v, None)
    for n in files["Nations.json"]:
        if n.get("preferredVictoryType") in removed_victories:
            report.add("Nations.json", f"{n['name']}: preferredVictoryType {n['preferredVictoryType']} -> Neutral (victory type removed until B063)")
            n["preferredVictoryType"] = "Neutral"
    overrides = lenient_loads(OVERRIDES.read_text(encoding="utf-8"))
    files["Policies.json"] = build_policies(files["Policies.json"], overrides, report)
    gate_wonders_by_branch(files["Buildings.json"], report)
    for text in overrides.get("policyPending", []):
        report.add("pending policy effects (not emitted)", text)
    files = apply_overrides(files, overrides, report)
    name_groups_from_works(files, report)
    for key, texts in overrides.get("pending", {}).items():
        for t in texts:
            report.add("pending uniques (not emitted: need B003 UniqueTypes, wired by B010 or the feature ticket)", f"{key}: {t}")
    unread_columns_report(report)
    consts = mod_constants(report)
    mo = files["ModOptions.json"]
    mo["uniques"] = [u for u in overrides.get("gates", [])]
    mo["constants"] = dict(mo.get("constants", {}), **consts)
    return files, report


def render(files):
    return {name: dump_json(value) + "\n" for name, value in files.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    files, report = generate()
    rendered = render(files)
    rendered_report = report.render() + "\n"
    if args.check:
        bad = [n for n, t in rendered.items() if not (OUT_DIR / n).exists() or (OUT_DIR / n).read_text(encoding="utf-8") != t]
        if not REPORT.exists() or REPORT.read_text(encoding="utf-8") != rendered_report:
            bad.append(str(REPORT.name))
        if bad:
            print("stale generated files: " + ", ".join(bad))
            return 1
        return 0
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, text in rendered.items():
        (OUT_DIR / name).write_text(text, encoding="utf-8")
    REPORT.write_text(rendered_report, encoding="utf-8")
    print(f"wrote {len(rendered)} files to {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
