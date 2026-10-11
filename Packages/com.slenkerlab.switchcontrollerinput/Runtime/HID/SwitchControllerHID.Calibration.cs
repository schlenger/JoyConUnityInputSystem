using System;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem.LowLevel;

namespace UnityEngine.InputSystem.Switch
{
    // Calibration of the sticks and of the IMU: the data stored on the controller, how it is decoded and how it is changed.
    public abstract partial class SwitchControllerHID
    {
        public struct CalibrationData
        {
            public StickCalibrationData lStickCalibData;
            public StickCalibrationData rStickCalibData;
            public IMUCalibrationData imuCalibData;
        }

        public CalibrationData calibrationData = new CalibrationData()
        {
            lStickCalibData = new StickCalibrationData()
            {
                xMin = 720,
                xMax = 3408,
                yMin = 654,
                yMax = 2908
            },
            rStickCalibData = new StickCalibrationData()
            {
                xMin = 720,
                xMax = 3408,
                yMin = 654,
                yMax = 2908
            }
        };

        private bool m_stickRestChecked = false;

        public enum CalibrationState
        {
            None,
            InProgress,
            Done
        }

        public CalibrationState CurrentCalibrationState { get; private set; } = CalibrationState.None;
        public float CalibrationProgress { get; private set; } = 0f;
        private double m_calibrationStartTime;
        private float m_calibrationDuration;
        private Vector3 m_accumulatedAccel;
        private Vector3 m_accumulatedGyro;
        private int m_sampleCount;

        public void StartIMUCalibration(float duration = 5.0f)
        {
            if (CurrentCalibrationState == CalibrationState.InProgress)
            {
                Debug.LogWarning("Calibration is already in progress.");
                return;
            }
            Debug.Log($"Starting IMU calibration for {duration} seconds. Keep the controller stable.");
            CurrentCalibrationState = CalibrationState.InProgress;
            m_calibrationStartTime = InputRuntime.s_Instance.currentTime;
            m_calibrationDuration = duration;
            m_accumulatedAccel = Vector3.zero;
            m_accumulatedGyro = Vector3.zero;
            m_sampleCount = 0;
            CalibrationProgress = 0f;
        }

        /// <summary>
        /// Accumulate the IMU readings while a calibration is in progress, and write the result once it is over.
        /// </summary>
        private void UpdateIMUCalibration()
        {
            if (CurrentCalibrationState != CalibrationState.InProgress)
                return;

            double elapsedTime = InputRuntime.s_Instance.currentTime - m_calibrationStartTime;
            CalibrationProgress = (float)(elapsedTime / m_calibrationDuration);

            // Accumulate data
            m_accumulatedAccel += new Vector3(m_lastIMUData.accelX, m_lastIMUData.accelY, m_lastIMUData.accelZ);
            m_accumulatedGyro += new Vector3(m_lastIMUData.gyro1, m_lastIMUData.gyro2, m_lastIMUData.gyro3);
            m_sampleCount++;

            if (elapsedTime >= m_calibrationDuration)
            {
                // Finish calibration
                Vector3 avgAccel = m_accumulatedAccel / m_sampleCount;
                Vector3 avgGyro = m_accumulatedGyro / m_sampleCount;
                WriteUserIMUCalibrationData(avgAccel, avgGyro);
            }
        }

