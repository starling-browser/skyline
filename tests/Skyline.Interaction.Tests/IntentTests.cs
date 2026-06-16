namespace Skyline.Interaction.Tests;

[TestClass]
public class IntentTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void SourceStates_CarryTheirFields()
    {
        var generic = new GenericSourceState(InputModality.Pointer);
        Assert.AreEqual(InputModality.Pointer, generic.Modality);

        var voice = new VoiceSourceState("copy this", 0.9f);
        Assert.AreEqual("copy this", voice.Transcript);
        Assert.AreEqual(0.9f, voice.Confidence);

        var gaze = new GazeSourceState(120f, 80f, 0.7f);
        Assert.AreEqual(120f, gaze.X);
        Assert.AreEqual(80f, gaze.Y);
        Assert.AreEqual(0.7f, gaze.Confidence);
    }

    [TestMethod]
    public void SourceState_IsAClosedSetOfThreeCases()
    {
        Assert.AreEqual("generic", Kind(new GenericSourceState(InputModality.Keyboard)));
        Assert.AreEqual("voice", Kind(new VoiceSourceState("hi", 1f)));
        Assert.AreEqual("gaze", Kind(new GazeSourceState(0f, 0f, 1f)));
    }

    private static string Kind(SourceState state) => state switch
    {
        GenericSourceState => "generic",
        VoiceSourceState => "voice",
        GazeSourceState => "gaze",
        _ => "?",
    };

    [TestMethod]
    public void InputSnapshot_CapturesOneReading()
    {
        var snapshot = new InputSnapshot(Actors.Planner, InputModality.Voice, new VoiceSourceState("paste", 0.8f), At);
        Assert.AreSame(Actors.Planner, snapshot.Source);
        Assert.AreEqual(InputModality.Voice, snapshot.Modality);
        Assert.IsTrue(snapshot.State is VoiceSourceState);
        Assert.AreEqual(At, snapshot.At);
    }

    [TestMethod]
    public void InteractionIntent_BindsCommandActorTargetAndEvidence()
    {
        var command = new CommandId("edit", "paste");
        var target = new TargetRef("address-bar", new ObjectTarget("field"));
        var evidence = new InputSnapshot(Actors.Planner, InputModality.Ai, new GenericSourceState(InputModality.Ai), At);

        var bare = new InteractionIntent(command, Actors.Planner);
        Assert.AreEqual(command, bare.Command);
        Assert.AreSame(Actors.Planner, bare.Actor);
        Assert.IsNull(bare.Target);
        Assert.IsNull(bare.Evidence);

        var full = new InteractionIntent(command, Actors.Planner, target, evidence);
        Assert.AreSame(target, full.Target);
        Assert.AreSame(evidence, full.Evidence);
    }
}
