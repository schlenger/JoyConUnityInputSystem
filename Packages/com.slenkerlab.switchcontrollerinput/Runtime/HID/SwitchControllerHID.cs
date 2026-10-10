using System;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Switch.LowLevel;
using UnityEngine.InputSystem.Utilities;
using System.Runtime.InteropServices;
using System.Text;

namespace UnityEngine.InputSystem.Switch
{
    [InputControlLayout(stateType = typeof(SwitchControllerVirtualInputState))]
    public abstract class SwitchControllerHID : InputDevice, IInputStateCallbackReceiver, IEventPreProcessor
    {
        #region Accelerometer/gyroscope controls
        public Vector3Control angularVelocity { get; private set; }
        public Vector3Control orientation { get; private set; }
        public Vector3Control acceleration { get; private set; }
        public QuaternionControl deviceRotation { get; private set; }

        // Uncalibrated values for debugging
        public Vector3Control uncalibratedAngularVelocity { get; private set; }
        public Vector3Control uncalibratedAcceleration { get; private set; }
        #endregion

        #region Button controls
        public DpadControl dpad { get; private set; }

        public ButtonControl buttonWest { get; private set; }
        public ButtonControl buttonNorth { get; private set; }
        public ButtonControl buttonSouth { get; private set; }
        public ButtonControl buttonEast { get; private set; }

        public ButtonControl leftShoulder { get; private set; }
        public ButtonControl rightShoulder { get; private set; }
        public ButtonControl leftStickPress { get; private set; }
        public ButtonControl rightStickPress { get; private set; }
        public ButtonControl leftTrigger { get; private set; }
        public ButtonControl rightTrigger { get; private set; }
        public ButtonControl start { get; private set; }
        public ButtonControl select { get; private set; }
        public ButtonControl leftShoulderMini { get; private set; }
        public ButtonControl rightShoulderMini { get; private set; }

        public StickControl leftStick { get; private set; }
        public StickControl rightStick { get; private set; }
        #endregion

        #region Device colors
        public Color BodyColor { get; protected set; } = Color.black;
        public Color ButtonColor { get; protected set; } = Color.black;
        public Color LeftGripColor { get; protected set; } = Color.black;
        public Color RightGripColor { get; protected set; } = Color.black;
        #endregion

        #region Stick and IMU data
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
        #endregion

        /// <summary>Latest uncalibrated 12 bit stick positions as sent by the controller.</summary>
        public Vector2Int RawLeftStick { get; private set; }
        public Vector2Int RawRightStick { get; private set; }

        private double m_lastReportTime;
        private IMUData m_lastIMUData;

        /// <summary>Gain of the sensor fusion, see <see cref="MadgwickFilter"/>.</summary>
        public float MadgwickBeta = 0.01f;
        private MadgwickFilter m_orientationFilter = MadgwickFilter.Identity;

        #region Generic data
        public BatteryLevelEnum BatteryLevel { get; protected set; } = BatteryLevelEnum.Empty;
        public bool BatteryIsCharging { get; protected set; } = false;
        public ControllerTypeEnum ControllerType { get; protected set; } = ControllerTypeEnum.JoyCon;
        public SpecificControllerTypeEnum SpecificControllerType { get; protected set; } = SpecificControllerTypeEnum.Unknown;

        /// <summary>
        /// The controller type implied by the layout the device was matched to. Unknown if the layout is generic.
        /// </summary>
        protected virtual SpecificControllerTypeEnum LayoutControllerType => SpecificControllerTypeEnum.Unknown;
        public bool IsPoweredBySwitchOrUSB { get; protected set; } = false;
        public string FirmwareVersion { get; protected set; } = "X.X";
        public string MACAddress { get; protected set; } = "XX.XX.XX.XX.XX.XX";
        public string SerialNumber { get; protected set; } = "N/A";
        #endregion

        #region Data re/loading
        // Are the different device informations loaded ?
        private bool m_IMUConfigDataLoaded = false;
        private bool m_stickConfigDataLoaded = false;
        private bool m_stickRestChecked = false;
        private bool m_deviceInfoLoaded = false;
        private bool m_colorsLoaded = false;
        private bool m_serialNumberLoaded = false;

