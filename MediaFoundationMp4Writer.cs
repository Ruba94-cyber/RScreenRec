using System;
using System.Runtime.InteropServices;

namespace RScreenRec
{
    internal sealed class MediaFoundationMp4Writer : IDisposable
    {
        private const int MF_VERSION = 0x00020070;
        private const int MFSTARTUP_FULL = 0;
        private const int COINIT_MULTITHREADED = 0;
        private const long HnsPerSecond = 10000000L;

        private readonly int width;
        private readonly int height;
        private readonly int fps;
        private readonly long frameDuration;
        private IMFSinkWriter sinkWriter;
        private int streamIndex;
        private bool finalized;
        private bool mediaFoundationStarted;
        private bool comInitialized;

        public MediaFoundationMp4Writer(string outputPath, int width, int height, int fps, int bitrate)
        {
            this.width = width;
            this.height = height;
            this.fps = fps;
            frameDuration = HnsPerSecond / fps;

            Initialize(outputPath, bitrate);
        }

        public void WriteFrame(byte[] frameData, int length, long frameIndex)
        {
            if (finalized)
                throw new InvalidOperationException("Cannot write frames after finalizing the MP4 writer.");
            if (frameData == null)
                throw new ArgumentNullException("frameData");
            if (length != width * height * 4)
                throw new ArgumentException("Frame data length does not match the configured RGB32 frame size.", "length");

            IMFMediaBuffer buffer = null;
            IMFSample sample = null;

            try
            {
                ThrowIfFailed(MFCreateMemoryBuffer(length, out buffer), "MFCreateMemoryBuffer");

                IntPtr destination;
                int maxLength;
                int currentLength;
                ThrowIfFailed(buffer.Lock(out destination, out maxLength, out currentLength), "IMFMediaBuffer.Lock");
                try
                {
                    Marshal.Copy(frameData, 0, destination, length);
                }
                finally
                {
                    ThrowIfFailed(buffer.Unlock(), "IMFMediaBuffer.Unlock");
                }

                ThrowIfFailed(buffer.SetCurrentLength(length), "IMFMediaBuffer.SetCurrentLength");
                ThrowIfFailed(MFCreateSample(out sample), "MFCreateSample");
                ThrowIfFailed(sample.AddBuffer(buffer), "IMFSample.AddBuffer");
                ThrowIfFailed(sample.SetSampleTime(frameIndex * frameDuration), "IMFSample.SetSampleTime");
                ThrowIfFailed(sample.SetSampleDuration(frameDuration), "IMFSample.SetSampleDuration");
                ThrowIfFailed(sinkWriter.WriteSample(streamIndex, sample), "IMFSinkWriter.WriteSample");
            }
            finally
            {
                ReleaseComObject(sample);
                ReleaseComObject(buffer);
            }
        }

        public void Dispose()
        {
            FinalizeWriter();
            if (mediaFoundationStarted)
            {
                try { MFShutdown(); } catch { }
                mediaFoundationStarted = false;
            }

            if (comInitialized)
            {
                CoUninitialize();
                comInitialized = false;
            }
        }

        private void Initialize(string outputPath, int bitrate)
        {
            int hr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);
            if (hr >= 0)
                comInitialized = true;

            Logger.Log("Media Foundation startup begin.");
            ThrowIfFailed(MFStartup(MF_VERSION, MFSTARTUP_FULL), "MFStartup");
            mediaFoundationStarted = true;

            IMFAttributes sinkAttributes = null;
            IMFMediaType outputType = null;
            IMFMediaType inputType = null;

            try
            {
                ThrowIfFailed(MFCreateAttributes(out sinkAttributes, 2), "MFCreateAttributes");
                SetGuid(sinkAttributes, MediaFoundationGuids.MF_TRANSCODE_CONTAINERTYPE, MediaFoundationGuids.MFTranscodeContainerType_MPEG4);
                SetUInt32(sinkAttributes, MediaFoundationGuids.MF_SINK_WRITER_DISABLE_THROTTLING, 1);

                Logger.Log("Creating MP4 sink writer.");
                ThrowIfFailed(MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, sinkAttributes, out sinkWriter), "MFCreateSinkWriterFromURL");

