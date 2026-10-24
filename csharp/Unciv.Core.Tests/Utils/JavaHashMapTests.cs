using Unciv.Logic.Map;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Core.Tests.Utils;

// Golden iteration orders: each scenario below ran on the real java.util.HashSet/HashMap (OpenJDK 25) and on Kotlin
// stdlib 2.3.0 hashSetOf/toHashSet/hashMapOf (HexCoord from the installed Unciv.jar), from the same one-line script,
// which is interpreted here against JavaHashSet/JavaHashMap. Table sizes were read from the JVM by reflection.
public class JavaHashMapTests
{
    [Theory]
    [InlineData("strings grow", "type string; set; add Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi Olympia Troy Ephesus Byzantium Syracuse Marathon Argos Miletus Rhodes Knossos Halicarnassus Pergamon Tyre Sidon Nineveh Ur Uruk Susa Ecbatana Sardis Gordion Hattusa Kadesh Megiddo Jericho Petra Palmyra", "Pergamon,Rhodes,Jericho,Byzantium,Palmyra,Kadesh,Sidon,Carthage,Rome,Argos,Persepolis,Hattusa,Nineveh,Syracuse,Memphis,Alexandria,Corinth,Ephesus,Thebes,Ecbatana,Megiddo,Sparta,Olympia,Sardis,Knossos,Athens,Babylon,Antioch,Delphi,Troy,Tyre,Thebes2,Miletus,Susa,Gordion,Petra,Marathon,Halicarnassus,Uruk,Ur")]
    [InlineData("strings few", "type string; set; add Rome Athens Sparta Thebes Corinth", "Sparta,Rome,Athens,Corinth,Thebes")]
    [InlineData("strings remove readd", "type string; set; add Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi Olympia Troy Ephesus Byzantium Syracuse Marathon Argos; rm Rome Troy Argos; add Rome Zeus Hera", "Sparta,Olympia,Zeus,Byzantium,Athens,Babylon,Antioch,Delphi,Hera,Carthage,Thebes2,Rome,Persepolis,Syracuse,Memphis,Marathon,Alexandria,Corinth,Ephesus,Thebes")]
    [InlineData("ints small", "type int; set; add 5 3 100 -7 17 33 16 48 1 0 65536 65537 -65536 2147483647 -2147483648 1000 999 12 13 14 15 31 32 64 128 256", "0,65537,2147483647,-2147483648,64,128,256,1,65536,3,5,-7,12,13,14,15,16,17,31,32,33,100,999,1000,48,-65536")]
    [InlineData("ints 200 shuffled", "type int; set; add 29 5 42 151 82 103 121 157 14 65 122 87 7 68 76 4 95 106 30 166 115 26 129 2 34 67 33 116 198 158 114 181 131 180 187 133 179 194 73 48 38 178 1 140 43 70 84 169 113 111 124 168 104 185 152 80 182 69 56 99 163 91 165 36 52 92 155 11 101 71 51 175 176 85 28 136 100 189 119 46 60 3 107 50 125 126 160 199 40 8 118 184 90 61 173 164 9 59 172 134 188 45 89 146 41 105 196 74 72 98 123 86 6 193 132 20 44 97 22 54 66 154 186 78 159 174 191 88 127 135 21 63 153 64 77 93 117 120 81 110 137 128 53 170 17 141 192 96 49 156 149 27 47 144 79 147 31 139 37 183 75 35 143 12 57 195 197 171 148 177 190 161 162 58 32 145 16 109 142 24 62 18 108 112 23 10 55 130 15 150 94 25 138 19 13 167 102 39 83", "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,129,130,131,132,133,134,135,136,137,138,139,140,141,142,143,144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,159,160,161,162,163,164,165,166,167,168,169,170,171,172,173,174,175,176,177,178,179,180,181,182,183,184,185,186,187,188,189,190,191,192,193,194,195,196,197,198,199")]
    [InlineData("ints cap 2", "type int; set cap=2; add 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20", "1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20")]
    [InlineData("ints cap 100", "type int; set cap=100; add 29 5 42 151 82 103 121 157 14 65 122 87 7 68 76 4 95 106 30 166 115 26 129 2 34 67 33 116 198 158 114 181 131 180 187 133 179 194 73 48 38 178 1 140 43 70 84 169 113 111 124 168 104 185 152 80 182 69 56 99", "129,1,2,131,4,5,133,7,140,14,151,152,26,29,157,30,158,33,34,166,38,168,169,42,43,48,178,179,180,181,182,56,185,187,65,194,67,68,69,198,70,73,76,80,82,84,87,95,99,103,104,106,111,113,114,115,116,121,122,124")]
    [InlineData("ints cap 0", "type int; set cap=0; add 7 8 9 10", "8,9,10,7")]
    [InlineData("clear keeps table", "type int; set; add 29 5 42 151 82 103 121 157 14 65 122 87 7 68 76 4 95 106 30 166 115 26 129 2 34 67 33 116 198 158; clear; add 100 33 17 65 1", "65,1,17,33,100")]
    [InlineData("hashSetOf 3", "type string; hashSetOf Rome Athens Sparta", "Sparta,Rome,Athens")]
    [InlineData("hashSetOf 7", "type string; hashSetOf Rome Athens Sparta Thebes Corinth Babylon Memphis", "Sparta,Rome,Memphis,Athens,Babylon,Corinth,Thebes")]
    [InlineData("hashSetOf 13", "type string; hashSetOf Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi", "Sparta,Athens,Babylon,Antioch,Delphi,Carthage,Rome,Thebes2,Persepolis,Memphis,Alexandria,Corinth,Thebes")]
    [InlineData("hashSetOf 1 and 2", "type int; hashSetOf 42", "42")]
    [InlineData("toHashSet list 10", "type string; toHashSet Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis", "Carthage,Sparta,Rome,Thebes2,Persepolis,Memphis,Athens,Babylon,Corinth,Thebes")]
    [InlineData("toHashSet list 40", "type string; toHashSet Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi Olympia Troy Ephesus Byzantium Syracuse Marathon Argos Miletus Rhodes Knossos Halicarnassus Pergamon Tyre Sidon Nineveh Ur Uruk Susa Ecbatana Sardis Gordion Hattusa Kadesh Megiddo Jericho Petra Palmyra", "Pergamon,Rhodes,Jericho,Byzantium,Palmyra,Kadesh,Sidon,Carthage,Rome,Argos,Persepolis,Hattusa,Nineveh,Syracuse,Memphis,Alexandria,Corinth,Ephesus,Thebes,Ecbatana,Megiddo,Sparta,Olympia,Sardis,Knossos,Athens,Babylon,Antioch,Delphi,Troy,Tyre,Thebes2,Miletus,Susa,Gordion,Petra,Marathon,Halicarnassus,Uruk,Ur")]
    [InlineData("copy 5", "type string; setCopy Rome Athens Sparta Thebes Corinth", "Sparta,Rome,Athens,Corinth,Thebes")]
    [InlineData("copy 12", "type string; setCopy Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria", "Carthage,Sparta,Rome,Thebes2,Persepolis,Memphis,Athens,Babylon,Alexandria,Corinth,Antioch,Thebes")]
    [InlineData("copy 13", "type string; setCopy Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi", "Sparta,Athens,Babylon,Antioch,Delphi,Carthage,Rome,Thebes2,Persepolis,Memphis,Alexandria,Corinth,Thebes")]
    [InlineData("copy 40", "type string; setCopy Rome Athens Sparta Thebes Corinth Babylon Memphis Thebes2 Carthage Persepolis Antioch Alexandria Delphi Olympia Troy Ephesus Byzantium Syracuse Marathon Argos Miletus Rhodes Knossos Halicarnassus Pergamon Tyre Sidon Nineveh Ur Uruk Susa Ecbatana Sardis Gordion Hattusa Kadesh Megiddo Jericho Petra Palmyra", "Pergamon,Rhodes,Jericho,Byzantium,Palmyra,Kadesh,Sidon,Carthage,Rome,Argos,Persepolis,Hattusa,Nineveh,Syracuse,Memphis,Alexandria,Corinth,Ephesus,Thebes,Ecbatana,Megiddo,Sparta,Olympia,Sardis,Knossos,Athens,Babylon,Antioch,Delphi,Troy,Tyre,Thebes2,Miletus,Susa,Gordion,Petra,Marathon,Halicarnassus,Uruk,Ur")]
    [InlineData("hex 7x7", "type hex; set; add -3:-3 -3:-2 -3:-1 -3:0 -3:1 -3:2 -3:3 -2:-3 -2:-2 -2:-1 -2:0 -2:1 -2:2 -2:3 -1:-3 -1:-2 -1:-1 -1:0 -1:1 -1:2 -1:3 0:-3 0:-2 0:-1 0:0 0:1 0:2 0:3 1:-3 1:-2 1:-1 1:0 1:1 1:2 1:3 2:-3 2:-2 2:-1 2:0 2:1 2:2 2:3 3:-3 3:-2 3:-1 3:0 3:1 3:2 3:3", "0:-1,0:0,0:-2,0:1,0:-3,0:2,0:3,-1:3,-1:2,1:-3,-1:1,1:-2,-1:0,1:-1,-1:-1,1:0,-1:-2,1:1,-1:-3,1:2,1:3,-2:3,-2:2,2:-3,-2:1,2:-2,-2:0,2:-1,-2:-1,2:0,-2:-2,2:1,-2:-3,2:2,2:3,-3:3,-3:2,3:-3,-3:1,3:-2,-3:0,3:-1,-3:-1,3:0,-3:-2,3:1,-3:-3,3:2,3:3")]
    [InlineData("hex 25x25", "type hex; set; add -12:-12 -12:-11 -12:-10 -12:-9 -12:-8 -12:-7 -12:-6 -12:-5 -12:-4 -12:-3 -12:-2 -12:-1 -12:0 -12:1 -12:2 -12:3 -12:4 -12:5 -12:6 -12:7 -12:8 -12:9 -12:10 -12:11 -12:12 -11:-12 -11:-11 -11:-10 -11:-9 -11:-8 -11:-7 -11:-6 -11:-5 -11:-4 -11:-3 -11:-2 -11:-1 -11:0 -11:1 -11:2 -11:3 -11:4 -11:5 -11:6 -11:7 -11:8 -11:9 -11:10 -11:11 -11:12 -10:-12 -10:-11 -10:-10 -10:-9 -10:-8 -10:-7 -10:-6 -10:-5 -10:-4 -10:-3 -10:-2 -10:-1 -10:0 -10:1 -10:2 -10:3 -10:4 -10:5 -10:6 -10:7 -10:8 -10:9 -10:10 -10:11 -10:12 -9:-12 -9:-11 -9:-10 -9:-9 -9:-8 -9:-7 -9:-6 -9:-5 -9:-4 -9:-3 -9:-2 -9:-1 -9:0 -9:1 -9:2 -9:3 -9:4 -9:5 -9:6 -9:7 -9:8 -9:9 -9:10 -9:11 -9:12 -8:-12 -8:-11 -8:-10 -8:-9 -8:-8 -8:-7 -8:-6 -8:-5 -8:-4 -8:-3 -8:-2 -8:-1 -8:0 -8:1 -8:2 -8:3 -8:4 -8:5 -8:6 -8:7 -8:8 -8:9 -8:10 -8:11 -8:12 -7:-12 -7:-11 -7:-10 -7:-9 -7:-8 -7:-7 -7:-6 -7:-5 -7:-4 -7:-3 -7:-2 -7:-1 -7:0 -7:1 -7:2 -7:3 -7:4 -7:5 -7:6 -7:7 -7:8 -7:9 -7:10 -7:11 -7:12 -6:-12 -6:-11 -6:-10 -6:-9 -6:-8 -6:-7 -6:-6 -6:-5 -6:-4 -6:-3 -6:-2 -6:-1 -6:0 -6:1 -6:2 -6:3 -6:4 -6:5 -6:6 -6:7 -6:8 -6:9 -6:10 -6:11 -6:12 -5:-12 -5:-11 -5:-10 -5:-9 -5:-8 -5:-7 -5:-6 -5:-5 -5:-4 -5:-3 -5:-2 -5:-1 -5:0 -5:1 -5:2 -5:3 -5:4 -5:5 -5:6 -5:7 -5:8 -5:9 -5:10 -5:11 -5:12 -4:-12 -4:-11 -4:-10 -4:-9 -4:-8 -4:-7 -4:-6 -4:-5 -4:-4 -4:-3 -4:-2 -4:-1 -4:0 -4:1 -4:2 -4:3 -4:4 -4:5 -4:6 -4:7 -4:8 -4:9 -4:10 -4:11 -4:12 -3:-12 -3:-11 -3:-10 -3:-9 -3:-8 -3:-7 -3:-6 -3:-5 -3:-4 -3:-3 -3:-2 -3:-1 -3:0 -3:1 -3:2 -3:3 -3:4 -3:5 -3:6 -3:7 -3:8 -3:9 -3:10 -3:11 -3:12 -2:-12 -2:-11 -2:-10 -2:-9 -2:-8 -2:-7 -2:-6 -2:-5 -2:-4 -2:-3 -2:-2 -2:-1 -2:0 -2:1 -2:2 -2:3 -2:4 -2:5 -2:6 -2:7 -2:8 -2:9 -2:10 -2:11 -2:12 -1:-12 -1:-11 -1:-10 -1:-9 -1:-8 -1:-7 -1:-6 -1:-5 -1:-4 -1:-3 -1:-2 -1:-1 -1:0 -1:1 -1:2 -1:3 -1:4 -1:5 -1:6 -1:7 -1:8 -1:9 -1:10 -1:11 -1:12 0:-12 0:-11 0:-10 0:-9 0:-8 0:-7 0:-6 0:-5 0:-4 0:-3 0:-2 0:-1 0:0 0:1 0:2 0:3 0:4 0:5 0:6 0:7 0:8 0:9 0:10 0:11 0:12 1:-12 1:-11 1:-10 1:-9 1:-8 1:-7 1:-6 1:-5 1:-4 1:-3 1:-2 1:-1 1:0 1:1 1:2 1:3 1:4 1:5 1:6 1:7 1:8 1:9 1:10 1:11 1:12 2:-12 2:-11 2:-10 2:-9 2:-8 2:-7 2:-6 2:-5 2:-4 2:-3 2:-2 2:-1 2:0 2:1 2:2 2:3 2:4 2:5 2:6 2:7 2:8 2:9 2:10 2:11 2:12 3:-12 3:-11 3:-10 3:-9 3:-8 3:-7 3:-6 3:-5 3:-4 3:-3 3:-2 3:-1 3:0 3:1 3:2 3:3 3:4 3:5 3:6 3:7 3:8 3:9 3:10 3:11 3:12 4:-12 4:-11 4:-10 4:-9 4:-8 4:-7 4:-6 4:-5 4:-4 4:-3 4:-2 4:-1 4:0 4:1 4:2 4:3 4:4 4:5 4:6 4:7 4:8 4:9 4:10 4:11 4:12 5:-12 5:-11 5:-10 5:-9 5:-8 5:-7 5:-6 5:-5 5:-4 5:-3 5:-2 5:-1 5:0 5:1 5:2 5:3 5:4 5:5 5:6 5:7 5:8 5:9 5:10 5:11 5:12 6:-12 6:-11 6:-10 6:-9 6:-8 6:-7 6:-6 6:-5 6:-4 6:-3 6:-2 6:-1 6:0 6:1 6:2 6:3 6:4 6:5 6:6 6:7 6:8 6:9 6:10 6:11 6:12 7:-12 7:-11 7:-10 7:-9 7:-8 7:-7 7:-6 7:-5 7:-4 7:-3 7:-2 7:-1 7:0 7:1 7:2 7:3 7:4 7:5 7:6 7:7 7:8 7:9 7:10 7:11 7:12 8:-12 8:-11 8:-10 8:-9 8:-8 8:-7 8:-6 8:-5 8:-4 8:-3 8:-2 8:-1 8:0 8:1 8:2 8:3 8:4 8:5 8:6 8:7 8:8 8:9 8:10 8:11 8:12 9:-12 9:-11 9:-10 9:-9 9:-8 9:-7 9:-6 9:-5 9:-4 9:-3 9:-2 9:-1 9:0 9:1 9:2 9:3 9:4 9:5 9:6 9:7 9:8 9:9 9:10 9:11 9:12 10:-12 10:-11 10:-10 10:-9 10:-8 10:-7 10:-6 10:-5 10:-4 10:-3 10:-2 10:-1 10:0 10:1 10:2 10:3 10:4 10:5 10:6 10:7 10:8 10:9 10:10 10:11 10:12 11:-12 11:-11 11:-10 11:-9 11:-8 11:-7 11:-6 11:-5 11:-4 11:-3 11:-2 11:-1 11:0 11:1 11:2 11:3 11:4 11:5 11:6 11:7 11:8 11:9 11:10 11:11 11:12 12:-12 12:-11 12:-10 12:-9 12:-8 12:-7 12:-6 12:-5 12:-4 12:-3 12:-2 12:-1 12:0 12:1 12:2 12:3 12:4 12:5 12:6 12:7 12:8 12:9 12:10 12:11 12:12", "0:-1,0:0,0:-2,0:1,0:-3,0:2,0:-4,0:3,0:-5,0:4,0:-6,0:5,0:-7,0:6,0:-8,0:7,0:-9,0:8,0:-10,0:9,0:-11,0:10,0:-12,0:11,0:12,-1:12,-1:11,1:-12,-1:10,1:-11,-1:9,1:-10,-1:8,1:-9,-1:7,1:-8,-1:6,1:-7,-1:5,1:-6,-1:4,1:-5,-1:3,1:-4,-1:2,1:-3,-1:1,1:-2,-1:0,1:-1,-1:-1,1:0,-1:-2,1:1,-1:-3,1:2,-1:-4,1:3,-1:-5,1:4,-1:-6,1:5,-1:-7,1:6,-1:-8,1:7,-1:-9,1:8,-1:-10,1:9,-1:-11,1:10,-1:-12,1:11,1:12,-2:12,-2:11,2:-12,-2:10,2:-11,-2:9,2:-10,-2:8,2:-9,-2:7,2:-8,-2:6,2:-7,-2:5,2:-6,-2:4,2:-5,-2:3,2:-4,-2:2,2:-3,-2:1,2:-2,-2:0,2:-1,-2:-1,2:0,-2:-2,2:1,-2:-3,2:2,-2:-4,2:3,-2:-5,2:4,-2:-6,2:5,-2:-7,2:6,-2:-8,2:7,-2:-9,2:8,-2:-10,2:9,-2:-11,2:10,-2:-12,2:11,2:12,-3:12,-3:11,3:-12,-3:10,3:-11,-3:9,3:-10,-3:8,3:-9,-3:7,3:-8,-3:6,3:-7,-3:5,3:-6,-3:4,3:-5,-3:3,3:-4,-3:2,3:-3,-3:1,3:-2,-3:0,3:-1,-3:-1,3:0,-3:-2,3:1,-3:-3,3:2,-3:-4,3:3,-3:-5,3:4,-3:-6,3:5,-3:-7,3:6,-3:-8,3:7,-3:-9,3:8,-3:-10,3:9,-3:-11,3:10,-3:-12,3:11,3:12,-4:12,-4:11,4:-12,-4:10,4:-11,-4:9,4:-10,-4:8,4:-9,-4:7,4:-8,-4:6,4:-7,-4:5,4:-6,-4:4,4:-5,-4:3,4:-4,-4:2,4:-3,-4:1,4:-2,-4:0,4:-1,-4:-1,4:0,-4:-2,4:1,-4:-3,4:2,-4:-4,4:3,-4:-5,4:4,-4:-6,4:5,-4:-7,4:6,-4:-8,4:7,-4:-9,4:8,-4:-10,4:9,-4:-11,4:10,-4:-12,4:11,4:12,-5:12,-5:11,5:-12,-5:10,5:-11,-5:9,5:-10,-5:8,5:-9,-5:7,5:-8,-5:6,5:-7,-5:5,5:-6,-5:4,5:-5,-5:3,5:-4,-5:2,5:-3,-5:1,5:-2,-5:0,5:-1,-5:-1,5:0,-5:-2,5:1,-5:-3,5:2,-5:-4,5:3,-5:-5,5:4,-5:-6,5:5,-5:-7,5:6,-5:-8,5:7,-5:-9,5:8,-5:-10,5:9,-5:-11,5:10,-5:-12,5:11,5:12,-6:12,-6:11,6:-12,-6:10,6:-11,-6:9,6:-10,-6:8,6:-9,-6:7,6:-8,-6:6,6:-7,-6:5,6:-6,-6:4,6:-5,-6:3,6:-4,-6:2,6:-3,-6:1,6:-2,-6:0,6:-1,-6:-1,6:0,-6:-2,6:1,-6:-3,6:2,-6:-4,6:3,-6:-5,6:4,-6:-6,6:5,-6:-7,6:6,-6:-8,6:7,-6:-9,6:8,-6:-10,6:9,-6:-11,6:10,-6:-12,6:11,6:12,-7:12,-7:11,7:-12,-7:10,7:-11,-7:9,7:-10,-7:8,7:-9,-7:7,7:-8,-7:6,7:-7,-7:5,7:-6,-7:4,7:-5,-7:3,7:-4,-7:2,7:-3,-7:1,7:-2,-7:0,7:-1,-7:-1,7:0,-7:-2,7:1,-7:-3,7:2,-7:-4,7:3,-7:-5,7:4,-7:-6,7:5,-7:-7,7:6,-7:-8,7:7,-7:-9,7:8,-7:-10,7:9,-7:-11,7:10,-7:-12,7:11,7:12,-8:12,-8:11,8:-12,-8:10,8:-11,-8:9,8:-10,-8:8,8:-9,-8:7,8:-8,-8:6,8:-7,-8:5,8:-6,-8:4,8:-5,-8:3,8:-4,-8:2,8:-3,-8:1,8:-2,-8:0,8:-1,-8:-1,8:0,-8:-2,8:1,-8:-3,8:2,-8:-4,8:3,-8:-5,8:4,-8:-6,8:5,-8:-7,8:6,-8:-8,8:7,-8:-9,8:8,-8:-10,8:9,-8:-11,8:10,-8:-12,8:11,8:12,-9:12,-9:11,9:-12,-9:10,9:-11,-9:9,9:-10,-9:8,9:-9,-9:7,9:-8,-9:6,9:-7,-9:5,9:-6,-9:4,9:-5,-9:3,9:-4,-9:2,9:-3,-9:1,9:-2,-9:0,9:-1,-9:-1,9:0,-9:-2,9:1,-9:-3,9:2,-9:-4,9:3,-9:-5,9:4,-9:-6,9:5,-9:-7,9:6,-9:-8,9:7,-9:-9,9:8,-9:-10,9:9,-9:-11,9:10,-9:-12,9:11,9:12,-10:12,-10:11,10:-12,-10:10,10:-11,-10:9,10:-10,-10:8,10:-9,-10:7,10:-8,-10:6,10:-7,-10:5,10:-6,-10:4,10:-5,-10:3,10:-4,-10:2,10:-3,-10:1,10:-2,-10:0,10:-1,-10:-1,10:0,-10:-2,10:1,-10:-3,10:2,-10:-4,10:3,-10:-5,10:4,-10:-6,10:5,-10:-7,10:6,-10:-8,10:7,-10:-9,10:8,-10:-10,10:9,-10:-11,10:10,-10:-12,10:11,10:12,-11:12,-11:11,11:-12,-11:10,11:-11,-11:9,11:-10,-11:8,11:-9,-11:7,11:-8,-11:6,11:-7,-11:5,11:-6,-11:4,11:-5,-11:3,11:-4,-11:2,11:-3,-11:1,11:-2,-11:0,11:-1,-11:-1,11:0,-11:-2,11:1,-11:-3,11:2,-11:-4,11:3,-11:-5,11:4,-11:-6,11:5,-11:-7,11:6,-11:-8,11:7,-11:-9,11:8,-11:-10,11:9,-11:-11,11:10,-11:-12,11:11,11:12,-12:12,-12:11,12:-12,-12:10,12:-11,-12:9,12:-10,-12:8,12:-9,-12:7,12:-8,-12:6,12:-7,-12:5,12:-6,-12:4,12:-5,-12:3,12:-4,-12:2,12:-3,-12:1,12:-2,-12:0,12:-1,-12:-1,12:0,-12:-2,12:1,-12:-3,12:2,-12:-4,12:3,-12:-5,12:4,-12:-6,12:5,-12:-7,12:6,-12:-8,12:7,-12:-9,12:8,-12:-10,12:9,-12:-11,12:10,-12:-12,12:11,12:12")]
    [InlineData("map put replace remove", "type string; map; put Rome=1 Athens=2 Sparta=3 Thebes=4; put Athens=20; rm Rome; put Rome=10 Corinth=5", "Sparta=3,Rome=10,Athens=20,Corinth=5,Thebes=4")]
    [InlineData("map 40", "type string; map; put Rome=0 Athens=1 Sparta=2 Thebes=3 Corinth=4 Babylon=5 Memphis=6 Thebes2=7 Carthage=8 Persepolis=9 Antioch=10 Alexandria=11 Delphi=12 Olympia=13 Troy=14 Ephesus=15 Byzantium=16 Syracuse=17 Marathon=18 Argos=19 Miletus=20 Rhodes=21 Knossos=22 Halicarnassus=23 Pergamon=24 Tyre=25 Sidon=26 Nineveh=27 Ur=28 Uruk=29 Susa=30 Ecbatana=31 Sardis=32 Gordion=33 Hattusa=34 Kadesh=35 Megiddo=36 Jericho=37 Petra=38 Palmyra=39", "Pergamon=24,Rhodes=21,Jericho=37,Byzantium=16,Palmyra=39,Kadesh=35,Sidon=26,Carthage=8,Rome=0,Argos=19,Persepolis=9,Hattusa=34,Nineveh=27,Syracuse=17,Memphis=6,Alexandria=11,Corinth=4,Ephesus=15,Thebes=3,Ecbatana=31,Megiddo=36,Sparta=2,Olympia=13,Sardis=32,Knossos=22,Athens=1,Babylon=5,Antioch=10,Delphi=12,Troy=14,Tyre=25,Thebes2=7,Miletus=20,Susa=30,Gordion=33,Petra=38,Marathon=18,Halicarnassus=23,Uruk=29,Ur=28")]
    [InlineData("mapCopy 40", "type string; mapCopy Rome=0 Athens=1 Sparta=2 Thebes=3 Corinth=4 Babylon=5 Memphis=6 Thebes2=7 Carthage=8 Persepolis=9 Antioch=10 Alexandria=11 Delphi=12 Olympia=13 Troy=14 Ephesus=15 Byzantium=16 Syracuse=17 Marathon=18 Argos=19 Miletus=20 Rhodes=21 Knossos=22 Halicarnassus=23 Pergamon=24 Tyre=25 Sidon=26 Nineveh=27 Ur=28 Uruk=29 Susa=30 Ecbatana=31 Sardis=32 Gordion=33 Hattusa=34 Kadesh=35 Megiddo=36 Jericho=37 Petra=38 Palmyra=39", "Pergamon=24,Rhodes=21,Jericho=37,Byzantium=16,Palmyra=39,Kadesh=35,Sidon=26,Carthage=8,Rome=0,Argos=19,Persepolis=9,Hattusa=34,Nineveh=27,Syracuse=17,Memphis=6,Alexandria=11,Corinth=4,Ephesus=15,Thebes=3,Ecbatana=31,Megiddo=36,Sparta=2,Olympia=13,Sardis=32,Knossos=22,Athens=1,Babylon=5,Antioch=10,Delphi=12,Troy=14,Tyre=25,Thebes2=7,Miletus=20,Susa=30,Gordion=33,Petra=38,Marathon=18,Halicarnassus=23,Uruk=29,Ur=28")]
    [InlineData("mapCopy 3", "type string; mapCopy Rome=1 Athens=2 Sparta=3", "Sparta=3,Rome=1,Athens=2")]
    [InlineData("hashMapOf 20", "type string; hashMapOf Rome=0 Athens=1 Sparta=2 Thebes=3 Corinth=4 Babylon=5 Memphis=6 Thebes2=7 Carthage=8 Persepolis=9 Antioch=10 Alexandria=11 Delphi=12 Olympia=13 Troy=14 Ephesus=15 Byzantium=16 Syracuse=17 Marathon=18 Argos=19", "Sparta=2,Olympia=13,Byzantium=16,Athens=1,Babylon=5,Antioch=10,Delphi=12,Troy=14,Carthage=8,Rome=0,Thebes2=7,Argos=19,Persepolis=9,Syracuse=17,Memphis=6,Marathon=18,Alexandria=11,Corinth=4,Ephesus=15,Thebes=3")]
    [InlineData("putAll into existing", "type string; map; put Rome=1; putAll Rome=0 Athens=1 Sparta=2 Thebes=3 Corinth=4 Babylon=5 Memphis=6 Thebes2=7 Carthage=8 Persepolis=9 Antioch=10 Alexandria=11 Delphi=12 Olympia=13 Troy=14 Ephesus=15 Byzantium=16 Syracuse=17 Marathon=18 Argos=19 Miletus=20 Rhodes=21 Knossos=22 Halicarnassus=23 Pergamon=24 Tyre=25 Sidon=26 Nineveh=27 Ur=28 Uruk=29 Susa=30 Ecbatana=31 Sardis=32 Gordion=33 Hattusa=34 Kadesh=35 Megiddo=36 Jericho=37 Petra=38 Palmyra=39", "Pergamon=24,Rhodes=21,Jericho=37,Byzantium=16,Palmyra=39,Kadesh=35,Sidon=26,Carthage=8,Rome=0,Argos=19,Persepolis=9,Hattusa=34,Nineveh=27,Syracuse=17,Memphis=6,Alexandria=11,Corinth=4,Ephesus=15,Thebes=3,Ecbatana=31,Megiddo=36,Sparta=2,Olympia=13,Sardis=32,Knossos=22,Athens=1,Babylon=5,Antioch=10,Delphi=12,Troy=14,Tyre=25,Thebes2=7,Miletus=20,Susa=30,Gordion=33,Petra=38,Marathon=18,Halicarnassus=23,Uruk=29,Ur=28")]
    [InlineData("long keys", "type long; set; add 0 1 -1 4294967296 -4294967296 9223372036854775807 -9223372036854775808 81985529216486895 255 256 65535 65536", "0,-1,-4294967296,9223372036854775807,-9223372036854775808,81985529216486895,256,1,4294967296,65536,255,65535")]
    [InlineData("collisions strings", "type string; set; add Aa BB AaAa BBBB AaBB BBAa AaAaAa BBBBBB Ab Bb", "Aa,BB,AaAaAa,BBBBBB,Bb,Ab,AaAa,BBBB,AaBB,BBAa")]
    public void IterationOrderMatchesJvm(string name, string spec, string expected)
    {
        Assert.Equal(expected, Run(spec));
        Assert.NotEmpty(name);
    }

