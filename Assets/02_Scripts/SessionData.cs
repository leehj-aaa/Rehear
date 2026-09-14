using System;

[Serializable]
public class SessionData
{
    public string created_at;
    public string status;

    public Page1 page_1;
    public Page2 page_2;
    public Page3 page_3;
}

[Serializable]
public class Page1
{
    public int duration_minutes;
    public string environment_type;
    public string presentation_purpose;
    public string presentation_title;
    public int qa_count;
    // Optional web field in minutes. Zero means the web has not supplied it.
    public int qa_duration_minutes;
    public string used_language;
}

[Serializable]
public class Page2
{
    public string paper_pdf_path;
    public string presentation_script_content;
    public SlideImage slide_image;
    public string slide_pdf_path;
}

[Serializable]
public class Page3
{
    public string audience_expertise;
    public string audience_interest;
    public int audience_scale;
    public string audience_type;
}

[Serializable]
public class SlideImage
{
    public int image_len;
    public string[] image_urls;
}
