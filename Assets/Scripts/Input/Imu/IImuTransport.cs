using System;

namespace EndlessRollball.Imu
{
    public enum ImuConnectionState { Disconnected, Scanning, Connecting, Receiving, DataLost, Error }

    public readonly struct ImuTransportMessage
    {
        public readonly byte[] Packet;
        public readonly double ReceivedAt;
        public readonly ImuConnectionState State;
        public readonly string Detail;
        public ImuTransportMessage(byte[] packet, double receivedAt)
        { Packet = packet; ReceivedAt = receivedAt; State = ImuConnectionState.Receiving; Detail = null; }
        public ImuTransportMessage(ImuConnectionState state, string detail)
        { Packet = null; ReceivedAt = 0; State = state; Detail = detail; }
    }

    public interface IImuTransport : IDisposable
    {
        bool IsRunning { get; }
        void Connect();
        void Disconnect();
        bool TryDequeue(out ImuTransportMessage message);
    }

    public readonly struct ImuSample
    {
        public readonly ushort Sequence;
        public readonly float W, X, Y, Z, Roll, Pitch;
        public readonly double ReceivedAt;
        public ImuSample(ushort sequence, float w, float x, float y, float z, double receivedAt)
        {
            Sequence = sequence; W = w; X = x; Y = y; Z = z; ReceivedAt = receivedAt;
            // Gravity vector from the firmware's Mahony convention; independent of heading.
            float gx = 2f * (x * z - w * y);
            float gy = 2f * (w * x + y * z);
            float gz = w * w - x * x - y * y + z * z;
            Roll = (float)(Math.Atan2(gy, gz) * 180.0 / Math.PI);
            Pitch = (float)(Math.Atan2(-gx, Math.Sqrt(gy * gy + gz * gz)) * 180.0 / Math.PI);
        }

        public static bool TryDecode(byte[] packet, double receivedAt, out ImuSample sample)
        {
            sample = default;
            if (packet == null || packet.Length != 18 || !BitConverter.IsLittleEndian) return false;
            float w = BitConverter.ToSingle(packet, 2), x = BitConverter.ToSingle(packet, 6);
            float y = BitConverter.ToSingle(packet, 10), z = BitConverter.ToSingle(packet, 14);
            double length = (double)w * w + (double)x * x + (double)y * y + (double)z * z;
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 0.000001) return false;
            float inverse = (float)(1.0 / Math.Sqrt(length));
            sample = new ImuSample(BitConverter.ToUInt16(packet, 0), w * inverse, x * inverse,
                y * inverse, z * inverse, receivedAt);
            return true;
        }
    }
}