    [Fact]
    public void TableGrowsLikeTheJvm()
    {
        // JVM: default table 16 grows at the 13th, 25th, 49th, 97th insertion; HashMap(0)/HashMap(1) start at 2
        var map = new JavaHashMap<int, int>();
        Assert.Equal(0, map.Capacity);
        var seen = new Dictionary<int, int>();
        for (int i = 1; i <= 100; i++)
        {
            map.Put(i, i);
            seen[i] = map.Capacity;
        }
        Assert.Equal([16, 16, 32, 32, 64, 64, 128, 128, 256], new[] { seen[1], seen[12], seen[13], seen[24], seen[25], seen[48], seen[49], seen[96], seen[97] });

        int[] capacities = [0, 1, 2, 3, 5, 12, 100];
        int[][] expected =
        [
            [2, 4, 4, 8, 8, 16, 32, 64], [2, 4, 4, 8, 8, 16, 32, 64], [2, 4, 4, 8, 8, 16, 32, 64], [4, 4, 4, 8, 8, 16, 32, 64],
            [8, 8, 8, 8, 8, 16, 32, 64], [16, 16, 16, 16, 16, 16, 32, 64], [128, 128, 128, 128, 128, 128, 128, 128]
        ];
        for (int c = 0; c < capacities.Length; c++)
        {
            var sized = new JavaHashMap<int, int>(capacities[c]);
            Assert.Equal(0, sized.Capacity);
            int[] checkpoints = [1, 2, 3, 4, 6, 7, 13, 25];
            var actual = new List<int>();
            for (int i = 1; i <= 30; i++)
            {
                sized.Put(i, i);
                if (checkpoints.Contains(i)) actual.Add(sized.Capacity);
            }
            Assert.Equal(expected[c], actual);
        }
    }