        // Register the time of last request to retry to fetch them in case of timeout
        private double m_stickCalibrationTimeOfLastRequest;
        private double m_infoTimeOfLastRequest;
        private double m_colorsTimeOfLastRequest;
        private double m_serialNumberTimeOfLastRequest;

        private const double timeout = 1.0;
        #endregion

        #region Calibration Process
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
        #endregion

        #region Unity InputDevice boilerplate

        /// <summary>
        /// The last used/added Joy-Con (R) controller.
        /// </summary>
        public static SwitchControllerHID current { get; private set; }

        /// <summary>
        /// Log the exchanges with the controllers to the console. Warnings and errors are always logged.
        /// </summary>
        public static bool VerboseLogging { get; set; }

        private static void LogVerbose(string message)
        {
            if (VerboseLogging)
                Debug.Log(message);
        }

        /// <summary>
        /// Since InputSystem 1.20 the editor-side manager is created by InputSystemEditorInitializer's
        /// [InitializeOnLoad] constructor, which may run after ours. Force it to run first so that
        /// RegisterLayout does not hit a null manager.
        /// </summary>
        protected static void EnsureInputSystemInitialized()
        {
#if UNITY_EDITOR
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(UnityEngine.InputSystem.Editor.InputSystemEditorInitializer).TypeHandle);
#endif
        }

        /// <inheritdoc />
        public unsafe void OnStateEvent(InputEventPtr eventPtr)
        {
            StateEvent.FromUnchecked(eventPtr)->stateFormat = new FourCC("SCVS");
            InputState.Change(this, eventPtr);
        }

        /// <inheritdoc />
        public bool GetStateOffsetForEvent(InputControl control, InputEventPtr eventPtr, ref uint offset)
        {
            return false;
        }

        /// <inheritdoc />
        protected override void FinishSetup()
        {
            base.FinishSetup();

            buttonWest = GetChildControl<ButtonControl>("buttonWest");
            buttonNorth = GetChildControl<ButtonControl>("buttonNorth");
            buttonSouth = GetChildControl<ButtonControl>("buttonSouth");
            buttonEast = GetChildControl<ButtonControl>("buttonEast");
            angularVelocity = GetChildControl<Vector3Control>("angularVelocity");
            orientation = GetChildControl<Vector3Control>("orientation");
            acceleration = GetChildControl<Vector3Control>("acceleration");
            deviceRotation = GetChildControl<QuaternionControl>("deviceRotation");
            uncalibratedAngularVelocity = GetChildControl<Vector3Control>("uncalibratedAngularVelocity");
            uncalibratedAcceleration = GetChildControl<Vector3Control>("uncalibratedAcceleration");

            dpad = GetChildControl<DpadControl>("dpad");

            leftShoulder = GetChildControl<ButtonControl>("leftShoulder");
            rightShoulder = GetChildControl<ButtonControl>("rightShoulder");
            leftStickPress = GetChildControl<ButtonControl>("leftStickPress");
            rightStickPress = GetChildControl<ButtonControl>("rightStickPress");
            leftTrigger = GetChildControl<ButtonControl>("leftTrigger");
            rightTrigger = GetChildControl<ButtonControl>("rightTrigger");
            start = GetChildControl<ButtonControl>("start");
            select = GetChildControl<ButtonControl>("select");
            leftShoulderMini = GetChildControl<ButtonControl>("leftShoulderMini");
            rightShoulderMini = GetChildControl<ButtonControl>("rightShoulderMini");

            leftStick = GetChildControl<StickControl>("leftStick");
            rightStick = GetChildControl<StickControl>("rightStick");
        }

