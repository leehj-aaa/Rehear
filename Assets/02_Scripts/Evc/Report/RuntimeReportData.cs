using Rehear.Evc.Contracts;

public static class RuntimeReportData
{
    public static ReportFeedback Report { get; private set; }

    public static bool IsLoaded => Report != null;

    public static void Set(ReportFeedback report)
    {
        Report = report;
    }

    public static void Clear()
    {
        Report = null;
    }
}