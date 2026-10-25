package com.unciv.logic

// T160: JVM exporter for the C# KotlinGoldenTests. NOT part of the Kotlin tree: csharp/migration/golden/export.sh copies it into
// tests/src for one gradle run and removes it again. Starts the same games as the C# test (Pangaea, Rectangular, Tiny, Human+AI, no
// city states) and writes one canonical digest line per game: the map without resources and the starting civ/unit state.
// No per-turn digests: Kotlin's StrategicBonusResourcePlacementLogic uses unseeded shuffled(), so a Kotlin game is not reproducible
// once resources matter (see the T160 notes).
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
        out.writeText(lines.toString())
    }
}
