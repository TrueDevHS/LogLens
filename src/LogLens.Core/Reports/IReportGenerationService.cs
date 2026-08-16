namespace LogLens.Core.Reports;

public interface IReportGenerationService
{
    ReportDocument Generate(
        ReportGenerationRequest request,
        ReportFormat format,
        CancellationToken cancellationToken = default);
}
