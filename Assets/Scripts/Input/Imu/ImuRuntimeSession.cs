using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace EndlessRollball.Imu
{
    [DisallowMultipleComponent]
    public sealed class ImuRuntimeSession : MonoBehaviour
    {
        private SensorCalibrationUI view;
        private IImuTransport transport;
        private readonly List<ImuSample> window = new List<ImuSample>(100);
        private ImuConnectionState state;
        private ImuSample latest;
        private bool hasSample, sampling, calibrated, hasSequence;
        private ushort lastSequence;
        private int valid, invalid, dropped, duplicates, rateCount;
        private double rateStart, nextDiagnostics;
        private float rate;
        private Vector2 neutral;
        public bool IsCalibrated => calibrated && state == ImuConnectionState.Receiving;
        public Vector2 NeutralTiltDegrees => neutral;
        public ImuSample LatestSample => latest;
        public event Action<ImuSample> SampleReceived;

        public static ImuRuntimeSession Instance { get; private set; }
        public static ImuRuntimeSession Ensure()
        {
            if (Instance == null) new GameObject("IMU Runtime Session").AddComponent<ImuRuntimeSession>();
            return Instance;
        }
        public bool InputRequested { get; private set; }
        public ImuConnectionState ConnectionState => state;
        public string StatusMessage { get; private set; } = "Connect and calibrate your sensor.";
        public float CalibrationProgress { get; private set; }
        public int CalibrationRevision { get; private set; }
        public bool IsSampling => sampling;
        public bool HasFreshInput => IsCalibrated && hasSample && WindowsBleImuTransport.Now - latest.ReceivedAt <= .25;
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            rateStart = WindowsBleImuTransport.Now;
        }
        public void AttachView(SensorCalibrationUI target)
        {
            if (view != null) DetachView(view);
            view = target;
            view.ConnectionRequested.AddListener(Connect);
            view.CalibrationRequested.AddListener(Calibrate);
            SetState(state, StatusMessage);
            if (hasSample && state == ImuConnectionState.Receiving)
            {
                view.SetImuTiltDegrees(latest.Roll, latest.Pitch, true);
                if (calibrated) view.RestoreNeutralCalibration(neutral);
            }
            nextDiagnostics = 0;
        }
        public void DetachView(SensorCalibrationUI target)
        {
            if (view != target) return;
            view.ConnectionRequested.RemoveListener(Connect);
            view.CalibrationRequested.RemoveListener(Calibrate);
            view = null;
        }
        public void Connect()
        {
            InputRequested = true;
            if (transport != null && transport.IsRunning)
            {
                transport.Disconnect();
                ResetSession();
                SetState(ImuConnectionState.Disconnected, "The previous connection is closing. Wait a moment, then select Retry Connection again.");
                return;
            }
            transport?.Dispose();
            ResetSession();
            transport = new WindowsBleImuTransport();
            SetState(ImuConnectionState.Scanning, "Looking for GAIT-WRIST...");
            transport.Connect();
        }

        public void Disconnect()
        {
            transport?.Disconnect();
            ResetSession();
            SetState(ImuConnectionState.Disconnected, "Disconnected. Select Retry Connection to reconnect.");
        }

        public void Calibrate()
        {
            if (state != ImuConnectionState.Receiving || !hasSample) return;
            window.Clear();
            sampling = true;
            CalibrationProgress = 0;
            view?.BeginCalibrationDisplay();
            calibrated = false;
        }

        private void ResetSession()
        {
            window.Clear();
            hasSample = hasSequence = sampling = calibrated = false;
            valid = invalid = dropped = duplicates = rateCount = 0;
            rate = 0;
            rateStart = WindowsBleImuTransport.Now;
            neutral = Vector2.zero;
            CalibrationProgress = 0; CalibrationRevision++;
        }

        private void SetState(ImuConnectionState value, string detail)
        {
            state = value;
            StatusMessage = detail;
            bool detected = value == ImuConnectionState.Connecting || value == ImuConnectionState.Receiving || value == ImuConnectionState.DataLost;
            view?.SetConnectionState(detected, detected, value == ImuConnectionState.Receiving);
            view?.SetTransportStatus(value == ImuConnectionState.DataLost ? "Data lost" : value.ToString(), detail);
        }

        private void Update()
        {
            double now = WindowsBleImuTransport.Now;
            if (transport != null && state != ImuConnectionState.DataLost && state != ImuConnectionState.Disconnected)
            {
                int limit = 128;
                while (limit-- > 0 && transport.TryDequeue(out ImuTransportMessage message))
                {
                    if (message.Packet == null)
                    {
                        if (message.State == ImuConnectionState.Error)
                        { sampling = calibrated = false; window.Clear(); }
                        SetState(message.State, message.Detail);
                    }
                    else if (now - message.ReceivedAt <= .25) Accept(message);
                }
            }
            if (state == ImuConnectionState.Receiving && now - latest.ReceivedAt > .25)
            {
                transport?.Disconnect();
                window.Clear();
                sampling = calibrated = false;
                CalibrationProgress = 0;
                SetState(ImuConnectionState.DataLost, "No fresh motion data. Check the sensor, then select Retry Connection and calibrate again.");
            }
            if (now - rateStart >= 1)
            {
                rate = (float)(rateCount / (now - rateStart));
                rateCount = 0; rateStart = now;
            }
            if (now >= nextDiagnostics)
            {
                nextDiagnostics = now + .1;
                view?.SetDiagnostics(hasSample ? string.Format(CultureInfo.InvariantCulture,
                    "Seq {0} | {1:F1} packets/s | Age {2:F0} ms\nw {3:F3}  x {4:F3}  y {5:F3}  z {6:F3}\nRoll {7:F1}°  Pitch {8:F1}° | Valid {9}  Gaps {10}  Invalid {11}  Duplicates {12}",
                    latest.Sequence, rate, (now - latest.ReceivedAt) * 1000,
                    latest.W, latest.X, latest.Y, latest.Z, latest.Roll, latest.Pitch, valid, dropped, invalid, duplicates)
                    : "Waiting for live IMU data. No samples are recorded to disk.");
            }
        }

        private void Accept(ImuTransportMessage message)
        {
            if (!ImuSample.TryDecode(message.Packet, message.ReceivedAt, out ImuSample sample)) { invalid++; return; }
            if (hasSequence)
            {
                int delta = (sample.Sequence - lastSequence) & 0xffff;
                if (delta == 0) { duplicates++; return; }
                if (delta > 32767) { invalid++; return; }
                dropped += delta - 1;
            }
            hasSequence = true;
            lastSequence = sample.Sequence;
            bool gap = hasSample && sample.ReceivedAt - latest.ReceivedAt > .1;
            latest = sample;
            hasSample = true;
            valid++; rateCount++;
            if (state != ImuConnectionState.Receiving)
                SetState(ImuConnectionState.Receiving, "Receiving live motion. Rest your wrist, then select Calibrate.");

            if (gap) window.Clear();
            window.Add(sample);
            while (window.Count > 0 && sample.ReceivedAt - window[0].ReceivedAt > (sampling ? 1.6 : .3)) window.RemoveAt(0);
            Statistics(out Vector2 mean, out Vector2 deviation);
            bool stable = window.Count >= 10 && sample.ReceivedAt - window[0].ReceivedAt >= .15 &&
                deviation.x <= 2f && deviation.y <= 2f;
            view?.SetImuTiltDegrees(sample.Roll, sample.Pitch, stable);
            if (sampling)
            {
                bool withinLimits = deviation.x <= 2f && deviation.y <= 2f;
                if (!withinLimits)
                {
                    window.Clear(); window.Add(sample);
                    CalibrationProgress = 0;
                    view?.SetCalibrationProgress(0, false);
                }
                else
                {
                    double elapsed = sample.ReceivedAt - window[0].ReceivedAt;
                    bool complete = stable && elapsed >= 1.5 && window.Count >= 60;
                    CalibrationProgress = stable ? (float)Math.Min(1, Math.Min(elapsed / 1.5, window.Count / 60.0)) : 0;
                    view?.SetCalibrationProgress(CalibrationProgress, complete);
                    if (complete)
                    {
                        neutral = mean;
                        CalibrationRevision++;
                        sampling = false;
                        calibrated = true;
                        view?.SetNeutralTilt(neutral);
                    }
                }
            }
            SampleReceived?.Invoke(sample);
        }

        private void Statistics(out Vector2 mean, out Vector2 deviation)
        {
            // Unwrap around the first observation to handle +/-180 degrees continuously.
            Vector2 anchor = new Vector2(window[0].Roll, window[0].Pitch);
            double rx = 0, py = 0, r2 = 0, p2 = 0;
            foreach (ImuSample sample in window)
            {
                double r = Mathf.DeltaAngle(anchor.x, sample.Roll), p = Mathf.DeltaAngle(anchor.y, sample.Pitch);
                rx += r; py += p; r2 += r * r; p2 += p * p;
            }
            int count = window.Count;
            rx /= count; py /= count;
            mean = new Vector2(Mathf.DeltaAngle(0, anchor.x + (float)rx), Mathf.DeltaAngle(0, anchor.y + (float)py));
            deviation = new Vector2((float)Math.Sqrt(Math.Max(0, r2 / count - rx * rx)),
                (float)Math.Sqrt(Math.Max(0, p2 / count - py * py)));
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            transport?.Dispose(); transport = null;
            Instance = null;
        }
        private void OnApplicationQuit() => transport?.Dispose();
    }
}
