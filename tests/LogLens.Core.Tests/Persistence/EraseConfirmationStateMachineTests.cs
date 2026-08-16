using LogLens.Core.Persistence;

namespace LogLens.Core.Tests.Persistence;

[TestClass]
public sealed class EraseConfirmationStateMachineTests
{
    [TestMethod]
    public void Confirmation_RequiresFourDistinctStagesInOrder()
    {
        var confirmation = new EraseConfirmationStateMachine();

        Assert.AreEqual(EraseConfirmationStage.WhatWillBeErased, confirmation.Stage);
        Assert.IsTrue(confirmation.MoveNext());
        Assert.AreEqual(EraseConfirmationStage.WhatWillBeKept, confirmation.Stage);
        Assert.IsTrue(confirmation.MoveNext());
        Assert.AreEqual(EraseConfirmationStage.StorageBoundary, confirmation.Stage);
        Assert.IsTrue(confirmation.MoveNext());
        Assert.AreEqual(EraseConfirmationStage.FinalPhrase, confirmation.Stage);
        Assert.IsFalse(confirmation.MoveNext());
        Assert.IsFalse(confirmation.IsAuthorized);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void CancelAtAnyStage_NeverAuthorizesErase(int stage)
    {
        var confirmation = new EraseConfirmationStateMachine();
        for (int current = 1; current < stage; current++)
        {
            confirmation.MoveNext();
        }

        confirmation.Cancel();

        Assert.AreEqual(EraseConfirmationStage.Cancelled, confirmation.Stage);
        Assert.IsFalse(confirmation.CanErase);
        Assert.IsFalse(confirmation.IsAuthorized);
        Assert.IsFalse(confirmation.AuthorizeErase());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("erase loglens data")]
    [DataRow(" ERASE LOGLENS DATA")]
    [DataRow("ERASE LOGLENS DATA ")]
    [DataRow("ERASE  LOGLENS DATA")]
    [DataRow("ERASE LOGLENS")]
    public void FinalErase_WrongPhraseKeepsEraseDisabled(string phrase)
    {
        EraseConfirmationStateMachine confirmation = AtFinalStage();

        confirmation.SetPhrase(phrase);

        Assert.IsFalse(confirmation.CanErase);
        Assert.IsFalse(confirmation.AuthorizeErase());
    }

    [TestMethod]
    public void FinalErase_ExactPhraseAuthorizesOnce()
    {
        EraseConfirmationStateMachine confirmation = AtFinalStage();

        confirmation.SetPhrase(LocalSessionPolicy.FinalErasePhrase);

        Assert.IsTrue(confirmation.CanErase);
        Assert.IsTrue(confirmation.AuthorizeErase());
        Assert.IsTrue(confirmation.IsAuthorized);
        Assert.IsFalse(confirmation.AuthorizeErase());
    }

    [TestMethod]
    public void BackNavigation_PreservesOrderedSafetyFlow()
    {
        EraseConfirmationStateMachine confirmation = AtFinalStage();

        Assert.IsTrue(confirmation.MoveBack());
        Assert.AreEqual(EraseConfirmationStage.StorageBoundary, confirmation.Stage);
        Assert.IsTrue(confirmation.MoveBack());
        Assert.AreEqual(EraseConfirmationStage.WhatWillBeKept, confirmation.Stage);
        Assert.IsTrue(confirmation.MoveBack());
        Assert.AreEqual(EraseConfirmationStage.WhatWillBeErased, confirmation.Stage);
        Assert.IsFalse(confirmation.MoveBack());
    }

    private static EraseConfirmationStateMachine AtFinalStage()
    {
        var confirmation = new EraseConfirmationStateMachine();
        confirmation.MoveNext();
        confirmation.MoveNext();
        confirmation.MoveNext();
        return confirmation;
    }
}
