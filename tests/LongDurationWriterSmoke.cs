using System;
using System.IO;
using System.Reflection;

internal static class LongDurationWriterSmoke
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: LongDurationWriterSmoke <output.mp4>");
            return 2;
        }

        try
        {
            const int width = 160;
            const int height = 90;
            const int fps = 30;
            const int durationSeconds = 3601;
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            string runtimePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "RBG - RScreenRec.exe");
            Assembly product = Assembly.LoadFrom(runtimePath);
            Type writerType = product.GetType("RScreenRec.MediaFoundationMp4Writer", true);
            ConstructorInfo constructor = writerType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int) },
                null);
            MethodInfo writeFrame = writerType.GetMethod("WriteFrame", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo dispose = writerType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public);
            Type recorderType = product.GetType("RScreenRec.ScreenRecorder", true);
            FieldInfo qualityField = recorderType.GetField("ScreenRecordingQuality", BindingFlags.Static | BindingFlags.NonPublic);
            if (qualityField == null)
                throw new MissingFieldException("The recorder quality setting could not be found.");
            int quality = (int)qualityField.GetRawConstantValue();
            object writer = constructor.Invoke(new object[] { output, width, height, fps, quality });

            byte[] pixels = new byte[width * height * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 32;
                pixels[index + 1] = 64;
                pixels[index + 2] = 96;
                pixels[index + 3] = 255;
            }

            long frameCount = (long)durationSeconds * fps;
            for (long frame = 0; frame < frameCount; frame++)
            {
                writeFrame.Invoke(writer, new object[] { pixels, pixels.Length, frame });
                if (frame > 0 && frame % 30000 == 0)
                    Console.WriteLine("written_frames=" + frame);
            }

            dispose.Invoke(writer, null);
            Console.WriteLine("quality=" + quality + " synthetic_seconds=" + durationSeconds + " fps=" + fps +
                " frames=" + frameCount + " bytes=" + new FileInfo(output).Length);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 3;
        }
    }
}
