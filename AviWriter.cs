using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RScreenRec;

namespace RScreenRec.Avi
{
    public class AviWriter : IDisposable
    {
        public enum VideoCodec
        {
            Rgb24,
            Mjpeg
        }

        public const long MaxUsableFileSizeBytes = (4L * 1024 * 1024 * 1024) - (512 * 1024); // keep under the AVI 4 GB boundary

        private readonly BinaryWriter writer;
        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly VideoCodec codec;
        private readonly string frameChunkFourCC;
        private readonly string streamHandlerFourCC;
        private readonly int compressionFourCC;
        private readonly int rawFrameSize;
        private int maxFrameSize;
        private long totalFrameBytes;
        private long avihMicroSecPerFramePos;
        private long avihMaxBytesPerSecPos;
        private long avihBufferSizePos;
        private long strhScalePos;
        private long strhRatePos;
        private long strhBufferSizePos;
        private long strfSizeImagePos;
        private readonly List<long> frameOffsets = new List<long>();
        private readonly List<int> frameSizes = new List<int>();
        private long moviStartPos;
        private long moviDataStartPos;
        private long riffSizePos;
        private long totalFramesPos;
        private long streamLengthPos;
        private bool isClosed = false;

        public AviWriter(string path, int width, int height, int fps = 30, VideoCodec codec = VideoCodec.Rgb24)
        {
            this.width = width;
            this.height = height;
            this.fps = fps;
            this.codec = codec;
            frameChunkFourCC = codec == VideoCodec.Mjpeg ? "00dc" : "00db";
            streamHandlerFourCC = codec == VideoCodec.Mjpeg ? "MJPG" : "DIB ";
            compressionFourCC = codec == VideoCodec.Mjpeg ? 0x47504A4D : 0; // 'MJPG'
            rawFrameSize = width * height * 3;
            maxFrameSize = rawFrameSize;
            totalFrameBytes = 0;

            writer = new BinaryWriter(File.Create(path));
            WriteHeaders();
            Logger.Log($"AVI init: moviStartPos={moviStartPos}, moviDataStartPos={moviDataStartPos}, pos={writer.BaseStream.Position}, codec={codec}, size={width}x{height}");
        }

        private void WriteHeaders()
        {
            uint initialBytesPerSecond = (uint)Math.Min((long)rawFrameSize * fps, uint.MaxValue);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            riffSizePos = writer.BaseStream.Position;
            writer.Write(0); // Placeholder for RIFF chunk size
            writer.Write(Encoding.ASCII.GetBytes("AVI "));

            // LIST hdrl
            WriteList("hdrl", () =>
            {
                WriteChunk("avih", () =>
                {
                    avihMicroSecPerFramePos = writer.BaseStream.Position;
                    writer.Write((uint)(1000000 / fps)); // Microseconds per frame
                    avihMaxBytesPerSecPos = writer.BaseStream.Position;
                    writer.Write(initialBytesPerSecond); // MaxBytesPerSec (approx.)
                    writer.Write(0); // PaddingGranularity
                    writer.Write(0x10); // Flags: HAS_INDEX
                    totalFramesPos = writer.BaseStream.Position;
                    writer.Write(0); // TotalFrames (to be updated)
                    writer.Write(0); // InitialFrames
                    writer.Write(1); // Streams
                    avihBufferSizePos = writer.BaseStream.Position;
                    writer.Write((uint)maxFrameSize); // SuggestedBufferSize (updated on close)
                    writer.Write(width);
                    writer.Write(height);
                    writer.Write(new byte[16]); // Reserved
                });

                // LIST strl
                WriteList("strl", () =>
                {
                    WriteChunk("strh", () =>
                    {
                        writer.Write(Encoding.ASCII.GetBytes("vids")); // fccType
                        writer.Write(Encoding.ASCII.GetBytes(streamHandlerFourCC)); // fccHandler
                        writer.Write(0); // Flags
                        writer.Write((ushort)0); writer.Write((ushort)0); // Priority, Language
                        writer.Write(0); // InitialFrames
                        strhScalePos = writer.BaseStream.Position;
                        writer.Write(1); // Scale
                        strhRatePos = writer.BaseStream.Position;
                        writer.Write(fps); // Rate
                        writer.Write(0); // Start
                        streamLengthPos = writer.BaseStream.Position;
                        writer.Write(0); // Length (to be updated)
                        strhBufferSizePos = writer.BaseStream.Position;
                        writer.Write((uint)maxFrameSize); // SuggestedBufferSize (updated on close)
                        writer.Write(uint.MaxValue); // Quality
                        writer.Write(0); // SampleSize
                        writer.Write((short)0); writer.Write((short)0); // left, top
                        writer.Write((short)width); writer.Write((short)height); // right, bottom
                    });

                    WriteChunk("strf", () =>
                    {
                        writer.Write(40); // BITMAPINFOHEADER size
                        writer.Write(width);
                        writer.Write(height);
                        writer.Write((ushort)1); // Planes
                        writer.Write((ushort)24); // BitCount
                        writer.Write(compressionFourCC); // Compression
                        strfSizeImagePos = writer.BaseStream.Position;
                        writer.Write(codec == VideoCodec.Mjpeg ? 0u : (uint)rawFrameSize); // SizeImage
                        writer.Write(0); // XPelsPerMeter
                        writer.Write(0); // YPelsPerMeter
                        writer.Write(0); // ClrUsed
                        writer.Write(0); // ClrImportant
                    });
                });
            });

            // LIST movi
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            moviStartPos = writer.BaseStream.Position;
            writer.Write(0); // Placeholder for movi size
            writer.Write(Encoding.ASCII.GetBytes("movi"));
            moviDataStartPos = writer.BaseStream.Position;
        }

