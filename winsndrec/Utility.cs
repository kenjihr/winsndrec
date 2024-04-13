using NAudio.Wave;
using NAudio.MediaFoundation;

namespace winsndrec
{
    public class Utility
    {
        public static float[]? readSamples(WaveFormat waveFormat, byte[] samples)
        {
            var wfe = waveFormat as WaveFormatExtensible;
            WaveFormatEncoding? encoding = null;
            if (waveFormat.Encoding == WaveFormatEncoding.Pcm && (waveFormat.BitsPerSample == 16 || waveFormat.BitsPerSample == 24 || waveFormat.BitsPerSample == 32))
            {
                encoding = WaveFormatEncoding.Pcm;
            }
            else if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
            {
                encoding = WaveFormatEncoding.IeeeFloat;
            }
            else if (waveFormat.Encoding == WaveFormatEncoding.Extensible && wfe != null)
            {
                if (wfe.SubFormat == AudioSubtypes.MFAudioFormat_PCM && (waveFormat.BitsPerSample == 16 || waveFormat.BitsPerSample == 24 || waveFormat.BitsPerSample == 32))
                {
                    encoding = WaveFormatEncoding.Pcm;
                }
                else if (wfe.SubFormat == AudioSubtypes.MFAudioFormat_Float && waveFormat.BitsPerSample == 32)
                {
                    encoding = WaveFormatEncoding.IeeeFloat;
                }
            }

            if (encoding == null)
                return null;

            var bytesPerSample = waveFormat.BitsPerSample / 8;
            var frameCount = samples.Length / waveFormat.BlockAlign;
            var framePadding = waveFormat.BlockAlign - bytesPerSample * waveFormat.Channels;
            var output = new float[frameCount * waveFormat.Channels];

            if (encoding == WaveFormatEncoding.Pcm)
            {
                var sourceIndex = 0;
                var outputIndex = 0;
                for (var i = 0; i < frameCount; i++)
                {
                    for (var j = 0; j < waveFormat.Channels; j++)
                    {
                        if (waveFormat.BitsPerSample == 16)
                        {
                            output[outputIndex] = BitConverter.ToInt16(samples, sourceIndex)/32768f;
                        }
                        else if (waveFormat.BitsPerSample == 24)
                        {
                            output[outputIndex] = (((sbyte)samples[sourceIndex + 2] << 16) | (samples[sourceIndex + 1] << 8) | samples[sourceIndex]) / 8388608f;
                        }
                        else if (waveFormat.BitsPerSample == 32)
                        {
                            output[outputIndex] = BitConverter.ToInt32(samples, sourceIndex) / (Int32.MaxValue + 1f);
                        }
                        sourceIndex += bytesPerSample;
                        outputIndex++;
                    }
                    sourceIndex += framePadding;
                }
            }
            else if (encoding == WaveFormatEncoding.IeeeFloat)
            {
                var sourceIndex = 0;
                var outputIndex = 0;
                for (var i = 0; i < frameCount; i++)
                {
                    for (var j = 0; j < waveFormat.Channels; j++)
                    {
                        if (waveFormat.BitsPerSample == 32)
                        {
                            output[outputIndex] = BitConverter.ToSingle(samples, sourceIndex);
                        }
                        sourceIndex += bytesPerSample;
                        outputIndex++;
                    }
                    sourceIndex += framePadding;
                }
            }

            return output;
        }

        public static float? decibelToFloat(float? decibel)
        {
            if (decibel == null)
                return null;
            return (float)Math.Pow(10.0, (Double)decibel / 20.0);
        }
    }
}