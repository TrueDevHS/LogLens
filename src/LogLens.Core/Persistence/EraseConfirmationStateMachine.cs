namespace LogLens.Core.Persistence;

public enum EraseConfirmationStage
{
    WhatWillBeErased = 1,
    WhatWillBeKept = 2,
    StorageBoundary = 3,
    FinalPhrase = 4,
    Authorized = 5,
    Cancelled = 6
}

public sealed class EraseConfirmationStateMachine
{
    public EraseConfirmationStage Stage { get; private set; } =
        EraseConfirmationStage.WhatWillBeErased;

    public string EnteredPhrase { get; private set; } = string.Empty;

    public bool CanErase => Stage == EraseConfirmationStage.FinalPhrase
        && string.Equals(
            EnteredPhrase,
            LocalSessionPolicy.FinalErasePhrase,
            StringComparison.Ordinal);

    public bool IsAuthorized => Stage == EraseConfirmationStage.Authorized;

    public bool MoveNext()
    {
        if (Stage is >= EraseConfirmationStage.WhatWillBeErased
            and < EraseConfirmationStage.FinalPhrase)
        {
            Stage++;
            return true;
        }

        return false;
    }

    public bool MoveBack()
    {
        if (Stage is > EraseConfirmationStage.WhatWillBeErased
            and <= EraseConfirmationStage.FinalPhrase)
        {
            Stage--;
            return true;
        }

        return false;
    }

    public void SetPhrase(string? phrase) => EnteredPhrase = phrase ?? string.Empty;

    public bool AuthorizeErase()
    {
        if (!CanErase)
        {
            return false;
        }

        Stage = EraseConfirmationStage.Authorized;
        return true;
    }

    public void Cancel()
    {
        if (!IsAuthorized)
        {
            Stage = EraseConfirmationStage.Cancelled;
        }
    }
}
