using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearPhotoAudioPreview
{
    private static AudioClip quietClip;
    private static float preparedVolume;
    public static bool Suppress;
    static RehearPhotoAudioPreview(){AudienceAnimationPlayer.PreviewPhotoShutter+=Play;}
    public static void Prepare(float volume=.10f)
    {
        if(quietClip && Mathf.Approximately(volume,preparedVolume))return;
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/08_Audio/ioscameraflash.mp3");
        if(!clip)return;
        clip.LoadAudioData();var samples=new float[clip.samples*clip.channels];
        if(!clip.GetData(samples,0))return;
        const string path="Assets/Editor/PhotoShutterPreview.wav";
        using(var writer=new BinaryWriter(File.Create(path))){
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples.Length*2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)clip.channels);
            writer.Write(clip.frequency);writer.Write(clip.frequency*clip.channels*2);writer.Write((short)(clip.channels*2));writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples.Length*2);
            foreach(var value in samples)writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(value*volume,-1,1)*32767));
        }
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        quietClip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);quietClip.LoadAudioData();preparedVolume=volume;
    }
    private static void Play(AudienceAnimationPlayer owner,AudioClip clip,float volume)
    {
        File.AppendAllText("Temp/RehearPhotoShutter.txt",$"{owner.name}: shutter once at held pose, volume={volume:F2}, source={clip.name}\n");
        if(Suppress){
            typeof(RehearAudienceMotionPreview).GetMethod("CapturePhotoCamera",BindingFlags.NonPublic|BindingFlags.Static)?.Invoke(null,new object[]{owner,null,false});
            File.Copy("Temp/PhotoAim-scene.png","Temp/PhotoShot-"+owner.name+".png",true);
            return;
        }
        Prepare(volume);
        if(!quietClip)return;
        typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil")?.GetMethod("PlayPreviewClip",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(AudioClip),typeof(int),typeof(bool)},null)?.Invoke(null,new object[]{quietClip,0,false});
    }
}