        /// <summary>
        /// Calibrates the IMU by setting the current raw sensor values as the new base/offset.
        /// The controller should be on a flat, stable surface when this is called.
        /// This will write to the user calibration data section in the controller's SPI flash.
        /// </summary>
        private unsafe void WriteUserIMUCalibrationData(Vector3 averageAccel, Vector3 averageGyro)
        {
            LogVerbose($"Calibration finished. Average Accel: {averageAccel}, Average Gyro: {averageGyro}. Writing to controller...");

            // The sensitivities are written back unchanged, so they have to be the ones read from the controller.
            // Writing the zeroes we start with would leave the controller with an unusable user calibration.
            if (!calibrationData.imuCalibData.IsValid)
            {
                Debug.LogError("IMU calibration was not written: the current calibration has to be read from the controller first");
                CurrentCalibrationState = CalibrationState.None;
                CalibrationProgress = 0f;
                return;
            }

            // Create a 26-byte (0x1A) payload for user calibration data.
            byte[] payload = new byte[0x1A];

            // Magic number `0xB2 0xA1` at the start.
            payload[0] = 0xB2;
            payload[1] = 0xA1;

            // Create a new calibration structure.
            // The new 'base' is the averaged raw sensor value.
            // For the accelerometer, we calculate the offset by removing the gravity component from the reading.
            // 1. Normalize the average acceleration vector to get the direction of gravity.
            Vector3 gravityDirection = new Vector3(averageAccel.x, averageAccel.y, averageAccel.z).normalized;
            // 2. 1G corresponds to a raw value of ~4096 (since 4G is ~16384).
            const float oneGAsRaw = 16384f / 4f;
            // 3. Calculate the ideal gravity vector in the current orientation.
            Vector3 idealGravityVector = gravityDirection * oneGAsRaw;
            // 4. The offset is the measured value minus the ideal gravity vector.
            Vector3 accelOffset = averageAccel - idealGravityVector;

            // For the gyroscope, we zero out all axes as it should be perfectly still.
            IMUCalibrationData newUserCalib = new IMUCalibrationData
            {
                accelBase = ToRawVector(accelOffset),
                // keep factory coefficients unchanged
                accelSensitivity = calibrationData.imuCalibData.accelSensitivity,

                gyroBase = ToRawVector(averageGyro),
                gyroSensitivity = calibrationData.imuCalibData.gyroSensitivity,
                // keep factory coefficients unchanged
            };

            // Manually copy the relevant fields (Base and Sensitivity) to the payload to avoid overflow
            // and to match the 24-byte structure expected by the controller's SPI flash.
            // The coefficients are not stored on the device.
            fixed (byte* p = &payload[2])
            {
                ushort* ushortPtr = (ushort*)p;
                ushortPtr[0] = newUserCalib.accelBase.x;
                ushortPtr[1] = newUserCalib.accelBase.y;
                ushortPtr[2] = newUserCalib.accelBase.z;
                ushortPtr[3] = newUserCalib.accelSensitivity.x;
                ushortPtr[4] = newUserCalib.accelSensitivity.y;
                ushortPtr[5] = newUserCalib.accelSensitivity.z;
                ushortPtr[6] = newUserCalib.gyroBase.x;
                ushortPtr[7] = newUserCalib.gyroBase.y;
                ushortPtr[8] = newUserCalib.gyroBase.z;
                ushortPtr[9] = newUserCalib.gyroSensitivity.x;
                ushortPtr[10] = newUserCalib.gyroSensitivity.y;
                ushortPtr[11] = newUserCalib.gyroSensitivity.z;
            }

            Debug.LogWarning("Writing current IMU calibration data to User SPI section. This is permanent!");

            var writeSubcommand = new SwitchControllerWriteSPIFlashSubcommand(
                atAddress: (uint)SPIFlashReadAddressEnum.UserIMUCalibration,
                data: payload
            );
            SendCommand("Write user IMU calibration", writeSubcommand);

            CurrentCalibrationState = CalibrationState.Done;
            CalibrationProgress = 1f;
        }

        /// <summary>
        /// The controller stores the sensor values as signed 16 bit integers. A negative float cannot be
        /// cast to ushort directly, the result of that depends on the platform.
        /// </summary>
        private static Vector3UInt16 ToRawVector(Vector3 value)
        {
            return new Vector3UInt16
            {
                x = (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(value.x), short.MinValue, short.MaxValue),
                y = (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(value.y), short.MinValue, short.MaxValue),
                z = (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(value.z), short.MinValue, short.MaxValue)
            };
        }

        /// <summary>
        /// The calibration stored on the controller (third party ones in particular) does not always match
        /// where the stick actually rests, which makes the stick drift. The stick is assumed to be untouched
        /// right after connecting, so if it reads outside of the deadzone then, its rest position becomes the center.
        /// </summary>
        private void RecenterStickIfOffCenter(ref StickCalibrationData calib, Vector2Int rest, string stickName)
        {
            const float tolerance = 0.1f;

            if (calib.xCenter <= calib.xMin || calib.xCenter >= calib.xMax)
                calib.xCenter = (ushort)((calib.xMin + calib.xMax) / 2);
            if (calib.yCenter <= calib.yMin || calib.yCenter >= calib.yMax)
                calib.yCenter = (ushort)((calib.yMin + calib.yMax) / 2);

            LogVerbose($"{displayName}: {stickName} stick rests at ({rest.x};{rest.y}), calibration is X {calib.xMin}/{calib.xCenter}/{calib.xMax}, Y {calib.yMin}/{calib.yCenter}/{calib.yMax}");

            int halfRangeX = (calib.xMax - calib.xMin) / 2;
            int halfRangeY = (calib.yMax - calib.yMin) / 2;
            if (Mathf.Abs(rest.x - calib.xCenter) <= halfRangeX * tolerance && Mathf.Abs(rest.y - calib.yCenter) <= halfRangeY * tolerance)
                return;

            Debug.LogWarning($"{displayName}: {stickName} stick calibration is off center, using the current stick position as center");
            SetStickCenter(ref calib, rest);
        }

