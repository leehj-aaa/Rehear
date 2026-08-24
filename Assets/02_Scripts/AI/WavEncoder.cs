using System;
using System.IO;
using UnityEngine;

public static class WavEncoder
{
    private const int HeaderSize = 44;

    public static byte[] FromAudioClip(AudioClip clip)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        float[] samples =
            new float[clip.samples * clip.channels];

        clip.GetData(samples, 0);

        byte[] pcmData =
            ConvertToPcm16(samples);

        using MemoryStream stream =
            new MemoryStream(HeaderSize + pcmData.Length);

        using BinaryWriter writer =
            new BinaryWriter(stream);

        WriteHeader(
            writer,
            clip.channels,
            clip.frequency,
            pcmData.Length
        );

        writer.Write(pcmData);
        writer.Flush();

        return stream.ToArray();
    }

    private static byte[] ConvertToPcm16(
        float[] samples)
    {
        byte[] pcmData =
            new byte[samples.Length * 2];

        int byteIndex = 0;

        foreach (float sample in samples)
        {
            float clamped =
                Mathf.Clamp(sample, -1f, 1f);

            short pcmSample =
                (short)Mathf.RoundToInt(
                    clamped * short.MaxValue
                );

            pcmData[byteIndex++] =
                (byte)(pcmSample & 0xff);

            pcmData[byteIndex++] =
                (byte)((pcmSample >> 8) & 0xff);
        }

        return pcmData;
    }

    private static void WriteHeader(
        BinaryWriter writer,
        int channels,
        int sampleRate,
        int pcmDataLength)
    {
        const short bitsPerSample = 16;

        int byteRate =
            sampleRate *
            channels *
            bitsPerSample / 8;

        short blockAlign =
            (short)(
                channels *
                bitsPerSample / 8
            );

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("RIFF")
        );

        writer.Write(
            36 + pcmDataLength
        );

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("WAVE")
        );

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("fmt ")
        );

        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes("data")
        );

        writer.Write(pcmDataLength);
    }
}