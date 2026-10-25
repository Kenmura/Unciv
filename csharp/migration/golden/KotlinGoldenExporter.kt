package com.unciv.logic

// T160: JVM exporter for the C# KotlinGoldenTests. NOT part of the Kotlin tree: csharp/migration/golden/export.sh copies it into
// tests/src for one gradle run and removes it again. Starts the same games as the C# test (Pangaea, Rectangular, Tiny, Human+AI, no
// city states) and writes one canonical digest line per game: the map without resources and the starting civ/unit state.
// T216: also exports per-turn digests from saved map files (see exportSavedMapTurns).
import com.unciv.UncivGame
import com.unciv.logic.civilization.PlayerType
import com.unciv.logic.map.MapParameters
import com.unciv.logic.map.MapShape
import com.unciv.logic.map.MapSize
import com.unciv.logic.map.MapType
import com.unciv.models.metadata.GameParameters
import com.unciv.models.metadata.GameSettings
import com.unciv.models.metadata.GameSetupInfo
import com.unciv.models.metadata.Player
import com.unciv.models.ruleset.RulesetCache
import com.unciv.testing.BaseTestRunner
import org.junit.Test
import org.junit.runner.RunWith
import com.unciv.logic.files.MapSaver
import com.badlogic.gdx.files.FileHandle
import java.io.File

@RunWith(BaseTestRunner::class)
class KotlinGoldenExporter {
    private fun fnv(text: String): String {
        var h = -0x340d631b7bdddcdbL // 14695981039346656037 as signed
        for (b in text.toByteArray(Charsets.UTF_8)) {
            h = h xor (b.toLong() and 0xff)
            h *= 0x100000001b3L
        }
        return java.lang.Long.toUnsignedString(h, 16).padStart(16, '0')
    }

    private fun mapDigest(game: GameInfo): String {
        val sb = StringBuilder()
        for (t in game.tileMap.tileList.sortedWith(compareBy({ it.position.x }, { it.position.y }))) {
            sb.append("T ${t.position.x},${t.position.y} ${t.baseTerrain}|${t.terrainFeatures.filter { it != "Oasis" }.sorted().joinToString("+")}|${t.improvement ?: ""}|${t.naturalWonder ?: ""}|${t.roadStatus}\n")
        }
        return sb.toString()
    }

    private fun civDigest(game: GameInfo): String {
        val sb = StringBuilder()
        sb.append("turn ${game.turns}\n")
        for (civ in game.civilizations) {
            sb.append("C ${civ.civName} gold=${civ.gold} cities=${civ.cities.size} units=${civ.units.getCivUnitsSize()} techs=${civ.tech.techsResearched.size}\n")
            for (c in civ.cities.sortedBy { it.name })
                sb.append("  city ${c.name} ${c.location.x},${c.location.y} pop=${c.population.population} tiles=${c.tiles.size}\n")
            for (u in civ.units.getCivUnits().map { "  unit ${it.name} ${it.getTile().position.x},${it.getTile().position.y}" }.sorted())
                sb.append(u).append('\n')
        }
        return sb.toString()
    }

    // T216: everything per-turn, resources included (they are fixed because the game starts from a saved map).
    private fun fullDigest(game: GameInfo): String {
        val sb = StringBuilder()
        for (t in game.tileMap.tileList.sortedWith(compareBy({ it.position.x }, { it.position.y })))
            sb.append("T ${t.position.x},${t.position.y} ${t.baseTerrain}|${t.terrainFeatures.sorted().joinToString("+")}|${t.resource ?: ""}|${t.improvement ?: ""}|${t.roadStatus}|${t.getOwner()?.civName ?: ""}\n")
        return sb.toString() + civDigest(game)
    }

    private fun debugRanks(game: GameInfo): String {
        val sb = StringBuilder()
        for (civ in game.civilizations) for (u in civ.units.getCivUnits())
            sb.append("#DBG U ${civ.civName} ${u.name} ${u.getTile().position.x},${u.getTile().position.y} id=${u.id} mov=${u.currentMovement} hp=${u.health} action=${u.action}\n")
        for (civ in game.civilizations) for (c in civ.cities) {
            sb.append("#DBG ${c.name} techs=${civ.tech.techsResearched.sorted()}\n")
            for (t in c.expansion.getChoosableTiles().toList())
                sb.append("#DBG  ${t.position.x},${t.position.y} rank=${c.expansion.rankTileForExpansion(t)} stats=${t.stats.getTileStats(c, civ)} see=${civ.canSeeResource(t.tileResource)}\n")
        }
        return sb.toString()
    }