                ThrowIfFailed(MFCreateMediaType(out outputType), "MFCreateMediaType(output)");
                SetGuid(outputType, MediaFoundationGuids.MF_MT_MAJOR_TYPE, MediaFoundationGuids.MFMediaType_Video);
                SetGuid(outputType, MediaFoundationGuids.MF_MT_SUBTYPE, MediaFoundationGuids.MFVideoFormat_H264);
                SetUInt32(outputType, MediaFoundationGuids.MF_MT_AVG_BITRATE, bitrate);
                SetUInt32(outputType, MediaFoundationGuids.MF_MT_INTERLACE_MODE, 2);
                SetSize(outputType, MediaFoundationGuids.MF_MT_FRAME_SIZE, width, height);
                SetRatio(outputType, MediaFoundationGuids.MF_MT_FRAME_RATE, fps, 1);
                SetRatio(outputType, MediaFoundationGuids.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                ThrowIfFailed(sinkWriter.AddStream(outputType, out streamIndex), "IMFSinkWriter.AddStream");

                ThrowIfFailed(MFCreateMediaType(out inputType), "MFCreateMediaType(input)");
                SetGuid(inputType, MediaFoundationGuids.MF_MT_MAJOR_TYPE, MediaFoundationGuids.MFMediaType_Video);
                SetGuid(inputType, MediaFoundationGuids.MF_MT_SUBTYPE, MediaFoundationGuids.MFVideoFormat_RGB32);
                SetUInt32(inputType, MediaFoundationGuids.MF_MT_INTERLACE_MODE, 2);
                SetUInt32(inputType, MediaFoundationGuids.MF_MT_DEFAULT_STRIDE, width * 4);
                SetSize(inputType, MediaFoundationGuids.MF_MT_FRAME_SIZE, width, height);
                SetRatio(inputType, MediaFoundationGuids.MF_MT_FRAME_RATE, fps, 1);
                SetRatio(inputType, MediaFoundationGuids.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                ThrowIfFailed(sinkWriter.SetInputMediaType(streamIndex, inputType, null), "IMFSinkWriter.SetInputMediaType");
                ThrowIfFailed(sinkWriter.BeginWriting(), "IMFSinkWriter.BeginWriting");
                Logger.Log("Media Foundation writer ready.");
            }
            finally
            {
                ReleaseComObject(inputType);
                ReleaseComObject(outputType);
                ReleaseComObject(sinkAttributes);
            }
        }

        private void FinalizeWriter()
        {
            if (finalized)
                return;

            finalized = true;
            if (sinkWriter == null)
                return;

            try
            {
                ThrowIfFailed(sinkWriter.Finalize_(), "IMFSinkWriter.Finalize");
            }
            finally
            {
                ReleaseComObject(sinkWriter);
                sinkWriter = null;
            }
        }

        private static void SetGuid(IMFAttributes attributes, Guid key, Guid value)
        {
            ThrowIfFailed(attributes.SetGUID(ref key, ref value), "IMFAttributes.SetGUID");
        }

        private static void SetGuid(IMFMediaType attributes, Guid key, Guid value)
        {
            ThrowIfFailed(attributes.SetGUID(ref key, ref value), "IMFMediaType.SetGUID");
        }

        private static void SetUInt32(IMFAttributes attributes, Guid key, int value)
        {
            ThrowIfFailed(attributes.SetUINT32(ref key, value), "IMFAttributes.SetUINT32");
        }

        private static void SetUInt32(IMFMediaType attributes, Guid key, int value)
        {
            ThrowIfFailed(attributes.SetUINT32(ref key, value), "IMFMediaType.SetUINT32");
        }

        private static void SetSize(IMFAttributes attributes, Guid key, int width, int height)
        {
            ThrowIfFailed(attributes.SetUINT64(ref key, Pack2UInt32AsUInt64(width, height)), "IMFAttributes.SetUINT64(size)");
        }

        private static void SetSize(IMFMediaType attributes, Guid key, int width, int height)
        {
            ThrowIfFailed(attributes.SetUINT64(ref key, Pack2UInt32AsUInt64(width, height)), "IMFMediaType.SetUINT64(size)");
        }