        public void WriteFrame(byte[] frameData)
        {
            WriteFrame(frameData, frameData?.Length ?? 0);
        }

        public void WriteFrame(byte[] frameData, int length)
        {
            if (isClosed)
                throw new InvalidOperationException("Cannot write frames after the AVI writer has been closed.");
            if (frameData == null)
                throw new ArgumentNullException(nameof(frameData));
            if (length <= 0 || length > frameData.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "Frame length must reference valid data.");
            if (codec == VideoCodec.Rgb24 && length != rawFrameSize)
                throw new ArgumentException($"Frame data must be exactly {rawFrameSize} bytes for {width}x{height} 24bpp frames.", nameof(frameData));

            long chunkStart = writer.BaseStream.Position;

            writer.Write(Encoding.ASCII.GetBytes(frameChunkFourCC)); // frame chunk
            writer.Write(length);
            writer.BaseStream.Write(frameData, 0, length);

            if (length % 2 != 0)
                writer.Write((byte)0); // padding

            frameOffsets.Add(chunkStart - moviDataStartPos);
            frameSizes.Add(length);
            maxFrameSize = Math.Max(maxFrameSize, length);
            totalFrameBytes += length + (length % 2);
        }

        public bool WouldExceedLimit(int nextFrameSize, long maxFileSize = MaxUsableFileSizeBytes)
        {
            if (maxFileSize <= 0)
                return false;

            long padding = (nextFrameSize % 2);
            long nextChunkBytes = 8 + nextFrameSize + padding;
            long futureIndexBytes = 8 + ((frameOffsets.Count + 1) * 16);
            long projected = writer.BaseStream.Position + nextChunkBytes + futureIndexBytes;

            return projected >= maxFileSize;
        }

        public void Close(TimeSpan? actualDuration = null)
        {
            if (isClosed) return;
            isClosed = true;

            if (frameOffsets.Count > 0)
            {
                double effectiveFps = fps;
                if (actualDuration.HasValue && actualDuration.Value.TotalSeconds > 0.001)
                {
                    effectiveFps = frameOffsets.Count / actualDuration.Value.TotalSeconds;
                    // Clamp to sensible bounds to avoid corrupt headers on very short recordings
                    effectiveFps = Math.Max(1, Math.Min(120, effectiveFps));
                }
                long endPos = writer.BaseStream.Position;
                UpdateTimingHeaders(effectiveFps, actualDuration);
                uint suggestedBuffer = (uint)Math.Min(Math.Max(maxFrameSize, rawFrameSize), uint.MaxValue);
                UpdateBufferSizes(suggestedBuffer);
                writer.BaseStream.Seek(endPos, SeekOrigin.Begin);
            }
            else
            {
                Logger.Log("AVI close called with zero frames written.");
            }

            long moviEnd = writer.BaseStream.Position;
            // LIST size is the number of bytes that follow the size field
            long moviSize = moviEnd - moviStartPos - 4;
            Logger.Log($"AVI close: moviStartPos={moviStartPos}, moviDataStartPos={moviDataStartPos}, moviEnd={moviEnd}, moviSize={moviSize}, frames={frameOffsets.Count}");

            // Update movi size
            writer.BaseStream.Seek(moviStartPos, SeekOrigin.Begin);
            writer.Write((int)moviSize);
            writer.BaseStream.Seek(moviEnd, SeekOrigin.Begin);

            // Write idx1 chunk
            writer.Write(Encoding.ASCII.GetBytes("idx1"));
            writer.Write(frameOffsets.Count * 16);
            for (int i = 0; i < frameOffsets.Count; i++)
            {
                var offset = frameOffsets[i];
                writer.Write(Encoding.ASCII.GetBytes(frameChunkFourCC));
                writer.Write(0x10); // key frame flag
                writer.Write((int)offset); // relative offset from start of movi data
                writer.Write(frameSizes[i]);
            }
            Logger.Log($"AVI idx1 written at {writer.BaseStream.Position}, entries={frameOffsets.Count}");

            // Update frame counts in header
            writer.BaseStream.Seek(totalFramesPos, SeekOrigin.Begin);
            writer.Write(frameOffsets.Count);
            writer.BaseStream.Seek(streamLengthPos, SeekOrigin.Begin);
            writer.Write(frameOffsets.Count);

            // Update RIFF size
            long fileEnd = writer.BaseStream.Length;
            writer.BaseStream.Seek(riffSizePos, SeekOrigin.Begin);
            writer.Write((int)(fileEnd - 8));

            writer.Flush();
            writer.Close();
        }

