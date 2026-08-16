namespace LogLens.Core.Reports;

public interface IReportDestinationWriter
{
    Task<ReportWriteResult> WriteAsync(
        ReportWriteRequest request,
        CancellationToken cancellationToken = default);
}
