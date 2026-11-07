using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace EndlessRollball.Imu
{
    /// <summary>Single-device owner of BleWinrtDll. No Unity API is called by the worker.</summary>
    public sealed class WindowsBleImuTransport : IImuTransport
    {
        public const string DeviceName = "GAIT-WRIST";
        public const string ServiceId = "19B30000-E8F2-537E-4F6C-D104768A1214";
        public const string CharacteristicId = "19B30001-E8F2-537E-4F6C-D104768A1214";
        private readonly object gate = new object();
        private readonly Queue<ImuTransportMessage> messages = new Queue<ImuTransportMessage>();
        private Thread worker;
        private volatile bool stop;
        private static int nativeOwner;
        // Upstream coroutines retain wchar_t pointers after returning. Keep UTF-16 storage
        // alive for the process, including a timed-out native operation's eventual callback.
        private static readonly Dictionary<string, IntPtr> nativeStrings = new Dictionary<string, IntPtr>();
        private static IntPtr NativeString(string text)
        {
            lock (nativeStrings)
            {
                if (!nativeStrings.TryGetValue(text, out IntPtr pointer))
                { pointer = Marshal.StringToHGlobalUni(text); nativeStrings.Add(text, pointer); }
                return pointer;
            }
        }
        public bool IsRunning => worker != null && worker.IsAlive;
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        public void Connect()
        {
            if (IsRunning) return;
            lock (gate) messages.Clear();
            stop = false;
            worker = new Thread(Run) { IsBackground = true, Name = "GAIT-WRIST BLE" };
            worker.Start();
        }

        public void Disconnect() { stop = true; }
        public void Dispose()
        {
            stop = true;
            // Polls are nonblocking. Bound the join so a driver problem cannot freeze Unity.
            if (IsRunning) worker.Join(500);
        }
        public bool TryDequeue(out ImuTransportMessage message)
        {
            lock (gate)
            {
                if (messages.Count != 0) { message = messages.Dequeue(); return true; }
                message = default; return false;
            }
        }
        private void Enqueue(ImuTransportMessage message)
        {
            if (stop) return;
            lock (gate)
            {
                if (messages.Count >= 128) messages.Dequeue();
                messages.Enqueue(message);
            }
        }
        private void Status(ImuConnectionState state, string detail) =>
            Enqueue(new ImuTransportMessage(state, detail));

        private void Run()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            bool ownsNative = false;
            bool loaded = false;
            bool apartmentInitialized = false;
            try
            {
                ownsNative = Interlocked.CompareExchange(ref nativeOwner, 1, 0) == 0;
                if (!ownsNative) throw new InvalidOperationException("Another sensor connection is still closing. Retry shortly.");
                int initialized = Native.RoInitialize(1);
                if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
                apartmentInitialized = true;
                Status(ImuConnectionState.Scanning, "Looking for GAIT-WRIST. Close other BLE receiver apps.");
                Native.StartDeviceScan();
                loaded = true;
                double deadline = Now + 15;
                string id = null;
                var names = new Dictionary<string, string>();
                var connectable = new HashSet<string>();
                while (!stop && Now < deadline && id == null)
                {
                    var device = new Native.DeviceUpdate();
                    if (Native.PollDevice(ref device, false) == Native.ScanStatus.AVAILABLE && !string.IsNullOrEmpty(device.id))
                    {
                        if (device.nameUpdated) names[device.id] = device.name;
                        if (device.isConnectableUpdated)
                        {
                            if (device.isConnectable) connectable.Add(device.id);
                            else connectable.Remove(device.id);
                        }
                        if (names.TryGetValue(device.id, out string name) && name == DeviceName && connectable.Contains(device.id)) id = device.id;
                    }
                    else Thread.Sleep(5);
                }
                Native.StopDeviceScan();
                if (stop) return;
                if (id == null) throw new InvalidOperationException("GAIT-WRIST was not found. Check Bluetooth and sensor power, then retry.");
                Status(ImuConnectionState.Connecting, "GAIT-WRIST detected. Discovering its motion service...");
                deadline = Now + 10;
                Native.ScanServices(NativeString(id));
                bool found = false;
                while (!stop && Now < deadline)
                {
                    var state = Native.PollService(out Native.Service service, false);
                    if (state == Native.ScanStatus.AVAILABLE && Same(service.uuid, ServiceId)) found = true;
                    if (state == Native.ScanStatus.FINISHED) break;
                    Thread.Sleep(5);
                }
                if (stop) return;
                if (!found) throw new InvalidOperationException("Motion service discovery failed. Reset the sensor once and retry.");
                Native.ScanCharacteristics(NativeString(id), NativeString(ServiceId));
                found = false;
                while (!stop && Now < deadline)
                {
                    var state = Native.PollCharacteristic(out Native.Characteristic characteristic, false);
                    if (state == Native.ScanStatus.AVAILABLE && Same(characteristic.uuid, CharacteristicId)) found = true;
                    if (state == Native.ScanStatus.FINISHED) break;
                    Thread.Sleep(5);
                }
                if (stop) return;
                if (!found) throw new InvalidOperationException("Motion characteristic was not found. Check the firmware and retry.");
                // Nonblocking subscription reports success through the first notification, not its return value.
                Native.SubscribeCharacteristic(NativeString(id), NativeString(ServiceId), NativeString(CharacteristicId), false);
                double firstPacketDeadline = Now + 10;
                bool received = false;
                while (!stop)
                {
                    if (Native.PollData(out Native.BleData data, false))
                    {
                        if (data.deviceId != id || !Same(data.serviceUuid, ServiceId) || !Same(data.characteristicUuid, CharacteristicId)) continue;
                        int size = data.size;
                        if (size < 0 || size > 512) continue;
                        var packet = new byte[size];
                        Array.Copy(data.buf, packet, size);
                        Enqueue(new ImuTransportMessage(packet, Now));
                        received |= ImuSample.TryDecode(packet, Now, out _);
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                    if (!received && Now > firstPacketDeadline)
                        throw new InvalidOperationException("No valid motion notifications received. Disconnect other apps, reset the sensor, and retry.");
                }
            }
            catch (Exception error)
            {
                Status(ImuConnectionState.Error, error is DllNotFoundException || error is BadImageFormatException
                    ? "Windows BLE plugin could not load. Use Windows x64 and install the Microsoft Visual C++ x64 Redistributable."
                    : error.Message);
            }
            finally
            {
                try { if (loaded) Native.Quit(); }
                catch (Exception) { /* Keep driver cleanup failures off Unity's thread. */ }
                finally
                {
                    if (apartmentInitialized) Native.RoUninitialize();
                    if (ownsNative) Interlocked.Exchange(ref nativeOwner, 0);
                }
            }
#else
            Status(ImuConnectionState.Error, "Direct BLE is supported on Windows x64 only.");
#endif
        }

        private static bool Same(string a, string b) => string.Equals(a?.Trim('{', '}'), b, StringComparison.OrdinalIgnoreCase);

        // ABI declarations adapted from adabru/BleWinrtDll, revision recorded beside the DLL.
        private static class Native
        {
            internal enum ScanStatus { PROCESSING, AVAILABLE, FINISHED }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct DeviceUpdate
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] public string id;
                [MarshalAs(UnmanagedType.I1)] public bool isConnectable;
                [MarshalAs(UnmanagedType.I1)] public bool isConnectableUpdated;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 50)] public string name;
                [MarshalAs(UnmanagedType.I1)] public bool nameUpdated;
            }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct Service { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] public string uuid; }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct Characteristic
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] public string uuid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)] public string description;
            }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct BleData
            {
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] buf;
                public short size;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string deviceId;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string serviceUuid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string characteristicUuid;
            }
            private const string Dll = "BleWinrtDll";
            [DllImport("combase.dll")] internal static extern int RoInitialize(uint type);
            [DllImport("combase.dll")] internal static extern void RoUninitialize();
            [DllImport(Dll)] internal static extern void StartDeviceScan();
            [DllImport(Dll)] internal static extern ScanStatus PollDevice(ref DeviceUpdate device, [MarshalAs(UnmanagedType.I1)] bool block);
            [DllImport(Dll)] internal static extern void StopDeviceScan();
            [DllImport(Dll)] internal static extern void ScanServices(IntPtr deviceId);
            [DllImport(Dll)] internal static extern ScanStatus PollService(out Service service, [MarshalAs(UnmanagedType.I1)] bool block);
            [DllImport(Dll)] internal static extern void ScanCharacteristics(IntPtr deviceId, IntPtr serviceId);
            [DllImport(Dll)] internal static extern ScanStatus PollCharacteristic(out Characteristic characteristic, [MarshalAs(UnmanagedType.I1)] bool block);
            [DllImport(Dll, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.I1)]
            internal static extern bool SubscribeCharacteristic(IntPtr deviceId, IntPtr serviceId, IntPtr characteristicId, [MarshalAs(UnmanagedType.I1)] bool block);
            [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool PollData(out BleData data, [MarshalAs(UnmanagedType.I1)] bool block);
            [DllImport(Dll)] internal static extern void Quit();
        }
    }
}
