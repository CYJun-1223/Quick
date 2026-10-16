using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Quick.Tests;

[TestClass]
public sealed class InputHistoryTests
{
    [TestMethod]
    public void UpAndDownWalkBeforeAndAfterStates()
    {
        var history = new InputHistory();
        history.Commit("1+2");
        history.Commit("3");

        Assert.IsTrue(history.TryMove("3", -1, out var before));
        Assert.AreEqual("1+2", before);
        Assert.IsFalse(history.TryMove("1+2", -1, out _));
        Assert.IsTrue(history.TryMove("1+2", 1, out var after));
        Assert.AreEqual("3", after);
        Assert.IsFalse(history.TryMove("3", 1, out _));
    }

    [TestMethod]
    public void EditedTextIsKeptAsAState()
    {
        var history = new InputHistory();
        history.Commit("1+2");
        history.Commit("3");

        Assert.IsTrue(history.TryMove("3+4", -1, out var previous));
        Assert.AreEqual("3", previous);
        Assert.IsTrue(history.TryMove("3", 1, out var draft));
        Assert.AreEqual("3+4", draft);
    }

    [TestMethod]
    public void NothingToMoveToReturnsFalse()
    {
        var history = new InputHistory();
        Assert.IsFalse(history.TryMove("1+2", -1, out _));
        Assert.IsFalse(history.TryMove("1+2", 1, out _));
        Assert.IsFalse(history.TryMove("", -1, out _));
    }

    [TestMethod]
    public void UpFromEmptyInputRecallsLastState()
    {
        var history = new InputHistory();
        history.Commit("1+2");
        history.Commit("3");

        Assert.IsTrue(history.TryMove("", -1, out var recalled));
        Assert.AreEqual("3", recalled);
        Assert.IsFalse(history.TryMove("", 1, out _));
    }
}
