using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sayac
{
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateOperation
    {
        void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateHandler
    {
        void ActivateCompleted(IActivateOperation operation);
    }

    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileMarker { }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class ActivateHandler : IActivateHandler, IAgileMarker
    {
        public readonly ManualResetEvent Done = new ManualResetEvent(false);
        public void ActivateCompleted(IActivateOperation operation) { Done.Set(); }
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IClient
    {
        int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        int GetBufferSize(out uint frames);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out uint padding);
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        int GetMixFormat(out IntPtr format);
        int GetDevicePeriod(out long standard, out long minimum);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr handle);
        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICapture
    {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    // Yalnızca Spotify uygulamasının sesindeki bas bölgesini izler. Ses kaydedilmez, yalnızca anlık 0-1 değeri üretilir.
    class AudioBeat
    {
        const int Size = 1024, Hop = 512;
        const int Loopback = 0x00020000, AutoConvert = unchecked((int)0x80000000), DefaultQuality = 0x08000000, SilentFlag = 2;
        const int SampleRate = 44100, ChannelCount = 2, SampleBits = 16;
        const string ProcessDevice = "VAD\\Process_Loopback";

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string device, ref Guid iid, IntPtr parameters, IActivateHandler handler, out IActivateOperation operation);
        const double LowHz = 40, HighHz = 160, Decay = 0.93, PeakDecay = 0.9996;

        volatile bool running;
        Thread thread;
        double level;
        readonly double[] window = new double[Size], ring = new double[Size], re = new double[Size], im = new double[Size];
        int filled, sinceHop;
        double peak;

        public double Level { get { return Volatile.Read(ref level); } }

        public AudioBeat()
        {
            for (int i = 0; i < Size; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));
        }

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Ritim" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            Volatile.Write(ref level, 0);
        }

        void Run()
        {
            while (running)
            {
                try { Capture(); }
                catch (COMException) { }
                catch (InvalidCastException) { }
                catch (NotSupportedException) { }
                catch (Exception error) { Program.LogError(error); }
                Volatile.Write(ref level, 0);
                if (running) Thread.Sleep(2000);
            }
        }

        static void Check(int result)
        {
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        // YouTube Music açıkken hedef tarayıcıdır, aksi halde Spotify dinlenir.
        public static volatile string SourceProcess;

        static int TargetProcess()
        {
            string name = Environment.GetEnvironmentVariable("SAYAC_SES_HEDEF") ?? SourceProcess ?? "Spotify";
            var all = Process.GetProcessesByName(name);
            try { return all.Length == 0 ? 0 : all.OrderBy(x => x.StartTime).First().Id; }
            catch (InvalidOperationException) { return 0; }
            catch (System.ComponentModel.Win32Exception) { return 0; }
        }

        static bool Alive(int id)
        {
            try { return !Process.GetProcessById(id).HasExited; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        // Windows 10 2004 ve üzeri: hedef uygulama ve alt süreçlerinin sesi ayrı yakalanır.
        static IClient OpenProcess(int id)
        {
            IntPtr parameters = Marshal.AllocHGlobal(12), variant = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.WriteInt32(parameters, 0, 1);
                Marshal.WriteInt32(parameters, 4, id);
                Marshal.WriteInt32(parameters, 8, 0);
                for (int i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
                Marshal.WriteInt16(variant, 0, 65);
                Marshal.WriteInt32(variant, 8, 12);
                Marshal.WriteIntPtr(variant, 16, parameters);
                var clientId = typeof(IClient).GUID;
                var handler = new ActivateHandler();
                IActivateOperation operation;
                ActivateAudioInterfaceAsync(ProcessDevice, ref clientId, variant, handler, out operation);
                if (!handler.Done.WaitOne(5000)) throw new COMException("Ses bağlantısı zaman aşımına uğradı");
                int result; object instance;
                operation.GetActivateResult(out result, out instance);
                Check(result);
                return (IClient)instance;
            }
            finally
            {
                Marshal.FreeHGlobal(parameters);
                Marshal.FreeHGlobal(variant);
            }
        }

        void Capture()
        {
            int target = TargetProcess();
            if (target == 0) return;
            var client = OpenProcess(target);
            IntPtr format = Marshal.AllocHGlobal(18);
            try
            {
                Marshal.WriteInt16(format, 0, 1);
                Marshal.WriteInt16(format, 2, ChannelCount);
                Marshal.WriteInt32(format, 4, SampleRate);
                Marshal.WriteInt32(format, 8, SampleRate * ChannelCount * SampleBits / 8);
                Marshal.WriteInt16(format, 12, ChannelCount * SampleBits / 8);
                Marshal.WriteInt16(format, 14, SampleBits);
                Marshal.WriteInt16(format, 16, 0);
                Check(client.Initialize(0, Loopback | AutoConvert | DefaultQuality, 2000000, 0, format, IntPtr.Zero));
            }
            finally { Marshal.FreeHGlobal(format); }
            var captureId = typeof(ICapture).GUID;
            object service;
            Check(client.GetService(ref captureId, out service));
            var capture = (ICapture)service;
            Check(client.Start());
            int ticks = 0;
            try
            {
                while (running)
                {
                    Thread.Sleep(10);
                    uint packet;
                    Check(capture.GetNextPacketSize(out packet));
                    if (packet == 0) Volatile.Write(ref level, Level * Decay);
                    while (packet > 0)
                    {
                        IntPtr data; uint frames, flags; ulong position, counter;
                        Check(capture.GetBuffer(out data, out frames, out flags, out position, out counter));
                        Feed(data, (int)frames, ChannelCount, SampleBits, SampleRate, (flags & SilentFlag) != 0);
                        Check(capture.ReleaseBuffer(frames));
                        Check(capture.GetNextPacketSize(out packet));
                    }
                    // Spotify kapanıp açılırsa yeni kopyaya bağlanılır.
                    if (++ticks >= 300)
                    {
                        ticks = 0;
                        if (!Alive(target) || TargetProcess() != target) return;
                    }
                }
            }
            finally { client.Stop(); }
        }

        void Feed(IntPtr data, int frames, int channels, int bits, int rate, bool silent)
        {
            int bytes = bits / 8;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                if (!silent)
                    for (int c = 0; c < channels; c++)
                    {
                        int offset = (f * channels + c) * bytes;
                        sum += bits == 32
                            ? BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, offset)), 0)
                            : Marshal.ReadInt16(data, offset) / 32768.0;
                    }
                ring[filled] = sum / channels;
                filled = (filled + 1) % Size;
                if (++sinceHop >= Hop)
                {
                    sinceHop = 0;
                    Analyze(rate);
                }
            }
        }

        void Analyze(int rate)
        {
            for (int i = 0; i < Size; i++)
            {
                re[i] = ring[(filled + i) % Size] * window[i];
                im[i] = 0;
            }
            Fft();
            int low = Math.Max(1, (int)(LowHz * Size / rate)), high = (int)(HighHz * Size / rate);
            double energy = 0;
            for (int i = low; i <= high; i++) energy += re[i] * re[i] + im[i] * im[i];
            double bass = Math.Sqrt(energy);
            // Tepe değere göre oranlanır; ses kısık veya yüksek olsa da benzer tepki verir.
            peak = Math.Max(peak * PeakDecay, bass);
            double value = peak > 0.02 ? bass / peak : 0;
            double shaped = Math.Max(0, (value - 0.4) / 0.6);
            shaped *= shaped;
            double now = Level;
            Volatile.Write(ref level, shaped > now ? shaped : now * Decay);
        }

        void Fft()
        {
            for (int i = 1, j = 0; i < Size; i++)
            {
                int bit = Size >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int length = 2; length <= Size; length <<= 1)
            {
                double angle = -2 * Math.PI / length;
                double stepRe = Math.Cos(angle), stepIm = Math.Sin(angle);
                for (int i = 0; i < Size; i += length)
                {
                    double wRe = 1, wIm = 0;
                    for (int k = 0; k < length / 2; k++)
                    {
                        int a = i + k, b = a + length / 2;
                        double xRe = re[b] * wRe - im[b] * wIm, xIm = re[b] * wIm + im[b] * wRe;
                        re[b] = re[a] - xRe; im[b] = im[a] - xIm;
                        re[a] += xRe; im[a] += xIm;
                        double next = wRe * stepRe - wIm * stepIm;
                        wIm = wRe * stepIm + wIm * stepRe;
                        wRe = next;
                    }
                }
            }
        }
    }
}
