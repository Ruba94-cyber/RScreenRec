using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RScreenRec.Avi
{
    public class AviWriter : IDisposable
    {
        private readonly BinaryWriter writer;
        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly List<long> frameOffsets = new List<long>();
        private readonly List<int> frameSizes = new List<int>();
        private long moviStartPos;
        private long moviDataStartPos;
        private long riffSizePos;
        private long totalFramesPos;
        private long streamLengthPos;
        private bool isClosed = false;
        private readonly int bytesPerFrame;
        private readonly uint maxBytesPerSecond;

        public AviWriter(string path, int width, int height, int fps = 30)
        {
            this.width = width;
            this.height = height;
            this.fps = fps;
            bytesPerFrame = width * height * 3;
            long theoreticalBytesPerSecond = (long)bytesPerFrame * fps;
            maxBytesPerSecond = theoreticalBytesPerSecond > uint.MaxValue
                ? uint.MaxValue
                : (uint)theoreticalBytesPerSecond;

            writer = new BinaryWriter(File.Create(path));
            WriteHeaders();
        }

        private void WriteHeaders()
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            riffSizePos = writer.BaseStream.Position;
            writer.Write(0); // Placeholder for RIFF chunk size
            writer.Write(Encoding.ASCII.GetBytes("AVI "));

            // LIST hdrl
            WriteList("hdrl", () =>
            {
                WriteChunk("avih", () =>
                {
                    writer.Write((uint)(1000000 / fps)); // Microseconds per frame
                    writer.Write(maxBytesPerSecond); // MaxBytesPerSec (approx.)
                    writer.Write(0); // PaddingGranularity
                    writer.Write(0x10); // Flags: HAS_INDEX
                    totalFramesPos = writer.BaseStream.Position;
                    writer.Write(0); // TotalFrames (to be updated)
                    writer.Write(0); // InitialFrames
                    writer.Write(1); // Streams
                    writer.Write((uint)bytesPerFrame); // SuggestedBufferSize
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
                        writer.Write(Encoding.ASCII.GetBytes("DIB ")); // fccHandler (raw RGB)
                        writer.Write(0); // Flags
                        writer.Write((ushort)0); writer.Write((ushort)0); // Priority, Language
                        writer.Write(0); // InitialFrames
                        writer.Write(1); // Scale
                        writer.Write(fps); // Rate
                        writer.Write(0); // Start
                        streamLengthPos = writer.BaseStream.Position;
                        writer.Write(0); // Length (to be updated)
                        writer.Write((uint)bytesPerFrame); // SuggestedBufferSize
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
                        writer.Write(0); // Compression = BI_RGB
                        writer.Write((uint)bytesPerFrame); // SizeImage
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
            if (isClosed)
                throw new InvalidOperationException("Cannot write frames after the AVI writer has been closed.");
            if (frameData == null)
                throw new ArgumentNullException(nameof(frameData));
            if (frameData.Length != bytesPerFrame)
                throw new ArgumentException($"Frame data must be exactly {bytesPerFrame} bytes for {width}x{height} 24bpp frames.", nameof(frameData));

            long chunkStart = writer.BaseStream.Position;

            writer.Write(Encoding.ASCII.GetBytes("00db")); // uncompressed frame
            writer.Write(frameData.Length);
            writer.Write(frameData);

            if (frameData.Length % 2 != 0)
                writer.Write((byte)0); // padding

            frameOffsets.Add(chunkStart - moviDataStartPos);
            frameSizes.Add(frameData.Length);
        }

        public void Close()
        {
            if (isClosed) return;
            isClosed = true;

            long moviEnd = writer.BaseStream.Position;
            long moviSize = moviEnd - moviStartPos - 4;

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
                writer.Write(Encoding.ASCII.GetBytes("00db"));
                writer.Write(0x10); // key frame flag
                writer.Write((int)offset); // relative offset from start of movi data
                writer.Write(frameSizes[i]);
            }

            // Update frame counts in header
            writer.BaseStream.Seek(totalFramesPos, SeekOrigin.Begin);
            writer.Write(frameOffsets.Count);
            writer.BaseStream.Seek(streamLengthPos, SeekOrigin.Begin);
            writer.Write(frameOffsets.Count);

            // Update RIFF size
            long fileEnd = writer.BaseStream.Length;
            writer.BaseStream.Seek(riffSizePos, SeekOrigin.Begin);
            writer.Write((int)(fileEnd - 8));

            writer.Close();
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