        private static void SetRatio(IMFAttributes attributes, Guid key, int numerator, int denominator)
        {
            ThrowIfFailed(attributes.SetUINT64(ref key, Pack2UInt32AsUInt64(numerator, denominator)), "IMFAttributes.SetUINT64(ratio)");
        }

        private static void SetRatio(IMFMediaType attributes, Guid key, int numerator, int denominator)
        {
            ThrowIfFailed(attributes.SetUINT64(ref key, Pack2UInt32AsUInt64(numerator, denominator)), "IMFMediaType.SetUINT64(ratio)");
        }

        private static long Pack2UInt32AsUInt64(int high, int low)
        {
            return (long)(((ulong)(uint)high << 32) | (uint)low);
        }

        private static void ThrowIfFailed(int hr, string operation)
        {
            if (hr < 0)
                throw new InvalidOperationException(string.Format("{0} failed with HRESULT 0x{1:X8}.", operation, hr));
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null)
                Marshal.ReleaseComObject(comObject);
        }

        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern void CoUninitialize();

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFStartup(int version, int dwFlags);

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

        [DllImport("mfplat.dll", PreserveSig = true)]
        private static extern int MFCreateSample(out IMFSample ppIMFSample);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int MFCreateSinkWriterFromURL(
            string pwszOutputURL,
            IntPtr pByteStream,
            IMFAttributes pAttributes,
            out IMFSinkWriter ppSinkWriter);

        private static class MediaFoundationGuids
        {
            public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
            public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
            public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
            public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
            public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
            public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
            public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
            public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
            public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("A634A91C-822B-41B9-A494-4DE4643612B0");
            public static readonly Guid MF_LOW_LATENCY = new Guid("9C27891A-ED7A-40E1-88E8-B22727A024EE");
            public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new Guid("08B845D8-2B74-4AFE-9D53-BE16D2D5AE4F");
            public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new Guid("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA");
            public static readonly Guid MFTranscodeContainerType_MPEG4 = new Guid("DC6CD05D-B9D0-40EF-BD35-FA622C1AB28A");
            public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        }

        [ComImport]
        [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize, out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        }

        [ComImport]
        [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize, out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);
            [PreserveSig] int GetMajorType(out Guid pguidMajorType);
            [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
            [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out int pdwFlags);
            [PreserveSig] int GetRepresentation(ref Guid guidRepresentation, out IntPtr ppvRepresentation);
            [PreserveSig] int FreeRepresentation(ref Guid guidRepresentation, IntPtr pvRepresentation);
        }

        [ComImport]
        [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
            [PreserveSig] int SetCurrentLength(int cbCurrentLength);
            [PreserveSig] int GetMaxLength(out int pcbMaxLength);
        }

        [ComImport]
        [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize, out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(ref Guid guidKey, IntPtr pBuf, int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);
            [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);
            [PreserveSig] int SetSampleFlags(int dwSampleFlags);
            [PreserveSig] int GetSampleTime(out long phnsSampleTime);
            [PreserveSig] int SetSampleTime(long hnsSampleTime);
            [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
            [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
            [PreserveSig] int GetBufferCount(out int pdwBufferCount);
            [PreserveSig] int GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
            [PreserveSig] int RemoveBufferByIndex(int dwIndex);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out int pcbTotalLength);
            [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);
        }

        [ComImport]
        [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSinkWriter
        {
            [PreserveSig] int AddStream(IMFMediaType pTargetMediaType, out int pdwStreamIndex);
            [PreserveSig] int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes pEncodingParameters);
            [PreserveSig] int BeginWriting();
            [PreserveSig] int WriteSample(int dwStreamIndex, IMFSample pSample);
            [PreserveSig] int SendStreamTick(int dwStreamIndex, long llTimestamp);
            [PreserveSig] int PlaceMarker(int dwStreamIndex, IntPtr pvContext);
            [PreserveSig] int NotifyEndOfSegment(int dwStreamIndex);
            [PreserveSig] int Flush(int dwStreamIndex);
            [PreserveSig] int Finalize_();
            [PreserveSig] int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
            [PreserveSig] int GetStatistics(int dwStreamIndex, IntPtr pStats);
        }
    }
}
