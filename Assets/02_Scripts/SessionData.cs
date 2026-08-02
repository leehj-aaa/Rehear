[System.Serializable]
public class SessionData {
    public Page1 page_1;
    public Page2 page_2;
    public Page3 page_3;
}

[System.Serializable]
public class Page1 {
    public int duration_minutes;
    public string environment_type;
    public int qa_count;
}

[System.Serializable]
public class Page2 {
    public string presentation_script_content;
    public SlideImage slide_image;
}

[System.Serializable]
public class Page3 {
    public string audience_expertise;
    public string audience_interest;
    public int audience_scale;
}

[System.Serializable]
public class SlideImage {
    public string[] image_urls; // 여기에 JPG 경로들이 들어옵니다
}