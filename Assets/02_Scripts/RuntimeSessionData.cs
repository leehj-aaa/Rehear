public static class RuntimeSessionData
{
    public static string Pin { get; private set; }

    public static string PresentationTitle { get; private set; }
    public static string PresentationPurpose { get; private set; }
    public static int DurationMinutes { get; private set; }
    public static string EnvironmentType { get; private set; }
    public static int QaCount { get; private set; }
    public static string UsedLanguage { get; private set; }

    public static string PresentationScript { get; private set; }
    public static string[] SlideImageUrls { get; private set; }

    public static string AudienceExpertise { get; private set; }
    public static string AudienceInterest { get; private set; }
    public static int AudienceScale { get; private set; }
    public static string AudienceType { get; private set; }

    public static bool IsLoaded =>
        !string.IsNullOrWhiteSpace(Pin);

    public static void Load(
        string pin,
        SessionData session)
    {
        Pin = pin;

        Page1 page1 = session?.page_1;
        Page2 page2 = session?.page_2;
        Page3 page3 = session?.page_3;

        PresentationTitle =
            page1?.presentation_title ?? "";

        PresentationPurpose =
            page1?.presentation_purpose ?? "";

        DurationMinutes =
            page1?.duration_minutes ?? 0;

        EnvironmentType =
            page1?.environment_type ?? "";

        QaCount =
            page1?.qa_count ?? 0;

        UsedLanguage =
            page1?.used_language ?? "ko";

        PresentationScript =
            page2?.presentation_script_content ?? "";

        SlideImageUrls =
            page2?.slide_image?.image_urls;

        AudienceExpertise =
            page3?.audience_expertise ?? "";

        AudienceInterest =
            page3?.audience_interest ?? "";

        AudienceScale =
            page3?.audience_scale ?? 0;

        AudienceType =
            page3?.audience_type ?? "";
    }

    public static void Clear()
    {
        Pin = "";

        PresentationTitle = "";
        PresentationPurpose = "";
        DurationMinutes = 0;
        EnvironmentType = "";
        QaCount = 0;
        UsedLanguage = "";

        PresentationScript = "";
        SlideImageUrls = null;

        AudienceExpertise = "";
        AudienceInterest = "";
        AudienceScale = 0;
        AudienceType = "";
    }
}