        /// <inheritdoc />
        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }
        #endregion

        #region Adding, removal and late info updates on the device
        /// <inheritdoc />
        protected override void OnAdded()
        {
            base.OnAdded();

            if (LayoutControllerType != SpecificControllerTypeEnum.Unknown)
                SpecificControllerType = LayoutControllerType;

            // Set the controller to 60Hz reports mode instead of button events mode
            // TODO: Check if it's the one that fails sometimes and if so, try to put it on NextUpdate with a timeout like the others
            SetInputReportMode(InputModeEnum.Standard);

            // Let the controller breathe for a sec before asking its info, color and calibration datas
            m_colorsTimeOfLastRequest = InputRuntime.s_Instance.currentTime;
            m_infoTimeOfLastRequest = InputRuntime.s_Instance.currentTime;
            m_stickCalibrationTimeOfLastRequest = InputRuntime.s_Instance.currentTime;
            m_serialNumberTimeOfLastRequest = InputRuntime.s_Instance.currentTime;
        }

        /// <inheritdoc />
        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this)
                current = null;
        }

        /// <inheritdoc />
        public void OnNextUpdate()
        {
            // Handle calibration process first to ensure it's not blocked by other updates.
            if (CurrentCalibrationState == CalibrationState.InProgress)
            {
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

            if (!m_colorsLoaded)
            {
                UpdateColors();
                return;
            }
            if (!m_deviceInfoLoaded)
            {
                UpdateDeviceInfo();
                return;
            }
            if (!m_stickConfigDataLoaded)
            {
                UpdateStickCalibrationData();
                return;
            }
            if (!m_serialNumberLoaded)
            {
                UpdateSerialNumber();
                return;
            }
        }

        /// <summary>
        /// Update the devices color info if it is outdated
        /// </summary>
        private void UpdateColors()
        {
            double currentTime = InputRuntime.s_Instance.currentTime;

            // Are we waiting for a response?
            if (currentTime > m_colorsTimeOfLastRequest + timeout)
            {
                m_colorsTimeOfLastRequest = currentTime;
                ReadColors();
            }
        }

        private void UpdateDeviceInfo()
        {
            double currentTime = InputRuntime.s_Instance.currentTime;

            // Are we waiting for a response?
            if (currentTime > m_infoTimeOfLastRequest + timeout)
            {
                m_infoTimeOfLastRequest = currentTime;
                ReadControllerInfo();

                // Also grab the serial number, because it's a device info that is not in the device info section
                ReadSerialNumber();
            }
        }

        private void UpdateStickCalibrationData()
        {
            double currentTime = InputRuntime.s_Instance.currentTime;

            if (currentTime > m_stickCalibrationTimeOfLastRequest + timeout)
            {
                m_stickCalibrationTimeOfLastRequest = currentTime;
                ReadStickCalibrationData();
            }
        }

        private void UpdateSerialNumber()
        {
            double currentTime = InputRuntime.s_Instance.currentTime;

            if (currentTime > m_serialNumberTimeOfLastRequest + timeout)
            {
                m_serialNumberTimeOfLastRequest = currentTime;
                ReadSerialNumber();
            }
        }

        #endregion

        #region Public interactions with the controller (output reports)
        // Number of the output report, increased with every report sent to this controller
        private byte m_packetNumber;

        /// <summary>
        /// Send an output report to the controller. Every report carries rumble data and a subcommand,
        /// the neutral ones are sent for whichever is not given.
        /// </summary>
        /// <param name="description">What the report does, for the error logged when sending it fails.</param>
        private bool SendCommand(string description, SwitchControllerBaseSubcommand subcommand = null, SwitchControllerRumbleProfile? rumbleProfile = null)
        {
            var c = SwitchControllerCommand.Create(m_packetNumber++, rumbleProfile, subcommand);
            if (ExecuteCommand(ref c) >= 0)
                return true;

            Debug.LogError($"{description} failed");
            return false;
        }

        private void ReadSPIFlash(SPIFlashReadAddressEnum region, string description)
        {
            LogVerbose($"Requesting {description}...");
            var readSubcommand = new SwitchControllerReadSPIFlashSubcommand(atAddress: (uint)region, withLength: SPIFlashRegion.Length(region));
            SendCommand($"Read {description}", readSubcommand);
        }

        public void Rumble(SwitchControllerRumbleProfile rumbleProfile)
        {
            SendCommand("Rumble command", rumbleProfile: rumbleProfile);
        }

        public void ReadControllerInfo()
        {
            LogVerbose("Requesting device info...");
            SendCommand("Request device info", new SwitchControllerRequestInfoSubcommand());
        }

        public void SetInputReportMode(InputModeEnum mode)
        {
            SendCommand($"Set report mode to {mode}", new SwitchControllerInputModeSubcommand(mode));
            m_IMUConfigDataLoaded = (mode == InputModeEnum.Standard);
        }

        public void SetIMUEnabled(bool active)
        {
            SendCommand($"Set IMU active to {active}", new SwitchControllerSetImuEnabledSubcommand { Enabled = active });
        }

        public void SetVibrationEnabled(bool active)
        {
            SendCommand($"Set vibration active to {active}", new SwitchControllerSetVibrationEnabledSubcommand { Enabled = active });
        }

        public void SetLEDs(
            LEDStatusEnum p1 = LEDStatusEnum.Off,
            LEDStatusEnum p2 = LEDStatusEnum.Off,
            LEDStatusEnum p3 = LEDStatusEnum.Off,
            LEDStatusEnum p4 = LEDStatusEnum.Off)
        {
            SendCommand("Set LEDs", new SwitchControllerSetLEDSubcommand(p1, p2, p3, p4));
        }

        public void ReadFactoryIMUCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.FactoryIMUCalibration, "factory IMU calibration");

        public void ReadUserIMUCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.UserIMUCalibration, "user IMU calibration");

        public void ReadColors() => ReadSPIFlash(SPIFlashReadAddressEnum.ColorData, "colors");

        public void ReadStickCalibrationData() => ReadSPIFlash(SPIFlashReadAddressEnum.FactoryStickCalibration, "factory stick calibration");

        public void ReadSerialNumber() => ReadSPIFlash(SPIFlashReadAddressEnum.SerialNumber, "serial number");

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
        /// Calibrates the IMU by setting the current raw sensor values as the new base/offset.
        /// The controller should be on a flat, stable surface when this is called.
        /// This will write to the user calibration data section in the controller's SPI flash.
        /// </summary>
        private unsafe void WriteUserIMUCalibrationData(Vector3 averageAccel, Vector3 averageGyro)
        {
            LogVerbose($"Calibration finished. Average Accel: {averageAccel}, Average Gyro: {averageGyro}. Writing to controller...");

            // The sensitivities are written back unchanged, so they have to be the ones read from the controller.
            // Writing the zeroes we start with would leave the controller with an unusable user calibration.
            if (!calibrationData.imuCalibData.HasSensitivity)
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
        /// Resets the Yaw component of the orientation to zero, using the current direction as the new forward.
        /// </summary>
        public void ResetYaw()
        {
            var currentOrientationQuat = m_orientationFilter.Rotation;

            // Get the current yaw angle
            float currentYaw = currentOrientationQuat.eulerAngles.z;

            // Create a quaternion that represents the inverse of the current yaw rotation
            var yawCorrection = Quaternion.AngleAxis(-currentYaw, Vector3.forward);

            // Apply the correction to the current orientation
            m_orientationFilter.Rotation = yawCorrection * currentOrientationQuat;

            LogVerbose("Yaw has been reset.");
        }
        #endregion

        #region Input reports (answers from the controller)
        [StructLayout(LayoutKind.Explicit)]
        private struct SwitchControllerGenericInputReport
        {
            public static FourCC Format => new FourCC('H', 'I', 'D');
            [FieldOffset(0)] public byte reportId;
        }

        [StructLayout(LayoutKind.Explicit, Size = kSize)]
        private unsafe struct SwitchControllerSubcommandResponseInputReport
        {
            public static FourCC Format => new FourCC('H', 'I', 'D');
            public const byte kSize = 50;

            [FieldOffset(0)] public byte reportId;
            [FieldOffset(2)] public byte batteryAndConnectionInfo;

            [FieldOffset(13)] public byte ack;
            [FieldOffset(14)] public byte subcommandId;
            [FieldOffset(15)] public fixed byte replyData[35];
        }

        unsafe bool IEventPreProcessor.PreProcessEvent(InputEventPtr eventPtr)
        {
            // Use the virtual controller events as-is, and skip other delta state events
            if (eventPtr.type == DeltaStateEvent.Type)
                return DeltaStateEvent.FromUnchecked(eventPtr)->stateFormat == SwitchControllerVirtualInputState.Format;

            // Use all other non-state/non-delta-state events
            if (eventPtr.type != StateEvent.Type)
                return true;

            var stateEvent = StateEvent.FromUnchecked(eventPtr);
            var size = stateEvent->stateSizeInBytes;

            // If state format was the virtual controller, just use it
            if (stateEvent->stateFormat == SwitchControllerVirtualInputState.Format)
                return true;

            // Skip unrecognized state events?
            if (stateEvent->stateFormat != SwitchControllerGenericInputReport.Format || size < sizeof(SwitchControllerGenericInputReport))
                return false;

            var genericReport = (SwitchControllerGenericInputReport*)stateEvent->state;

            // Simple report mode!
            if (genericReport->reportId == (byte)InputModeEnum.Simple)
            {
                LogVerbose("PreProcessEvent: Simple report mode");
                SetInputReportMode(InputModeEnum.Standard);
                return false;
            }

            // Subcommand reply!
            else if (genericReport->reportId == (byte)InputModeEnum.ReadSubcommands)
            {
                LogVerbose("PreProcessEvent: Subcommand report mode");
                var data = ((SwitchControllerSubcommandResponseInputReport*)stateEvent->state);
                HandleSubcommand(*data);
              return false;
            }

            // Full report mode!
            else if (genericReport->reportId == (byte)InputModeEnum.Standard)
            {
                HandleFullReport(stateEvent);
                return true;
            }

            else if (genericReport->reportId == (byte)InputModeEnum.NFCOrIR)
            {
                LogVerbose("NFC or infra-red report");
                return false;
            }
            else
            {
                LogVerbose($"Unknown report ID: {genericReport->reportId:X2}");
            }
            return false;
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
            if (SpecificControllerType != SpecificControllerTypeEnum.RightJoyCon)
                SetStickCenter(ref calibrationData.lStickCalibData, RawLeftStick);
            if (SpecificControllerType != SpecificControllerTypeEnum.LeftJoyCon)
                SetStickCenter(ref calibrationData.rStickCalibData, RawRightStick);
            m_stickRestChecked = true;
            LogVerbose($"{displayName}: sticks recentered");
        }

        private unsafe void HandleFullReport(StateEvent* stateEvent)
        {
            // Several reports can be processed in the same update, so the time between two reports
            // has to come from the events themselves and not from the time of the update.
            var reportTime = stateEvent->baseEvent.time;
            var deltaTime = (float)(reportTime - m_lastReportTime);
            // For the first report, or when the timestamps are unusable, fall back to the controller's
            // report interval (approx. 60Hz -> 15ms).
            if (m_lastReportTime <= 0 || deltaTime < 0.001f || deltaTime > 0.1f)
                deltaTime = 0.015f;
            m_lastReportTime = reportTime;

            SwitchControllerFullInputReport* fullInputReport = ((SwitchControllerFullInputReport*)stateEvent->state);
            UpdateBatteryAndConnectionInfo(fullInputReport->batteryAndConnectionInfo);
            m_lastIMUData = fullInputReport->imuData0ms; // Store the latest raw IMU data
            RawLeftStick = fullInputReport->RawLeftStick;
            RawRightStick = fullInputReport->RawRightStick;

            if (m_stickConfigDataLoaded && !m_stickRestChecked)
            {
                m_stickRestChecked = true;
                if (SpecificControllerType != SpecificControllerTypeEnum.RightJoyCon)
                    RecenterStickIfOffCenter(ref calibrationData.lStickCalibData, fullInputReport->RawLeftStick, "Left");
                if (SpecificControllerType != SpecificControllerTypeEnum.LeftJoyCon)
                    RecenterStickIfOffCenter(ref calibrationData.rStickCalibData, fullInputReport->RawRightStick, "Right");
            }
            var hidInputReport = fullInputReport->ToHIDInputReport(ref calibrationData, SpecificControllerType);

            // Sensor fusion using Madgwick's algorithm
            if (m_IMUConfigDataLoaded)
            {
                // Madgwick's algorithm expects gyroscope data in radians per second.
                m_orientationFilter.Update(hidInputReport.angularVelocity * Mathf.Deg2Rad, hidInputReport.acceleration, MadgwickBeta, deltaTime);
            }
            hidInputReport.deviceRotation = m_orientationFilter.Rotation;
            hidInputReport.orientation = hidInputReport.deviceRotation.eulerAngles;
            *((SwitchControllerVirtualInputState*)stateEvent->state) = hidInputReport;
            stateEvent->stateFormat = SwitchControllerVirtualInputState.Format;
        }

        /// <summary>
        /// Every input report carries the battery level in its high nibble and the connection info in its low nibble.
        /// </summary>
        private void UpdateBatteryAndConnectionInfo(byte batteryAndConnectionInfo)
        {
            BatteryLevel = (BatteryLevelEnum)((batteryAndConnectionInfo & 0xE0) >> 4);
            BatteryIsCharging = (batteryAndConnectionInfo & 0x10) != 0;

            int connectionInfo = batteryAndConnectionInfo & 0x0F;
            ControllerType = (ControllerTypeEnum)((connectionInfo >> 1) & 3);
            IsPoweredBySwitchOrUSB = (connectionInfo & 1) == 1;
        }

        private unsafe void HandleSubcommand(SwitchControllerSubcommandResponseInputReport response)
        {
            UpdateBatteryAndConnectionInfo(response.batteryAndConnectionInfo);

            SubcommandIDEnum subcommandReplyId = (SubcommandIDEnum)response.subcommandId;
            var subcommandWasAcknowledged = (response.ack & 0x80) != 0;

            LogVerbose($"Subcommand response for {subcommandReplyId}: {response.ack:X2}");

            if (subcommandWasAcknowledged)
            {
                switch (subcommandReplyId)
                {
                    case SubcommandIDEnum.RequestDeviceInfo:
                        HandleDeviceInfo(response.replyData);
                        break;
                    case SubcommandIDEnum.SPIFlashRead:
                        HandleFlashRead(response.replyData);
                        break;
                    default:
                        break;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DeviceInfo
        {
            public byte firmwareVersionMaj;
            public byte firmwareVersionMin;
            public byte joyConType;
            public byte unknown1;
            public fixed byte macAddress[6];
            public byte unknown2;
            public byte useColorsFromSPI;
        }

        private unsafe void HandleDeviceInfo(byte* dataPtr)
        {
            DeviceInfo deviceInfo = (DeviceInfo)Marshal.PtrToStructure((IntPtr)dataPtr, typeof(DeviceInfo));

            // Third party controllers sometimes report the wrong type here, so the type given by
            // the matched layout (i.e. the product ID) wins over what the controller says about itself.
            var reportedType = (SpecificControllerTypeEnum)deviceInfo.joyConType;
            SpecificControllerType = LayoutControllerType != SpecificControllerTypeEnum.Unknown ? LayoutControllerType : reportedType;
            if (reportedType != SpecificControllerType)
                Debug.LogWarning($"{displayName} reports itself as {reportedType}, treating it as {SpecificControllerType}");
            FirmwareVersion = String.Format($"{deviceInfo.firmwareVersionMaj:X1}.{deviceInfo.firmwareVersionMin:X2}");
            MACAddress = String.Format($"{deviceInfo.macAddress[0]:X2}:{deviceInfo.macAddress[1]:X2}:{deviceInfo.macAddress[2]:X2}:{deviceInfo.macAddress[3]:X2}:{deviceInfo.macAddress[4]:X2}:{deviceInfo.macAddress[5]:X2}");

            LogVerbose($"Firmware version is: {FirmwareVersion}, Joy-Con type is {SpecificControllerType}");
            m_deviceInfoLoaded = true;
        }

        [StructLayout(LayoutKind.Explicit, Size = 3)]
        private struct Rgb24Bit
        {
            [FieldOffset(0)] public byte r;
            [FieldOffset(1)] public byte g;
            [FieldOffset(2)] public byte b;

            public Color ToUnityColor()
            {
                return new Color32(r, g, b, 255);
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 3 * 4)]
        private struct ControllerColors
        {
            [FieldOffset(0)] public Rgb24Bit bodyColor;
            [FieldOffset(3)] public Rgb24Bit buttonColor;
            [FieldOffset(6)] public Rgb24Bit leftGripColor;
            [FieldOffset(9)] public Rgb24Bit rightGripColor;
        }

        private unsafe void HandleFlashRead(byte* dataPtr)
        {
            // The reply repeats the requested address (little endian) and length, followed by the data
            uint address = (uint)(dataPtr[0] | dataPtr[1] << 8 | dataPtr[2] << 16 | dataPtr[3] << 24);
            byte length = dataPtr[4];
            byte* response = dataPtr + 5;
            var region = (SPIFlashReadAddressEnum)address;

            // Some third party controllers only return the body and button colors
            if (region == SPIFlashReadAddressEnum.ColorData && length == 0x06)
            {
                LogVerbose("Read controller color data (body and buttons only)...");
                BodyColor = new Color32(response[0], response[1], response[2], 255);
                ButtonColor = new Color32(response[3], response[4], response[5], 255);
                m_colorsLoaded = true;
                return;
            }

            if (length == 0 || length != SPIFlashRegion.Length(region))
            {
                LogVerbose($"Unrecognized range: 0x{address:X4}-0x{address + length:X2} (length is {length:X2})");
                return;
            }

            LogVerbose($"Read {region}...");
            switch (region)
            {
                // Serial number in NON-extended ASCII
                case SPIFlashReadAddressEnum.SerialNumber:
                    DecodeSerialNumberData(response);
                    m_serialNumberLoaded = true;
                    break;

                case SPIFlashReadAddressEnum.FactoryIMUCalibration:
                    DecodeIMUCalibrationData((ushort*)response);
                    break;

                case SPIFlashReadAddressEnum.FactoryStickCalibration:
                    DecodeStickCalibrationData(response);
                    break;

                case SPIFlashReadAddressEnum.ColorData:
                    DecodeColorData(response);
                    break;

                case SPIFlashReadAddressEnum.UserIMUCalibration:
                    // The user calibration data starts with the magic number 0xB2 0xA1 when it has been written.
                    if (response[0] == 0xB2 && response[1] == 0xA1)
                        DecodeIMUCalibrationData((ushort*)(response + 2));
                    else
                        Debug.LogWarning("User IMU calibration data does not contain the expected magic number. don't decode.");
                    break;

                default:
                    LogVerbose($"{region} is not decoded (not implemented)");
                    break;
            }
        }
        #endregion
        #region Data decoding (byte to text/)
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

            /// <summary>False as long as no calibration has been read from the controller.</summary>
            public bool HasSensitivity =>
                accelSensitivity.x != 0 && accelSensitivity.y != 0 && accelSensitivity.z != 0 &&
                gyroSensitivity.x != 0 && gyroSensitivity.y != 0 && gyroSensitivity.z != 0;

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

        private unsafe void DecodeSerialNumberData(byte* response)
        {
            StringBuilder snStringBuilder = new StringBuilder(15);

            LogVerbose($"First byte is {(*response):X2}");
            if ((*response) >= 0X80)
            {
                Debug.LogWarning("No valid serial number retrieved");
                return;
            }

            for (int i = 0; i < 16; i++)
            {
                byte* serialDigit = response + i;
                if (*serialDigit == 0x00)
                    continue;

                snStringBuilder.Append(Encoding.ASCII.GetString(serialDigit, 1));
            }

            SerialNumber = snStringBuilder.ToString();
            m_serialNumberLoaded = true; // for prevent read serial infinity loop 
        }

        private unsafe void DecodeIMUCalibrationData(ushort* response)
        {
            var newCalibData = IMUCalibrationData.FromResponse(response);

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
            // Keep the previous (default) values for sticks whose calibration data is unusable,
            // e.g. blank flash on a single Joy-Con's missing stick or on third party controllers.
            var previous = calibrationData;
            DecodeLeftStickData(response);
            DecodeRightStickData(response + 9);
            if (!calibrationData.lStickCalibData.IsValid)
                calibrationData.lStickCalibData = previous.lStickCalibData;
            if (!calibrationData.rStickCalibData.IsValid)
                calibrationData.rStickCalibData = previous.rStickCalibData;

            LogVerbose($"Calibration data loaded");
            m_stickConfigDataLoaded = true;
            m_stickRestChecked = false;
        }

        private unsafe void DecodeColorData(byte* response)
        {
            ControllerColors colors = (ControllerColors)Marshal.PtrToStructure((IntPtr)response, typeof(ControllerColors));
            BodyColor = colors.bodyColor.ToUnityColor();
            ButtonColor = colors.buttonColor.ToUnityColor();
            LeftGripColor = colors.leftGripColor.ToUnityColor();
            RightGripColor = colors.rightGripColor.ToUnityColor();
            m_colorsLoaded = true;

            LogVerbose($"Colors loaded: {BodyColor}, {ButtonColor}");
        }

        /// <summary>
        /// Decode the calibration part of the packet into human readable values.
        /// </summary>
        /// <param name="stickCal">Array of 9 bytes.</param>
        /// <returns>Array of 6 ushort values meaning different wether it is for left or right stick (see <see cref="DecodeLeftStickData"/> and <see cref="DecodeRightStickData"/> </returns>
        private unsafe ushort[] DecodeStickData(byte* stickCal)
        {
            // yoinked from
            // https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md#analog-stick-factory-and-user-calibration
            ushort[] data = new ushort[6];
            data[0] = (ushort)((stickCal[1] << 8) & 0xF00 | stickCal[0]);
            data[1] = (ushort)((stickCal[2] << 4) | (stickCal[1] >> 4));
            data[2] = (ushort)((stickCal[4] << 8) & 0xF00 | stickCal[3]);
            data[3] = (ushort)((stickCal[5] << 4) | (stickCal[4] >> 4));
            data[4] = (ushort)((stickCal[7] << 8) & 0xF00 | stickCal[6]);
            data[5] = (ushort)((stickCal[8] << 4) | (stickCal[7] >> 4));
            return data;
        }

        /// <summary>
        /// Decode the calibration part of the packet for the left stick.
        /// </summary>
        /// <param name="lStickCal">Array of 9 bytes.</param>
        private unsafe void DecodeLeftStickData(byte* lStickCal)
        {
            ushort[] decoded = DecodeStickData(lStickCal);

            ushort xAxisMaxAboveCenter = decoded[0];
            ushort yAxisMaxAboveCenter = decoded[1];
            ushort xAxisCenter = decoded[2];
            ushort yAxisCenter = decoded[3];
            ushort xAxisMinBelowCenter = decoded[4];
            ushort yAxisMinBelowCenter = decoded[5];


            ushort lStickXMin = (ushort)(xAxisCenter - xAxisMinBelowCenter);
            ushort lStickXMax = (ushort)(xAxisCenter + xAxisMaxAboveCenter);

            ushort lStickYMin = (ushort)(yAxisCenter - yAxisMinBelowCenter);
            ushort lStickYMax = (ushort)(yAxisCenter + yAxisMaxAboveCenter);

            calibrationData.lStickCalibData = new StickCalibrationData()
            {
                xMin = lStickXMin,
                xMax = lStickXMax,
                yMin = lStickYMin,
                yMax = lStickYMax,
                xCenter = xAxisCenter,
                yCenter = yAxisCenter
            };
        }

        private unsafe void DecodeRightStickData(byte* rStickCal)
        {
            ushort[] decoded = DecodeStickData(rStickCal);

            ushort xAxisCenter = decoded[0];
            ushort yAxisCenter = decoded[1];
            ushort xAxisMinBelowCenter = decoded[2];
            ushort yAxisMinBelowCenter = decoded[3];
            ushort xAxisMaxAboveCenter = decoded[4];
            ushort yAxisMaxAboveCenter = decoded[5];

            ushort rStickXMin = (ushort)(xAxisCenter - xAxisMinBelowCenter);
            ushort rStickXMax = (ushort)(xAxisCenter + xAxisMaxAboveCenter);

            ushort rStickYMin = (ushort)(yAxisCenter - yAxisMinBelowCenter);
            ushort rStickYMax = (ushort)(yAxisCenter + yAxisMaxAboveCenter);

            calibrationData.rStickCalibData = new StickCalibrationData()
            {
                xMin = rStickXMin,
                xMax = rStickXMax,
                yMin = rStickYMin,
                yMax = rStickYMax,
                xCenter = xAxisCenter,
                yCenter = yAxisCenter
            };
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

            public override string ToString()
            {
                return String.Format($"Center: ({xCenter:X2};{yCenter:X2}), X range: [{xMin:X2}-{xMax:X2}], Y range: [{yMin:X2}-{yMax:X2}]");
            }
        }
        #endregion
    }
}