        private void UpdateTimingHeaders(double effectiveFps, TimeSpan? actualDuration)
        {
            uint microSecPerFrame = (uint)Math.Max(1, Math.Min(1_000_000, Math.Round(1_000_000.0 / effectiveFps)));
            uint bytesPerSec;
            if (actualDuration.HasValue && actualDuration.Value.TotalSeconds > 0.001)
            {
                bytesPerSec = (uint)Math.Min((long)Math.Round(totalFrameBytes / actualDuration.Value.TotalSeconds), uint.MaxValue);
            }
            else
            {
                bytesPerSec = (uint)Math.Min((long)Math.Round(maxFrameSize * effectiveFps), uint.MaxValue);
            }

            // Use a larger scale to preserve fractional fps if necessary
            const int timeScale = 1000; // 1/timeScale seconds per unit
            int rate = (int)Math.Max(1, Math.Round(effectiveFps * timeScale));

            long currentPos = writer.BaseStream.Position;

            writer.BaseStream.Seek(avihMicroSecPerFramePos, SeekOrigin.Begin);
            writer.Write(microSecPerFrame);

            writer.BaseStream.Seek(avihMaxBytesPerSecPos, SeekOrigin.Begin);
            writer.Write(bytesPerSec);

            writer.BaseStream.Seek(strhScalePos, SeekOrigin.Begin);
            writer.Write(timeScale);

            writer.BaseStream.Seek(strhRatePos, SeekOrigin.Begin);
            writer.Write(rate);

            writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);
        }

        private void UpdateBufferSizes(uint bufferSize)
        {
            long currentPos = writer.BaseStream.Position;

            writer.BaseStream.Seek(avihBufferSizePos, SeekOrigin.Begin);
            writer.Write(bufferSize);

            writer.BaseStream.Seek(strhBufferSizePos, SeekOrigin.Begin);
            writer.Write(bufferSize);

            writer.BaseStream.Seek(strfSizeImagePos, SeekOrigin.Begin);
            writer.Write(bufferSize);

            writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);
        }

        private void WriteList(string fourCC, Action inner)
        {
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            long sizePos = writer.BaseStream.Position;
            writer.Write(0); // placeholder
            writer.Write(Encoding.ASCII.GetBytes(fourCC));
            long before = writer.BaseStream.Position;
            inner();
            long after = writer.BaseStream.Position;
            int size = (int)(after - sizePos - 4);
            writer.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            writer.Write(size);
            writer.BaseStream.Seek(after, SeekOrigin.Begin);
        }

        private void WriteChunk(string fourCC, Action inner)
        {
            writer.Write(Encoding.ASCII.GetBytes(fourCC));
            long sizePos = writer.BaseStream.Position;
            writer.Write(0); // placeholder
            long before = writer.BaseStream.Position;
            inner();
            long after = writer.BaseStream.Position;
            int size = (int)(after - before);
            writer.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            writer.Write(size);
            writer.BaseStream.Seek(after + (size % 2), SeekOrigin.Begin);
            if (size % 2 != 0)
                writer.Write((byte)0); // padding
        }

        public void Dispose()
        {
            if (!isClosed)
            {
                Close();
            }
        }
    }
}