    private fun exportSavedMapTurns(lines: StringBuilder, mapSeed: Int, turns: Int, dir: File, dump: File?) {
        val nations = listOf("Babylon", "Greece", "Rome")
        val ruleset = RulesetCache.getVanillaRuleset()
        fun params() = GameParameters().apply {
            baseRuleset = ruleset.name
            numberOfCityStates = 0
            victoryTypes = ArrayList(ruleset.victories.keys)
            players.clear()
            nations.forEachIndexed { i, n -> players.add(Player(n, if (i == 0) PlayerType.Human else PlayerType.AI)) }
        }
        val genMap = MapParameters().apply {
            type = MapType.pangaea; shape = MapShape.rectangular; seed = mapSeed.toLong(); mapSize = MapSize.Tiny
        }
        // Generate once (unseeded resource shuffles), strip the units, save as a map file; an existing file is kept (delete it to regenerate).
        val mapFile = File(dir, "golden-map-$mapSeed.map")
        if (!mapFile.exists()) {
            val gen = GameStarter.startNewGame(GameSetupInfo(params(), genMap))
            // Pin every civ's start (the settler's tile): picking a start otherwise sorts a HashMap<Tile, Float> by score, and Tile has an
            // identity hashCode, so ties come out in a different order on every JVM run.
            for (civ in gen.civilizations.filter { it.isMajorCiv() })
                gen.tileMap.startingLocations.add(com.unciv.logic.map.TileMap.StartingLocation(civ.units.getCivUnits().first().getTile().position, civ.civName))
            for (t in gen.tileMap.tileList) for (u in t.getUnits().toList()) u.removeFromTile()
            mapFile.writeText(MapSaver.mapToSavedString(gen.tileMap))
        }
        // Start the real game from that file: the map is fixed, only seeded randomness is left.
        val saved = MapParameters().apply { name = "golden$mapSeed"; type = MapType.pangaea; shape = MapShape.rectangular; seed = mapSeed.toLong(); mapSize = MapSize.Tiny }
        val setup = GameSetupInfo(params(), saved).apply { this.mapFile = FileHandle(mapFile) }
        val game = GameStarter.startNewGame(setup)
        UncivGame.Current.gameInfo = game
        // From here on every stateBasedRandom is seeded by gameId: pin it, so two Kotlin runs are comparable (the start used the random one).
        game.gameId = "golden-$mapSeed"
        lines.append("saved seed=$mapSeed gameId=${game.gameId} file=golden-map-$mapSeed.map\n")
        for (turn in 0..turns) {
            if (turn > 0) game.nextTurn()
            val d = fullDigest(game)
            lines.append("turn seed=$mapSeed n=${game.turns} digest=${fnv(d)}\n")
            dump?.appendText("== saved seed $mapSeed turn ${game.turns}\n$d" + debugRanks(game))
        }
    }

    @Test
    fun export() {
        val out = File(System.getenv("GOLDEN_OUT") ?: "/tmp/kotlin-golden.txt")
        val dump = System.getenv("GOLDEN_DUMP")?.takeIf { it.isNotEmpty() }?.let { File(it) }
        val lines = StringBuilder()
        RulesetCache.loadRulesets(noMods = true)
        UncivGame.Current = UncivGame()
        UncivGame.Current.settings = GameSettings()
        for ((mapSeed, nations) in listOf(Pair(3, listOf("Babylon", "Greece", "Rome")), Pair(11, listOf("Babylon", "Greece", "Rome")))) {
            val ruleset = RulesetCache.getVanillaRuleset()
            val param = GameParameters().apply {
                baseRuleset = ruleset.name
                numberOfCityStates = 0
                victoryTypes = ArrayList(ruleset.victories.keys)
                players.clear()
                nations.forEachIndexed { i, n -> players.add(Player(n, if (i == 0) PlayerType.Human else PlayerType.AI)) }
            }
            val map = MapParameters().apply {
                type = MapType.pangaea; shape = MapShape.rectangular; seed = mapSeed.toLong(); mapSize = MapSize.Tiny
            }
            val game = GameStarter.startNewGame(GameSetupInfo(param, map))
            UncivGame.Current.gameInfo = game
            lines.append("game seed=$mapSeed gameId=${game.gameId} map=${fnv(mapDigest(game))} start=${fnv(civDigest(game))}\n")
            dump?.appendText("== seed $mapSeed\n" + mapDigest(game) + civDigest(game))
        }
        for (seed in listOf(3, 11)) exportSavedMapTurns(lines, seed, 30, out.parentFile, dump)
        out.writeText(lines.toString())
    }
}