    [Fact]
    public void ClearKeepsTheTable()
    {
        var map = new JavaHashMap<int, int>();
        for (int i = 0; i < 30; i++) map.Put(i, i);
        Assert.Equal(64, map.Capacity);
        map.Clear();
        Assert.Equal(64, map.Capacity);
        Assert.Equal(0, map.Count);
        Assert.Empty(map);
    }

    [Fact]
    public void CollidingBinsResizeBelow64ThenRefuseToTreeify()
    {
        // JVM (constant hashCode): table 16 until the 8th key, 32 at the 9th (treeifyBin resizes small tables), 64 at the 10th
        var map = new JavaHashMap<int, int>(_ => 0);
        var capacities = new List<int>();
        for (int i = 1; i <= 10; i++)
        {
            map.Put(i, i);
            capacities.Add(map.Capacity);
        }
        Assert.Equal([16, 16, 16, 16, 16, 16, 16, 16, 32, 64], capacities);
        // from here the JVM would build a tree bin (11th key); that order is not reproduced
        Assert.Throws<NotSupportedException>(() => map.Put(11, 11));

        var large = new JavaHashMap<int, int>(64, _ => 0);
        for (int i = 1; i <= 8; i++) large.Put(i, i);
        Assert.Throws<NotSupportedException>(() => large.Put(9, 9));
    }

