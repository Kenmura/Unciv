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
    "VictoryTypes.json": {"Cultural", "Diplomatic"},  # rewritten by B032 / B063: their milestones need new kinds
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


def tsv(name):
    with open(DATA / (name + ".tsv"), encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter="\t"))


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
def build_techs(gk_techs, report):
    cols = {c["columnNumber"]: {k: v for k, v in c.items() if k != "techs"} for c in gk_techs}
    old = {t["name"]: t for c in gk_techs for t in c["techs"]}
    by_type = {r["Type"]: r["Name"] for r in tsv("Technologies")}
    prereqs = {}
    for r in tsv("Technology_PrereqTechs"):
        prereqs.setdefault(r["TechType"], []).append(by_type[r["PrereqTech"]])
    grouped = {}
    for r in tsv("Technologies"):
        name = r["Name"]
        col = int(r["GridX"])
        if col not in cols:
            raise SystemExit(f"tech {name}: no G&K column {col}")
        t = dict(old.get(name, {}))
        is_new = name not in old
        t["name"] = name
        t["row"] = int(r["GridY"]) + 1
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
# Buildings
# ----------------------------------------------------------------------------------------------------------------------
def build_buildings(gk, techs_json, lk, report):
    old = {b["name"]: b for b in gk}
    costs = era_costs(techs_json)
    ycs = table_by("Building_YieldChanges", "BuildingType")
    yms = table_by("Building_YieldModifiers", "BuildingType")
    needed = table_by("Building_ClassesNeededInCity", "BuildingType", "BuildingClassType")
    class_over = {r["BuildingType"]: r["CivilizationType"] for r in tsv("Civilization_BuildingClassOverrides")}
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
        for stat in YIELDS.values():
            if stat == "culture" and not is_new:
                continue
            if stat in yields:
                b[stat] = yields[stat]
            else:
                b.pop(stat, None)
        pct = {YIELDS[x["YieldType"]]: int(x["Yield"]) for x in yms.get(r["Type"], []) if x["YieldType"] in YIELDS and int(x["Yield"])}
        if pct:
            if is_new or "culture" not in b.get("percentStatBonus", {}):
                b["percentStatBonus"] = pct
            else:
                b["percentStatBonus"] = dict(pct, culture=b["percentStatBonus"]["culture"])
        elif is_new or "culture" not in b.get("percentStatBonus", {}):
            b.pop("percentStatBonus", None)
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
IDEOLOGY_BRANCHES = ("Freedom", "Order", "Autocracy")  # kept as in G&K until B051
PRIORITY_ANALOG = {"Aesthetics": "Patronage", "Exploration": "Commerce"}  # AI weights of the two new branches


def era_name(code):
    return code[len("ERA_"):].capitalize() + " era" if code else "Ancient era"


def build_policies(gk, overrides, report):
    """The nine social branches from the BNW tables; the three ideology branches stay as in G&K (B051)."""
    uniques = overrides["policyUniques"]
    gk_by_name = {b["name"]: b for b in gk}
    policies = tsv("Policies")
    branch_rows = [r for r in tsv("PolicyBranchTypes") if r["Name"] not in IDEOLOGY_BRANCHES]
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
    for b in gk:
        if b["name"] in IDEOLOGY_BRANCHES:
            out.append(b)
    return out


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
def generate():
    report = Report()
    files = {p.name: lenient_loads(p.read_text(encoding="utf-8")) for p in sorted(GK_DIR.glob("*.json"))}
    files = {n: apply_renames(v, RENAMES) for n, v in files.items()}
    for old_name, new_name in RENAMES.items():
        report.add("renamed", f"{old_name} -> {new_name}")
    lk = Lookups()
    files["Techs.json"] = build_techs(files["Techs.json"], report)
    files["Buildings.json"] = build_buildings(files["Buildings.json"], files["Techs.json"], lk, report)
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
            report.add("Nations.json", f"{n['name']}: preferredVictoryType {n['preferredVictoryType']} -> Neutral (victory type removed until B032/B063)")
            n["preferredVictoryType"] = "Neutral"
    overrides = lenient_loads(OVERRIDES.read_text(encoding="utf-8"))
    files["Policies.json"] = build_policies(files["Policies.json"], overrides, report)
    gate_wonders_by_branch(files["Buildings.json"], report)
    for text in overrides.get("policyPending", []):
        report.add("pending policy effects (not emitted)", text)
    files = apply_overrides(files, overrides, report)
    for key, texts in overrides.get("pending", {}).items():
        for t in texts:
            report.add("pending uniques (not emitted: need B003 UniqueTypes, wired by B010 or the feature ticket)", f"{key}: {t}")
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