        private static void SetStickCenter(ref StickCalibrationData calib, Vector2Int rest)
        {
            int halfRangeX = (calib.xMax - calib.xMin) / 2;
            int halfRangeY = (calib.yMax - calib.yMin) / 2;
            calib.xMin = (ushort)Mathf.Clamp(rest.x - halfRangeX, 0, 0xFFF);
            calib.xMax = (ushort)Mathf.Clamp(rest.x + halfRangeX, 0, 0xFFF);
            calib.yMin = (ushort)Mathf.Clamp(rest.y - halfRangeY, 0, 0xFFF);
            calib.yMax = (ushort)Mathf.Clamp(rest.y + halfRangeY, 0, 0xFFF);
            calib.xCenter = (ushort)rest.x;
            calib.yCenter = (ushort)rest.y;
        }

        /// <summary>
        /// Use the position the stick(s) are in right now as their center. Call this while the sticks are untouched.
        /// </summary>
        public void RecenterSticks()
        {
            if (HasLeftStick)
                SetStickCenter(ref calibrationData.lStickCalibData, RawLeftStick);
            if (HasRightStick)
                SetStickCenter(ref calibrationData.rStickCalibData, RawRightStick);
            m_stickRestChecked = true;
            LogVerbose($"{displayName}: sticks recentered");
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Vector3UInt16
        {
            public ushort x;
            public ushort y;
            public ushort z;

            public override string ToString()
            {
                return $"({x}, {y}, {z})";
            }
        }

        public struct IMUCalibrationData
        {
            public Vector3UInt16 accelBase;
            public Vector3UInt16 accelSensitivity;

            public Vector3UInt16 gyroBase;
            public Vector3UInt16 gyroSensitivity;
            
            public Vector3 accelCoeff;
            public Vector3 gyroCoeff;

            /// <summary>
            /// False as long as no calibration has been read from the controller, or if what was read is unusable.
            /// </summary>
            public bool IsValid =>
                IsValidAxis(accelBase.x, accelSensitivity.x) && IsValidAxis(accelBase.y, accelSensitivity.y) && IsValidAxis(accelBase.z, accelSensitivity.z) &&
                IsValidAxis(gyroBase.x, gyroSensitivity.x) && IsValidAxis(gyroBase.y, gyroSensitivity.y) && IsValidAxis(gyroBase.z, gyroSensitivity.z);

            // The coefficients divide by the difference between the two, and blank flash reads as 0xFFFF
            private static bool IsValidAxis(ushort axisBase, ushort axisSensitivity)
            {
                return axisSensitivity != 0 && axisSensitivity != 0xFFFF && axisSensitivity != axisBase;
            }

            public unsafe static IMUCalibrationData FromResponse(ushort* response)
            {
                return new IMUCalibrationData()
                {
                    accelBase = new Vector3UInt16()
                    {
                        x = response[0],
                        y = response[1],
                        z = response[2]
                    },

                    accelSensitivity = new Vector3UInt16()
                    {
                        x = response[3],
                        y = response[4],
                        z = response[5]
                    },

                    gyroBase = new Vector3UInt16()
                    {
                        x = response[6],
                        y = response[7],
                        z = response[8]
                    },

                    gyroSensitivity = new Vector3UInt16()
                    {
                        x = response[9],
                        y = response[10],
                        z = response[11]
                    }
                };
            }

            public override string ToString()
            {
                return $"accelBase = {accelBase}\naccelSen = {accelSensitivity}\ngyroBase = {gyroBase}\ngyroSen = {gyroSensitivity}";
            }
        }

        private unsafe void DecodeIMUCalibrationData(ushort* response)
        {
            var newCalibData = IMUCalibrationData.FromResponse(response);
            if (!newCalibData.IsValid)
            {
                Debug.LogWarning($"{displayName}: ignoring unusable IMU calibration data ({newCalibData})");
                return;
            }

            // Pre-calculate the coefficients once when calibration data is loaded.
            // This avoids redundant calculations in every input report.
            newCalibData.accelCoeff.x = 4.0f / ((short)newCalibData.accelSensitivity.x - (short)newCalibData.accelBase.x);
            newCalibData.accelCoeff.y = 4.0f / ((short)newCalibData.accelSensitivity.y - (short)newCalibData.accelBase.y);
            newCalibData.accelCoeff.z = 4.0f / ((short)newCalibData.accelSensitivity.z - (short)newCalibData.accelBase.z);

            newCalibData.gyroCoeff.x = 936.0f / ((short)newCalibData.gyroSensitivity.x - (short)newCalibData.gyroBase.x);
            newCalibData.gyroCoeff.y = 936.0f / ((short)newCalibData.gyroSensitivity.y - (short)newCalibData.gyroBase.y);
            newCalibData.gyroCoeff.z = 936.0f / ((short)newCalibData.gyroSensitivity.z - (short)newCalibData.gyroBase.z);

            calibrationData.imuCalibData = newCalibData;

            LogVerbose("Calibration data: " + calibrationData.imuCalibData);
        }

        private unsafe void DecodeStickCalibrationData(byte* response)
        {
            var leftStickCalib = StickCalibrationData.FromFlash(response, isRightStick: false);
            var rightStickCalib = StickCalibrationData.FromFlash(response + 9, isRightStick: true);

            // Keep the previous (default) values for sticks whose calibration data is unusable,
            // e.g. blank flash on a single Joy-Con's missing stick or on third party controllers.
            if (leftStickCalib.IsValid)
                calibrationData.lStickCalibData = leftStickCalib;
            if (rightStickCalib.IsValid)
                calibrationData.rStickCalibData = rightStickCalib;

            LogVerbose($"Calibration data loaded");
            m_stickConfigDataLoaded = true;
            m_stickRestChecked = false;
        }

        public struct StickCalibrationData
        {
            public ushort xMin;
            public ushort xMax;

            public ushort yMin;
            public ushort yMax;
            public ushort xCenter;
            public ushort yCenter;

            public bool IsValid => xMin < xMax && yMin < yMax && xMax <= 0xFFF && yMax <= 0xFFF
                && xMax - xMin > 0x400 && yMax - yMin > 0x400;

            /// <summary>
            /// Decode the 9 bytes a stick calibration takes in the SPI flash.
            /// </summary>
            internal static unsafe StickCalibrationData FromFlash(byte* stickCal, bool isRightStick)
            {
                // The bytes hold three pairs of 12 bit values, see
                // https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md#analog-stick-factory-and-user-calibration
                var pair0 = new Vector2Int((stickCal[1] << 8) & 0xF00 | stickCal[0], (stickCal[2] << 4) | (stickCal[1] >> 4));
                var pair1 = new Vector2Int((stickCal[4] << 8) & 0xF00 | stickCal[3], (stickCal[5] << 4) | (stickCal[4] >> 4));
                var pair2 = new Vector2Int((stickCal[7] << 8) & 0xF00 | stickCal[6], (stickCal[8] << 4) | (stickCal[7] >> 4));

                // The two sticks store them in a different order
                Vector2Int center = isRightStick ? pair0 : pair1;
                Vector2Int minBelowCenter = isRightStick ? pair1 : pair2;
                Vector2Int maxAboveCenter = isRightStick ? pair2 : pair0;

                return new StickCalibrationData()
                {
                    xMin = (ushort)(center.x - minBelowCenter.x),
                    xMax = (ushort)(center.x + maxAboveCenter.x),
                    yMin = (ushort)(center.y - minBelowCenter.y),
                    yMax = (ushort)(center.y + maxAboveCenter.y),
                    xCenter = (ushort)center.x,
                    yCenter = (ushort)center.y
                };
            }

            /// <summary>
            /// Map a raw 12 bit stick position to [-1, 1] on both axes.
            /// </summary>
            internal Vector2 Normalize(Vector2Int raw)
            {
                return new Vector2(
                    NormalizeAxis(raw.x, xMin, xCenter, xMax),
                    NormalizeAxis(raw.y, yMin, yCenter, yMax));
            }

            /// <summary>
            /// Map a raw 12 bit axis value to [-1, 1] so that the calibrated center is exactly 0.
            /// The center usually isn't halfway between min and max, so both halves are scaled separately.
            /// </summary>
            private static float NormalizeAxis(int raw, int min, int center, int max)
            {
                if (center <= min || center >= max)
                    center = (min + max) / 2;

                var value = raw >= center
                    ? (float)(raw - center) / (max - center)
                    : (float)(raw - center) / (center - min);
                return Mathf.Clamp(value, -1f, 1f);
            }

            public override string ToString()
            {
                return String.Format($"Center: ({xCenter:X2};{yCenter:X2}), X range: [{xMin:X2}-{xMax:X2}], Y range: [{yMin:X2}-{yMax:X2}]");
            }
        }
    }
}
