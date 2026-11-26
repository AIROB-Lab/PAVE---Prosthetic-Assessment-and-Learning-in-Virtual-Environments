using System;
using NUnit.Framework;
using Pave.Assessments;

public class AssessmentRulesTests
{
    [Test]
    public void BlockWithoutFingertipCrossingDoesNotScore()
    {
        var score = new BoxAndBlockScore();
        Assert.IsFalse(score.RegisterTransfer(0, 1, false));
        Assert.AreEqual(0, score.Score);
        Assert.IsTrue(score.RegisterTransfer(0, 1, true));
    }

    [Test]
    public void MultipleBlocksInOneTransportCountOnce()
    {
        var score = new BoxAndBlockScore();
        Assert.IsTrue(score.RegisterTransfer(0, 1, true));
        Assert.IsFalse(score.RegisterTransfer(1, 1, true));
        Assert.AreEqual(1, score.Score);
        Assert.IsTrue(score.RegisterTransfer(2, 2, true));
        Assert.AreEqual(2, score.Score);
    }

    [Test]
    public void BouncedOrRecrossedBlockCannotScoreAgain()
    {
        var score = new BoxAndBlockScore();
        score.RegisterTransfer(7, 1, true);
        Assert.IsFalse(score.RegisterTransfer(7, 2, true));
        Assert.AreEqual(1, score.Score);
    }

    [Test]
    public void NoTransportEpisodeCannotScore()
    {
        var score = new BoxAndBlockScore();
        Assert.IsFalse(score.RegisterTransfer(0, 0, true));
        Assert.IsFalse(score.RegisterTransfer(0, -1, true));
    }

    [Test]
    public void TargetedTaskRetainsFailedTransportsAndCompletesAfterNineReleases()
    {
        var score = new TargetedBlockScore();
        for (int index = 0; index < 9; index++)
        {
            Assert.IsTrue(score.RegisterRelease(index, index != 3));
        }

        Assert.IsTrue(score.Complete);
        Assert.AreEqual(1, score.UnsuccessfulTransports);
        Assert.AreEqual(9, score.ReleasedCount);
        Assert.IsFalse(score.RegisterRelease(9, true));
    }

    [Test]
    public void TargetedTaskRejectsWrongOrderAndDuplicateReleases()
    {
        var score = new TargetedBlockScore();
        Assert.IsFalse(score.RegisterRelease(1, true));
        Assert.AreEqual(0, score.ReleasedCount);
        Assert.IsTrue(score.RegisterRelease(0, true));
        Assert.IsFalse(score.RegisterRelease(0, true));
        Assert.AreEqual(1, score.ReleasedCount);
    }

    [Test]
    public void SamplesAtAndAfterSixtySecondDeadlineAreExcluded()
    {
        var clock = new AssessmentClock(60);
        Assert.IsFalse(clock.Accepts(10));
        clock.Start(10);
        Assert.IsFalse(clock.Accepts(9));
        Assert.IsTrue(clock.Accepts(69.999));
        Assert.IsFalse(clock.Accepts(70));
        Assert.IsFalse(clock.Accepts(71));
        clock.Finish(TrialState.Completed, 71);
        Assert.AreEqual(60, clock.Elapsed(80));
    }

    [Test]
    public void AbortIsTerminalAndKeepsItsOriginalTime()
    {
        var clock = new AssessmentClock(60);
        clock.Start(100);
        clock.Finish(TrialState.Aborted, 105);
        clock.Finish(TrialState.Completed, 110);
        Assert.AreEqual(TrialState.Aborted, clock.State);
        Assert.AreEqual(5, clock.Elapsed(120));
        Assert.IsFalse(clock.Accepts(106));
        Assert.Throws<InvalidOperationException>(() => clock.Start(120));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidTrialDurationIsRejected(double duration) => Assert.Throws<ArgumentOutOfRangeException>(() => new AssessmentClock(duration));
    [Test]
    public void BriefContactGapDoesNotBecomeRelease()
    {
        var release = new ReleaseObservation();
        Assert.IsFalse(release.IsReleased(false, 10, 0.1));
        release.Observe(true, 10);
        Assert.IsFalse(release.IsReleased(false, 10.05, 0.1));
        release.Observe(true, 10.06);
        Assert.IsFalse(release.IsReleased(false, 10.11, 0.1));
        Assert.IsTrue(release.IsReleased(false, 10.2, 0.1));
        Assert.AreEqual(10.06, release.LastContactAt);
        Assert.IsFalse(release.IsReleased(true, 10.2, 0.1));
    }
}