    [Fact]
    public void BasicMapSemantics()
    {
        var map = new JavaHashMap<string, int>();
        Assert.True(map.Put("a", 1));
        Assert.False(map.Put("a", 2)); // replace keeps position, reports not new
        Assert.Equal(2, map["a"]);
        Assert.True(map.ContainsKey("a"));
        Assert.False(map.ContainsKey("b"));
        Assert.False(map.TryGetValue("b", out _));
        Assert.Throws<KeyNotFoundException>(() => map["b"]);
        Assert.True(map.Remove("a"));
        Assert.False(map.Remove("a"));
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void ModifyingDuringEnumerationThrows()
    {
        var set = new JavaHashSet<int>();
        set.Add(1);
        set.Add(2);
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (int item in set) set.Add(item + 100);
        });
    }

    [Fact]
    public void KeyTypesWithoutDeterministicJvmHashCodeAreRejected()
    {
        Assert.Throws<NotSupportedException>(() => new JavaHashSet<Stat>());
        Assert.Throws<NotSupportedException>(() => new JavaHashMap<object, int>());
        var custom = new JavaHashSet<Stat>(stat => (int)stat); // an explicit hash function is the escape hatch
        Assert.True(custom.Add(Stat.Gold));
    }

    // --- tiny script interpreter, same grammar as the JVM side ---

    private static string Run(string spec)
    {
        string type = spec.Split(';')[0].Trim().Split(' ')[1];
        return type switch
        {
            "int" => Run(spec, int.Parse, key => key.ToString()),
            "long" => Run(spec, long.Parse, key => key.ToString()),
            "hex" => Run(spec, text => { var p = text.Split(':'); return HexCoord.Of(int.Parse(p[0]), int.Parse(p[1])); }, coord => $"{coord.x}:{coord.y}"),
            _ => Run(spec, text => text, key => key)
        };
    }

    private static string Run<T>(string spec, Func<string, T> parse, Func<T, string> show) where T : notnull
    {
        JavaHashSet<T>? set = null;
        JavaHashMap<T, int>? map = null;
        KeyValuePair<T, int> Pair(string text)
        {
            var kv = text.Split('=');
            return new KeyValuePair<T, int>(parse(kv[0]), int.Parse(kv[1]));
        }

        foreach (string op in spec.Split(';'))
        {
            string[] tokens = op.Trim().Split(' ');
            string[] args = tokens[1..];
            switch (tokens[0])
            {
                case "type": break;
                case "set": set = args.Length > 0 && args[0].StartsWith("cap=", StringComparison.Ordinal) ? new JavaHashSet<T>(int.Parse(args[0][4..])) : new JavaHashSet<T>(); break;
                case "map": map = new JavaHashMap<T, int>(); break;
                case "add": foreach (string x in args) set!.Add(parse(x)); break;
                case "rm": foreach (string x in args) { if (set != null) set.Remove(parse(x)); else map!.Remove(parse(x)); } break;
                case "put": foreach (string x in args) { var kv = Pair(x); map!.Put(kv.Key, kv.Value); } break;
                case "clear": set!.Clear(); break;
                case "hashSetOf": set = JavaHashSet<T>.HashSetOf(args.Select(parse).ToArray()); break;
                case "toHashSet": set = JavaHashSet<T>.ToHashSet(args.Select(parse).ToList()); break;
                case "setCopy": set = new JavaHashSet<T>(args.Select(parse).ToList()); break;
                case "mapCopy": map = new JavaHashMap<T, int>(args.Select(Pair).ToList()); break;
                case "hashMapOf": map = JavaHashMap<T, int>.HashMapOf(args.Select(Pair).ToArray()); break;
                case "putAll": map!.PutAll(args.Select(Pair).ToList()); break;
                default: throw new InvalidOperationException(tokens[0]);
            }
        }
        return set != null
            ? string.Join(',', set.Select(show))
            : string.Join(',', map!.Select(entry => show(entry.Key) + "=" + entry.Value));
    }
}
