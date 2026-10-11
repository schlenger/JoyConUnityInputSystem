using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.Switch
{
    // Loading of the device information after a controller connects, and handling of the controller's replies.
    public abstract partial class SwitchControllerHID
    {
        /// <summary>
        /// What is read from the controller after it connects, in the order it is requested.
        /// </summary>
        private enum SetupStep
        {
            Colors,
            DeviceInfo,
            StickCalibration,
            SerialNumber,
            FactoryIMUCalibration,
            UserIMUCalibration,
            EnableIMU,
            Done
        }

        // Are the different device informations loaded ?
        private bool m_stickConfigDataLoaded = false;
        private bool m_deviceInfoLoaded = false;
        private bool m_colorsLoaded = false;
        private bool m_serialNumberLoaded = false;
        private bool m_factoryIMUCalibrationLoaded = false;
        private bool m_userIMUCalibrationLoaded = false;
        private bool m_imuEnabled = false;

        // The step that was requested last, when and how often, to request it again in case of timeout
        private SetupStep m_setupStep;
        private double m_timeOfLastRequest;
        private int m_setupAttempts;

        // Not every controller has an IMU or answers these requests (third party ones in particular),
        // so the IMU steps are given up on after a few attempts.
        private const int maxIMUSetupAttempts = 3;
        private const double timeout = 1.0;

        private SetupStep GetPendingSetupStep()
        {
            if (!m_colorsLoaded)
                return SetupStep.Colors;
            if (!m_deviceInfoLoaded)
                return SetupStep.DeviceInfo;
            if (!m_stickConfigDataLoaded)
                return SetupStep.StickCalibration;
            if (!m_serialNumberLoaded)
                return SetupStep.SerialNumber;
            if (!m_factoryIMUCalibrationLoaded)
                return SetupStep.FactoryIMUCalibration;
            if (!m_userIMUCalibrationLoaded)
                return SetupStep.UserIMUCalibration;
            if (!m_imuEnabled)
                return SetupStep.EnableIMU;
            return SetupStep.Done;
        }

        /// <summary>
        /// Request the next missing piece of device information, one at a time.
        /// A request is sent again when the controller has not answered it within the timeout.
        /// </summary>
        private void UpdateSetup()
        {
            SetupStep step = GetPendingSetupStep();
            if (step == SetupStep.Done)
                return;

            double currentTime = InputRuntime.s_Instance.currentTime;

            if (step != m_setupStep)
            {
                m_setupStep = step;
                m_setupAttempts = 0;
            }
            // Are we waiting for a response?
            else if (currentTime <= m_timeOfLastRequest + timeout)
                return;

            if (step >= SetupStep.FactoryIMUCalibration && m_setupAttempts >= maxIMUSetupAttempts)
            {
                Debug.LogWarning($"{displayName}: no answer from the controller for {step}, giving up on the IMU setup");
                m_factoryIMUCalibrationLoaded = true;
                m_userIMUCalibrationLoaded = true;
                m_imuEnabled = true;
                return;
            }

            m_setupAttempts++;
            m_timeOfLastRequest = currentTime;

            switch (step)
            {
                case SetupStep.Colors:
                    ReadColors();
                    break;
                case SetupStep.DeviceInfo:
                    ReadControllerInfo();
                    break;
                case SetupStep.StickCalibration:
                    ReadStickCalibrationData();
                    break;
                case SetupStep.SerialNumber:
                    ReadSerialNumber();
                    break;
                // The user calibration is read after the factory one, to replace it if there is one
                case SetupStep.FactoryIMUCalibration:
                    ReadFactoryIMUCalibrationData();
                    break;
                case SetupStep.UserIMUCalibration:
                    ReadUserIMUCalibrationData();
                    break;
                case SetupStep.EnableIMU:
                    SetIMUEnabled(true);
                    break;
            }
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
                    case SubcommandIDEnum.EnableDisableIMU:
                        m_imuEnabled = true;
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
                    m_factoryIMUCalibrationLoaded = true;
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
                        LogVerbose("No user IMU calibration on the controller, keeping the factory one");
                    m_userIMUCalibrationLoaded = true;
                    break;

                default:
                    LogVerbose($"{region} is not decoded (not implemented)");
                    break;
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
    }
}
