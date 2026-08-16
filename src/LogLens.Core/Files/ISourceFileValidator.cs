namespace LogLens.Core.Files;

public interface ISourceFileValidator
{
    ValidatedSourceFile Validate(string path);
}
