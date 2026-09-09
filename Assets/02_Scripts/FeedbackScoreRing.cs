using UnityEngine;
using UnityEngine.UI;

// Figma 144px ring: 11.52px stroke with round ends; the value remains data driven.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FeedbackScoreRing : MaskableGraphic
{
    [SerializeField, Range(0, 1)] private float value;
    public float Value { get => value; set { this.value = Mathf.Clamp01(value); GetComponent<CurvedUI.CurvedUIVertexEffect>()?.SetDirty(); SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = GetPixelAdjustedRect();
        float radius = Mathf.Min(r.width,r.height)*.46f, half = Mathf.Min(r.width,r.height)*.04f;
        void Arc(float fraction, Color tint)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(128*fraction));
            for(int i=0;i<count;i++)
            {
                float a=Mathf.PI/2-2*Mathf.PI*fraction*i/count,b=Mathf.PI/2-2*Mathf.PI*fraction*(i+1)/count;
                int k=vh.currentVertCount;
                foreach(var p in new[]{new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(radius-half),new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(radius+half),new Vector2(Mathf.Cos(b),Mathf.Sin(b))*(radius+half),new Vector2(Mathf.Cos(b),Mathf.Sin(b))*(radius-half)})
                    vh.AddVert(r.center+p,tint,Vector2.zero);
                vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k+2,k+3,k);
            }
        }
        var track=color;track.a*=.14f;Arc(1,track);
        if(value<=0)return;
        Arc(value,color);
        foreach(float a in new[]{Mathf.PI/2,Mathf.PI/2-2*Mathf.PI*value})
        {
            var c=r.center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
            for(int j=0;j<16;j++)
            {
                int k=vh.currentVertCount;
                vh.AddVert(c,color,Vector2.zero);
                foreach(float t in new[]{j*2*Mathf.PI/16,(j+1)*2*Mathf.PI/16})
                    vh.AddVert(c+new Vector2(Mathf.Cos(t),Mathf.Sin(t))*half,color,Vector2.zero);
                vh.AddVert(c,color,Vector2.zero);
                vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k+2,k+3,k);
            }
        }
    }
}
