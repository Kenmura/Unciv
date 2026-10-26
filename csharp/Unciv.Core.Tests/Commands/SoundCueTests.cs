using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Commands;

// C057: the PlaySound unique becomes a participant-only SoundRequested event; the client plays SoundCues.For(events).
[Collection("RulesetCache")]
public sealed class SoundCueTests
{
    private sealed class AllSeen : IViewerVisibility
    {
        public bool CanSeeTile(Unciv.Logic.Map.HexCoord position) => true;

        public bool CanSeeUnit(string civId, int unitId, Unciv.Logic.Map.HexCoord position) => true;

        public bool KnowsCiv(string civId) => true;
    }

    private readonly TestGame game = new();
    private readonly Civilization human;
    private readonly Civilization otherHuman;

    public SoundCueTests()
    {
        game.MakeHexagonalMap(5);
        human = game.AddCiv(isPlayer: true);
        otherHuman = game.AddCiv(isPlayer: true);
        game.AddCity(human, game.TileMap[0, 0]);
    }

    private IReadOnlyList<GameEvent> Fire(Civilization civ, string uniqueText)
    {
        var sink = new GameInfoEventSink();
        game.GameInfo.EventSink = sink;
        try
        {
            UniqueTriggerActivation.TriggerUnique(new Unique(uniqueText), civ);
            return sink.Drain();
        }
        finally
        {
            game.GameInfo.EventSink = null;
        }
    }

    [Fact]
    public void PlaySoundTriggerEmitsOneCueForTheTriggeringCiv()
    {
        var events = Fire(human, "Play [coin] sound");
        Assert.Equal(new SoundRequested(human.CivId, "coin"), Assert.Single(events));
        Assert.Equal(["coin"], SoundCues.For(events, human.CivId));
    }

    [Fact]
    public void TheTriggerSucceedsHeadlessWithoutASinkAndWithoutDeviceState()
    {
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("Play [coin] sound"), human));
        Assert.True(UniqueTriggerActivation.TriggerUnique(new Unique("Choose a music track for [this civ], [Calm], [x]"), human));
        Assert.Empty(Fire(human, "Choose a music track for [this civ], [Calm], [x]")); // music none: no cue
    }

    [Fact]
    public void AnEmptyNameAnAiCivAndMissingAudioProduceNoCue()
    {
        Assert.Empty(Fire(human, "Play [] sound"));
        Assert.Empty(Fire(game.AddCiv(), "Play [coin] sound"));
        game.GameInfo.ClientState.Audio = null;
        Assert.Empty(Fire(human, "Play [coin] sound"));
    }

    [Fact]
    public void ACueReachesOnlyTheCivItWasFiredFor()
    {
        var events = Fire(human, "Play [coin] sound").OfType<EngineEvent>().Single();
        Assert.Same(events, Assert.Single(EventFilter.Classify(events, human.CivId, new AllSeen())));
        Assert.Empty(EventFilter.Classify(events, otherHuman.CivId, new AllSeen()));
    }

    [Fact]
    public void CuesKeepEventOrderAndDropOtherCivsAndEmptyNames()
    {
        GameEvent[] events =
        [
            new SoundRequested("A", "coin"), new TurnResolving(3), new SoundRequested("B", "fire"), new SoundRequested("A", ""), new SoundRequested("A", "click"),
        ];
        Assert.Equal(["coin", "click"], SoundCues.For(events, "A"));
        Assert.Equal(["fire"], SoundCues.For(events, "B"));
        Assert.Empty(SoundCues.For(events, "a"));
        Assert.Empty(SoundCues.For([], "A"));
    }

    [Fact]
    public void TheSameFiringGivesTheSameCuesEveryTime()
    {
        Assert.Equal(Fire(human, "Play [coin] sound"), Fire(human, "Play [coin] sound"));
    }

    [Fact]
    public void TheSoundAudioIsAGuardedNoOpWithoutASink()
    {
        var audio = SoundCueAudio.Instance;
        Assert.True(audio.HasSound("x"));
        Assert.False(audio.HasSound(""));
        audio.PlaySound(human, "x");
        audio.ChooseMusic("p", ["s"], []);
        Assert.Null(game.GameInfo.EventSink);
    }
}
