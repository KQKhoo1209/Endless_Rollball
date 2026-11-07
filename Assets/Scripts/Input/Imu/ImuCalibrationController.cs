using UnityEngine;

namespace EndlessRollball.Imu
{
    // Hiding the calibration page keeps the runtime sensor connection and neutral pose alive.
    [DisallowMultipleComponent, RequireComponent(typeof(SensorCalibrationUI))]
    public sealed class ImuCalibrationController : MonoBehaviour
    {
        private SensorCalibrationUI view;
        private ImuRuntimeSession session;
        private void OnEnable()
        {
            view = GetComponent<SensorCalibrationUI>();
            session = ImuRuntimeSession.Ensure();
            session.AttachView(view);
        }
        private void OnDisable()
        {
            if (session != null) session.DetachView(view);
        }
        public void Connect() => ImuRuntimeSession.Ensure().Connect();
        public void Disconnect() => ImuRuntimeSession.Ensure().Disconnect();
    }
}
