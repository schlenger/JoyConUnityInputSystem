using System.Runtime.InteropServices;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Switch.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.Switch
{
    // The device itself: its controls and state, and the conversion of the controller's input reports.
    // The other parts of the class are in the SwitchControllerHID.*.cs files.
    [InputControlLayout(stateType = typeof(SwitchControllerVirtualInputState))]
    public abstract partial class SwitchControllerHID : InputDevice, IInputStateCallbackReceiver, IEventPreProcessor
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

        #region Generic data
        public BatteryLevelEnum BatteryLevel { get; protected set; } = BatteryLevelEnum.Empty;
        public bool BatteryIsCharging { get; protected set; } = false;
        public ControllerTypeEnum ControllerType { get; protected set; } = ControllerTypeEnum.JoyCon;
        public SpecificControllerTypeEnum SpecificControllerType { get; protected set; } = SpecificControllerTypeEnum.Unknown;

        /// <summary>
        /// The controller type implied by the layout the device was matched to. Unknown if the layout is generic.
        /// </summary>
        protected virtual SpecificControllerTypeEnum LayoutControllerType => SpecificControllerTypeEnum.Unknown;

        /// <summary>A single Joy-Con only has the stick of its side.</summary>
        public bool HasLeftStick => SpecificControllerType != SpecificControllerTypeEnum.RightJoyCon;
        public bool HasRightStick => SpecificControllerType != SpecificControllerTypeEnum.LeftJoyCon;

        public bool IsPoweredBySwitchOrUSB { get; protected set; } = false;
        public string FirmwareVersion { get; protected set; } = "X.X";
        public string MACAddress { get; protected set; } = "XX.XX.XX.XX.XX.XX";
        public string SerialNumber { get; protected set; } = "N/A";
        #endregion

        #region Stick and IMU state
        /// <summary>Latest uncalibrated 12 bit stick positions as sent by the controller.</summary>
        public Vector2Int RawLeftStick { get; private set; }
        public Vector2Int RawRightStick { get; private set; }

        /// <summary>Gain of the sensor fusion, see <see cref="MadgwickFilter"/>.</summary>
        public float MadgwickBeta = 0.01f;
        private MadgwickFilter m_orientationFilter = MadgwickFilter.Identity;

        private double m_lastReportTime;
        private IMUData m_lastIMUData;
        private bool m_isInStandardReportMode = false;
        #endregion

        #region Unity InputDevice boilerplate
        /// <summary>
        /// The last used/added Switch controller of any type.
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
        private static void EnsureInputSystemInitialized()
        {
#if UNITY_EDITOR
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(UnityEngine.InputSystem.Editor.InputSystemEditorInitializer).TypeHandle);
#endif
        }

        private const int nintendoVendorId = 0x57E;

        /// <summary>
        /// Register the layout of a controller type, to be used for the HID devices with the given product ID.
        /// </summary>
        /// <param name="matchAnyProductName">
        /// Also match on the product name, whatever it is. The extra pattern makes the layout win over
        /// a built-in one that only matches the same vendor and product ID.
        /// </param>
        protected static void RegisterControllerLayout<TDevice>(int productId, bool matchAnyProductName = false)
            where TDevice : SwitchControllerHID
        {
            EnsureInputSystemInitialized();

            var matcher = new InputDeviceMatcher()
                .WithInterface("HID")
                .WithCapability("vendorId", nintendoVendorId)
                .WithCapability("productId", productId);
            if (matchAnyProductName)
                matcher = matcher.WithProduct(".+");

            InputSystem.RegisterLayout<TDevice>(matches: matcher);
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

        #region Adding, removal and update of the device
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
            m_timeOfLastRequest = InputRuntime.s_Instance.currentTime;
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
            UpdateIMUCalibration();
            UpdateSetup();
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
                if (HasLeftStick)
                    RecenterStickIfOffCenter(ref calibrationData.lStickCalibData, RawLeftStick, "Left");
                if (HasRightStick)
                    RecenterStickIfOffCenter(ref calibrationData.rStickCalibData, RawRightStick, "Right");
            }
            var hidInputReport = fullInputReport->ToHIDInputReport(ref calibrationData, HasLeftStick, HasRightStick);

            // Sensor fusion using Madgwick's algorithm
            if (m_isInStandardReportMode)
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
        #endregion
    }
}
