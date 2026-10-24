# T023 data generators

`Unciv.Core/Models/Translations/JavaLocaleData.cs` and `Unciv.Core.Tests/Models/Translations/NumberFormat.golden.tsv` come from the real
JVM classes (JDK 25, CLDR), because `InvariantGlobalization` removes the culture data the Kotlin code reads from `java.util.Locale`.

Classpath: the installed `Unciv.jar`, kotlin-stdlib 2.3.0, libGDX 1.14.2 (+ Ktor for nothing here). Run in the `dotnet-dev` container.

1. `javac -cp $CP Gen.java && java -cp .:$CP Gen` writes `num.tsv` (per `LocaleCode` language: DecimalFormat symbols) and `case.tsv`
   (case mapping of every BMP char for `Locale.ROOT` and each locale language).
2. A throw-away test dumps `char.ToUpperInvariant/ToLowerInvariant` for all BMP chars as `net_case.tsv` (`code<TAB>upper<TAB>lower`, hex, only
   chars that change). `gen.py` keeps the differences and writes `JavaLocaleData.cs`.
3. `Nm2.java` writes `nf.tsv`: fixed numbers per language (N), reformat probes (P), seeded fuzz (D, L). `NumberFormat.golden.tsv` is a thinned
   copy (every 90th D row, every 12th L row, without the 1.0E23-ish probe that the JVM's historic digit generator formats differently).
